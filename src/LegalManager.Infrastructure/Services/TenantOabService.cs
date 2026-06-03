using System.Text.RegularExpressions;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

public class TenantOabService : ITenantOabService
{
    private static readonly HashSet<string> UfsValidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC","AL","AP","AM","BA","CE","DF","ES","GO","MA",
        "MT","MS","MG","PA","PB","PR","PE","PI","RJ","RN",
        "RS","RO","RR","SC","SP","SE","TO"
    };

    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly IEscavadorService _escavador;
    private readonly ILogger<TenantOabService> _logger;

    public TenantOabService(
        AppDbContext context,
        ITenantContext tenant,
        IEscavadorService escavador,
        ILogger<TenantOabService> logger)
    {
        _context = context;
        _tenant = tenant;
        _escavador = escavador;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TenantOabDto>> ListarAsync(CancellationToken ct = default)
    {
        return await _context.TenantOabs
            .AsNoTracking()
            .Where(o => o.TenantId == _tenant.TenantId)
            .OrderBy(o => o.Nome)
            .Select(o => ToDto(o))
            .ToListAsync(ct);
    }

    public async Task<TenantOabDto> CriarAsync(CriarTenantOabRequest req, CancellationToken ct = default)
    {
        ValidarPlano();
        ValidarLimiteOabs();
        ValidarOab(req.Uf, req.Numero);

        var uf = req.Uf.ToUpperInvariant();
        var numero = Regex.Replace(req.Numero, @"\D", "");

        var duplicado = await _context.TenantOabs.AnyAsync(
            o => o.TenantId == _tenant.TenantId && o.Uf == uf && o.Numero == numero, ct);
        if (duplicado)
            throw new InvalidOperationException($"Já existe uma OAB cadastrada para {uf}/{numero}.");

        var oab = new TenantOab
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            UserId = req.UserId,
            Uf = uf,
            Numero = numero,
            Nome = req.Nome.Trim(),
            Ativo = req.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        _context.TenantOabs.Add(oab);
        await _context.SaveChangesAsync(ct);

        // Sync remoto (best-effort: falha não derruba o cadastro local)
        await SincronizarRemotoAsync(oab);

        _logger.LogInformation("[TenantOab] Criada OAB {Uf}/{Numero} para tenant {TenantId}", uf, numero, _tenant.TenantId);
        return ToDto(oab);
    }

    public async Task<TenantOabDto> AtualizarAsync(Guid id, AtualizarTenantOabRequest req, CancellationToken ct = default)
    {
        ValidarPlano();
        ValidarOab(req.Uf, req.Numero);

        var oab = await _context.TenantOabs
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == _tenant.TenantId, ct)
            ?? throw new KeyNotFoundException("OAB não encontrada.");

        // Se estamos ATIVANDO uma OAB inativa, validar o limite antes de prosseguir.
        // (Desativar não tem restrição; atualizar mantendo o estado ativo também não.)
        if (!oab.Ativo && req.Ativo)
        {
            await ValidarLimiteOabs();
        }

        var uf = req.Uf.ToUpperInvariant();
        var numero = Regex.Replace(req.Numero, @"\D", "");

        var duplicado = await _context.TenantOabs.AnyAsync(
            o => o.Id != id && o.TenantId == _tenant.TenantId && o.Uf == uf && o.Numero == numero, ct);
        if (duplicado)
            throw new InvalidOperationException($"Já existe uma OAB cadastrada para {uf}/{numero}.");

        // Detecta mudança em campos que invalidam o monitoramento remoto
        var remoteChanged = oab.Uf != uf || oab.Numero != numero || oab.Nome != req.Nome.Trim();
        var ativoChanged = oab.Ativo != req.Ativo;

        oab.UserId = req.UserId;
        oab.Uf = uf;
        oab.Numero = numero;
        oab.Nome = req.Nome.Trim();
        oab.Ativo = req.Ativo;

        if (remoteChanged || ativoChanged)
        {
            // Recria o monitoramento remoto com os novos dados
            await RemoverMonitoramentoRemotoAsync(oab);
            if (oab.Ativo)
                await SincronizarRemotoAsync(oab);
            else
            {
                oab.EscavadorMonitoramentoId = null;
                oab.SyncError = null;
            }
        }

        await _context.SaveChangesAsync(ct);
        return ToDto(oab);
    }

    public async Task RemoverAsync(Guid id, CancellationToken ct = default)
    {
        var oab = await _context.TenantOabs
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == _tenant.TenantId, ct)
            ?? throw new KeyNotFoundException("OAB não encontrada.");

        await RemoverMonitoramentoRemotoAsync(oab);
        _context.TenantOabs.Remove(oab);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<TenantOabDto> SincronizarAsync(Guid id, CancellationToken ct = default)
    {
        ValidarPlano();
        var oab = await _context.TenantOabs
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == _tenant.TenantId, ct)
            ?? throw new KeyNotFoundException("OAB não encontrada.");

        await RemoverMonitoramentoRemotoAsync(oab);
        if (oab.Ativo)
            await SincronizarRemotoAsync(oab);
        else
        {
            oab.EscavadorMonitoramentoId = null;
            oab.SyncError = null;
        }
        await _context.SaveChangesAsync(ct);
        return ToDto(oab);
    }

    public async Task<int> SincronizarTodasAsync(CancellationToken ct = default)
    {
        // Itera todas as OABs ativas sem EscavadorMonitoramentoId e tenta criar remoto
        var oabsSemSync = await _context.TenantOabs
            .Where(o => o.TenantId == _tenant.TenantId
                        && o.Ativo
                        && o.EscavadorMonitoramentoId == null)
            .ToListAsync(ct);

        var ok = 0;
        foreach (var oab in oabsSemSync)
        {
            await SincronizarRemotoAsync(oab);
            if (oab.EscavadorMonitoramentoId.HasValue) ok++;
        }
        await _context.SaveChangesAsync(ct);
        return ok;
    }

    public async Task MarcarVerificadasAsync(IEnumerable<Guid> ids, DateTime quando, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;
        await _context.TenantOabs
            .Where(o => o.TenantId == _tenant.TenantId && idList.Contains(o.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.UltimaVerificacao, quando), ct);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task SincronizarRemotoAsync(TenantOab oab)
    {
        try
        {
            var mon = await _escavador.CriarMonitoramentoOabAsync(oab.Uf, oab.Numero, oab.Nome);
            if (mon != null)
            {
                oab.EscavadorMonitoramentoId = mon.Id;
                oab.UltimoSyncEm = DateTime.UtcNow;
                oab.SyncError = null;
            }
            else
            {
                oab.SyncError = "Escavador retornou null ao criar monitoramento";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TenantOab] Erro ao sincronizar OAB {Id} com Escavador", oab.Id);
            oab.SyncError = ex.Message;
        }
    }

    private async Task RemoverMonitoramentoRemotoAsync(TenantOab oab)
    {
        if (!oab.EscavadorMonitoramentoId.HasValue) return;
        try
        {
            await _escavador.RemoverMonitoramentoAsync(oab.EscavadorMonitoramentoId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TenantOab] Falha ao remover monitoramento remoto {Id} (continuando)",
                oab.EscavadorMonitoramentoId);
            // Não bloqueia — se a remoção remota falhar, o remoto fica órfão mas o local segue consistente
        }
    }

    private static TenantOabDto ToDto(TenantOab o) => new(
        o.Id, o.UserId, null, // UserNome resolvido separadamente
        o.Uf, o.Numero, o.Nome, o.Ativo, o.CriadoEm, o.UltimaVerificacao,
        o.EscavadorMonitoramentoId, o.UltimoSyncEm, o.SyncError,
        Sincronizada: o.EscavadorMonitoramentoId.HasValue && string.IsNullOrEmpty(o.SyncError));

    private void ValidarPlano()
    {
        if (!PlanoRestricoes.PermiteCapturacaoPublicacoes(_tenant.Plano))
            throw new UnauthorizedAccessException(
                "O cadastro de OABs para captura de publicações está disponível a partir do plano Pro.");
    }

    private async Task ValidarLimiteOabs()
    {
        var limite = PlanoRestricoes.MaxOabsMonitoradas(_tenant.Plano);
        if (limite <= 0)
        {
            // Plano não permite nenhuma OAB — ValidarPlano já tratou, mas por segurança
            throw new UnauthorizedAccessException(
                "Seu plano não permite cadastro de OABs monitoradas.");
        }
        var atuais = await _context.TenantOabs
            .CountAsync(o => o.TenantId == _tenant.TenantId && o.Ativo);
        if (atuais >= limite)
        {
            throw new InvalidOperationException(
                $"Limite de OABs monitoradas atingido para o seu plano ({limite}). " +
                $"Faça upgrade para cadastrar mais.");
        }
    }

    private static void ValidarOab(string uf, string numero)
    {
        if (string.IsNullOrWhiteSpace(uf))
            throw new ArgumentException("UF é obrigatória.", nameof(uf));
        if (!UfsValidas.Contains(uf))
            throw new ArgumentException($"UF inválida: {uf}.", nameof(uf));

        var digits = Regex.Replace(numero ?? "", @"\D", "");
        if (digits.Length < 3 || digits.Length > 7)
            throw new ArgumentException("Número de OAB inválido (esperado entre 3 e 7 dígitos).", nameof(numero));
    }
}
