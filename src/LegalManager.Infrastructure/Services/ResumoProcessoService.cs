using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace LegalManager.Infrastructure.Services;

public class ResumoProcessoService : IResumoProcessoService
{
    private readonly AppDbContext _context;
    private readonly IIAService _iaService;
    private readonly ICreditoService _creditoService;
    private readonly ITenantContext _tenantContext;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<ResumoProcessoService> _logger;

    public ResumoProcessoService(
        AppDbContext context,
        IIAService iaService,
        ICreditoService creditoService,
        ITenantContext tenantContext,
        IBackgroundJobClient jobs,
        ILogger<ResumoProcessoService> logger)
    {
        _context = context;
        _iaService = iaService;
        _creditoService = creditoService;
        _tenantContext = tenantContext;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task<ResumoProcessoResponseDto> SolicitarAsync(GerarResumoDto dto, Guid usuarioId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        if (!await _creditoService.TemCreditoDisponivelAsync(TipoCreditoAI.TraducaoAndamento, 1, ct))
            throw new InvalidOperationException("Créditos de IA insuficientes.");

        if (!await _context.Processos.AnyAsync(p => p.Id == dto.ProcessoId && p.TenantId == tenantId, ct))
            throw new InvalidOperationException("Processo não encontrado.");

        var limite = DateTime.UtcNow - TempoMaximoGerando;
        var emAndamento = await _context.ResumosProcesso
            .Include(r => r.GeradoPor)
            .Where(r => r.ProcessoId == dto.ProcessoId && r.TenantId == tenantId
                        && r.Status == StatusResumoProcesso.Gerando && r.GeradoEm > limite)
            .OrderByDescending(r => r.GeradoEm)
            .FirstOrDefaultAsync(ct);
        if (emAndamento != null) return ParaDto(emAndamento);

        var resumo = new ResumoProcesso
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcessoId = dto.ProcessoId,
            GeradoPorId = usuarioId,
            Status = StatusResumoProcesso.Gerando,
            GeradoEm = DateTime.UtcNow
        };
        _context.ResumosProcesso.Add(resumo);
        await _context.SaveChangesAsync(ct);

        _jobs.Enqueue<ResumoProcessoJob>(j => j.ExecutarAsync(resumo.Id, CancellationToken.None));

        resumo.GeradoPor = (await _context.Users.FindAsync([usuarioId], ct))!;
        return ParaDto(resumo);
    }

    public async Task ProcessarAsync(Guid resumoId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var resumo = await _context.ResumosProcesso
            .Include(r => r.Processo).ThenInclude(p => p.Partes).ThenInclude(pt => pt.Contato)
            .FirstOrDefaultAsync(r => r.Id == resumoId && r.TenantId == tenantId, ct);
        if (resumo == null || resumo.Status != StatusResumoProcesso.Gerando) return;

        try
        {
            var contexto = await MontarContextoAsync(resumo.Processo, ct);
            var conteudo = await GerarComNovaTentativaAsync(contexto, ct);
            await _creditoService.ConsumirCreditoAsync(TipoCreditoAI.TraducaoAndamento, 1, ct);

            resumo.Conteudo = conteudo;
            resumo.Status = StatusResumoProcesso.Concluido;
            resumo.MensagemErro = null;
            resumo.GeradoEm = DateTime.UtcNow;
            Notificar(resumo, "Resumo do processo pronto",
                $"O resumo por IA do processo {resumo.Processo.NumeroCNJ} está pronto.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Resumo de processo {Id}: falha ao gerar", resumoId);
            resumo.Status = StatusResumoProcesso.Erro;
            resumo.MensagemErro = ex is TimeoutException or IAIndisponivelException or InvalidOperationException
                ? ex.Message
                : "Não foi possível gerar o resumo. Tente novamente.";
            Notificar(resumo, "Falha ao gerar o resumo do processo",
                $"Não foi possível gerar o resumo por IA do processo {resumo.Processo.NumeroCNJ}: {resumo.MensagemErro}");
        }

        await _context.SaveChangesAsync(ct);
    }

    /// <summary>A IA às vezes demora demais ou volta sem resposta; uma segunda tentativa costuma resolver.</summary>
    private async Task<string> GerarComNovaTentativaAsync(string contexto, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                var conteudo = await _iaService.GerarResumoProcessoAsync(contexto, ct);
                if (!string.IsNullOrWhiteSpace(conteudo)) return conteudo;
                if (tentativa >= TentativasIA)
                    throw new IAIndisponivelException("A IA devolveu um resumo vazio. Tente novamente.");
            }
            catch (Exception ex) when (ex is TimeoutException or IAIndisponivelException && tentativa < TentativasIA)
            {
                _logger.LogInformation(ex, "Resumo de processo: tentativa {Tentativa} falhou, tentando de novo", tentativa);
            }
        }
    }

    private void Notificar(ResumoProcesso resumo, string titulo, string mensagem)
    {
        _context.Notificacoes.Add(new Notificacao
        {
            Id = Guid.NewGuid(),
            TenantId = resumo.TenantId,
            UsuarioId = resumo.GeradoPorId,
            Tipo = TipoNotificacao.Geral,
            Titulo = titulo,
            Mensagem = mensagem,
            Url = $"/pages/processo-detalhe.html?id={resumo.ProcessoId}&resumo={resumo.Id}",
            CriadaEm = DateTime.UtcNow,
            ChaveDedup = $"resumo-processo:{resumo.Id}"
        });
    }

    private async Task<string> MontarContextoAsync(Processo processo, CancellationToken ct)
    {
        var andamentos = await _context.Andamentos
            .Where(a => a.ProcessoId == processo.Id)
            .OrderByDescending(a => a.Data)
            .Take(20)
            .ToListAsync(ct);

        var documentos = await _context.Documentos
            .Where(d => d.ProcessoId == processo.Id)
            .Select(d => new { d.Nome, d.Tipo })
            .ToListAsync(ct);

        var tarefas = await _context.Tarefas
            .Where(t => t.ProcessoId == processo.Id && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada)
            .Select(t => new { t.Titulo, t.Status, t.Prazo, t.Prioridade })
            .ToListAsync(ct);

        var parteNomes = processo.Partes?.Select(p =>
            $"{p.TipoParte}: {p.Contato?.Nome ?? "—"}") ?? [];

        var sb = new StringBuilder();
        sb.AppendLine($"Número CNJ: {processo.NumeroCNJ}");
        sb.AppendLine($"Tribunal: {processo.Tribunal ?? "—"}");
        sb.AppendLine($"Vara: {processo.Vara ?? "—"}");
        sb.AppendLine($"Comarca: {processo.Comarca ?? "—"}");
        sb.AppendLine($"Área do Direito: {processo.AreaDireito}");
        sb.AppendLine($"Fase: {processo.Fase}");
        sb.AppendLine($"Status: {processo.Status}");
        if (processo.TipoAcao != null) sb.AppendLine($"Tipo de Ação: {processo.TipoAcao}");
        if (processo.ValorCausa.HasValue) sb.AppendLine($"Valor da Causa: R$ {processo.ValorCausa?.ToString("N2", BrasiliaTime.PtBr)}");
        if (processo.Classe != null) sb.AppendLine($"Classe: {processo.Classe}");
        if (processo.Assuntos != null) sb.AppendLine($"Assuntos: {processo.Assuntos}");
        if (processo.Observacoes != null) sb.AppendLine($"Observações: {processo.Observacoes}");

        sb.AppendLine();
        sb.AppendLine("=== PARTES ===");
        var partesList = parteNomes.ToList();
        sb.AppendLine(partesList.Count > 0 ? string.Join("\n", partesList) : "Nenhuma parte cadastrada.");

        sb.AppendLine();
        sb.AppendLine("=== ANDAMENTOS (mais recentes) ===");
        if (andamentos.Count > 0)
        {
            foreach (var a in andamentos)
                sb.AppendLine($"[{a.Data.ToString("dd/MM/yyyy", BrasiliaTime.PtBr)}] {a.Tipo}: {a.Descricao}");
        }
        else
        {
            sb.AppendLine("Nenhum andamento registrado.");
        }

        sb.AppendLine();
        sb.AppendLine("=== DOCUMENTOS ===");
        if (documentos.Count > 0)
            foreach (var d in documentos)
                sb.AppendLine($"- {d.Nome} ({d.Tipo})");
        else
            sb.AppendLine("Nenhum documento anexado.");

        sb.AppendLine();
        sb.AppendLine("=== TAREFAS EM ABERTO ===");
        if (tarefas.Count > 0)
        {
            foreach (var t in tarefas)
            {
                var prazo = t.Prazo.HasValue ? $", prazo: {t.Prazo?.ToString("dd/MM/yyyy", BrasiliaTime.PtBr)}" : "";
                sb.AppendLine($"- [{t.Prioridade}] {t.Titulo} ({t.Status}{prazo})");
            }
        }
        else
        {
            sb.AppendLine("Nenhuma tarefa em aberto.");
        }

        return sb.ToString();
    }

    public async Task<ResumoProcessoResponseDto?> ObterAsync(Guid resumoId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var resumo = await _context.ResumosProcesso
            .Include(r => r.GeradoPor)
            .FirstOrDefaultAsync(r => r.Id == resumoId && r.TenantId == tenantId, ct);
        return resumo == null ? null : ParaDto(resumo);
    }

    /// <summary>
    /// Resumo parado em Gerando há mais que isso = o job morreu (ex.: reinício do servidor sem
    /// retomada); aparece como erro para o usuário poder pedir outro.
    /// </summary>
    public static readonly TimeSpan TempoMaximoGerando = TimeSpan.FromMinutes(15);
    private const int TentativasIA = 2;

    private static ResumoProcessoResponseDto ParaDto(ResumoProcesso r)
    {
        // Lido do banco, GeradoEm vem com Kind=Local (Npgsql legacy timestamp): normaliza antes de comparar.
        var interrompido = r.Status == StatusResumoProcesso.Gerando
                           && r.GeradoEm.ToUniversalTime() < DateTime.UtcNow - TempoMaximoGerando;
        return new ResumoProcessoResponseDto(
            r.Id,
            r.ProcessoId,
            r.Conteudo,
            r.GeradoPor?.UserName ?? "Usuário",
            r.GeradoEm,
            r.VisivelCliente,
            interrompido ? nameof(StatusResumoProcesso.Erro) : r.Status.ToString(),
            interrompido ? "A geração do resumo foi interrompida. Tente novamente." : r.MensagemErro);
    }

    public async Task<IEnumerable<ResumoProcessoResponseDto>> ListarAsync(Guid processoId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        var resumos = await _context.ResumosProcesso
            .Where(r => r.ProcessoId == processoId && r.TenantId == tenantId)
            .OrderByDescending(r => r.GeradoEm)
            .Include(r => r.GeradoPor)
            .ToListAsync(ct);

        return resumos.Select(ParaDto);
    }

    public async Task<ResumoProcessoResponseDto> SetVisivelClienteAsync(Guid resumoId, bool visivel, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        var resumo = await _context.ResumosProcesso
            .Include(r => r.GeradoPor)
            .FirstOrDefaultAsync(r => r.Id == resumoId && r.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("Resumo não encontrado.");
        if (resumo.Status != StatusResumoProcesso.Concluido)
            throw new InvalidOperationException("Só é possível compartilhar um resumo concluído.");

        resumo.VisivelCliente = visivel;
        await _context.SaveChangesAsync(ct);

        return ParaDto(resumo);
    }
}
