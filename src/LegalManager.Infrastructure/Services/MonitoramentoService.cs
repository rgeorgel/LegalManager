using LegalManager.Application.DTOs.Monitoramento;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

public class MonitoramentoService : IMonitoramentoService
{
    private readonly AppDbContext _context;
    private readonly DataJudAdapter _dataJud;
    private readonly ITenantContext _tenant;
    private readonly IEmailService _emailService;
    private readonly ILogger<MonitoramentoService> _logger;

    public MonitoramentoService(
        AppDbContext context,
        DataJudAdapter dataJud,
        ITenantContext tenant,
        IEmailService emailService,
        ILogger<MonitoramentoService> logger)
    {
        _context = context;
        _dataJud = dataJud;
        _tenant = tenant;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<bool> AlternarMonitoramentoAsync(Guid processoId, CancellationToken ct = default)
    {
        var processo = await _context.Processos
            .FirstOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == processoId, ct)
            ?? throw new KeyNotFoundException("Processo não encontrado.");

        processo.Monitorado = !processo.Monitorado;
        await _context.SaveChangesAsync(ct);
        return processo.Monitorado;
    }

    public async Task<MonitoramentoResultDto> MonitorarProcessoAsync(Guid processoId, CancellationToken ct = default)
    {
        var processo = await _context.Processos
            .FirstOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == processoId, ct)
            ?? throw new KeyNotFoundException("Processo não encontrado.");

        return await ExecutarMonitoramentoAsync(processo, ct);
    }

    public async Task<int> MonitorarTodosAsync(CancellationToken ct = default)
    {
        var processos = await _context.Processos
            .Where(p => p.Monitorado && p.Status == StatusProcesso.Ativo)
            .ToListAsync(ct);

        int totalNovos = 0;
        foreach (var processo in processos)
        {
            try
            {
                var result = await ExecutarMonitoramentoAsync(processo, ct);
                totalNovos += result.NovosAndamentos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao monitorar processo {Id}", processo.Id);
            }
        }
        return totalNovos;
    }

    private async Task<MonitoramentoResultDto> ExecutarMonitoramentoAsync(Processo processo, CancellationToken ct)
    {
        var agora = DateTime.Now;

        TribunalConsultaResult consulta;
        if (!string.IsNullOrWhiteSpace(processo.Tribunal))
            consulta = await _dataJud.ConsultarPorTribunalAsync(processo.NumeroCNJ, processo.Tribunal, ct);
        else
            consulta = await _dataJud.ConsultarAsync(processo.NumeroCNJ, ct);

        if (!consulta.Encontrado)
        {
            processo.UltimoMonitoramento = agora;
            await _context.SaveChangesAsync(ct);
            return new MonitoramentoResultDto(processo.Id, processo.NumeroCNJ, false, 0,
                "Processo não encontrado no DataJud.", agora);
        }

        var datasExistentes = await _context.Andamentos
            .Where(a => a.ProcessoId == processo.Id)
            .Select(a => a.Data)
            .ToHashSetAsync(ct);

        var novosAndamentos = new List<Andamento>();
        foreach (var mov in consulta.Movimentos)
        {
            if (datasExistentes.Contains(mov.Data)) continue;

            novosAndamentos.Add(new Andamento
            {
                Id = Guid.NewGuid(),
                ProcessoId = processo.Id,
                TenantId = processo.TenantId,
                Data = mov.Data,
                Tipo = MapearTipo(mov.TipoNome),
                Descricao = mov.Descricao,
                Fonte = FonteAndamento.Automatico,
                CriadoEm = agora
            });
        }

        if (novosAndamentos.Count > 0)
        {
            _context.Andamentos.AddRange(novosAndamentos);
            await NotificarNovosAndamentosAsync(processo, novosAndamentos, ct);
        }

        // Update tribunal/vara from DataJud if not set
        if (string.IsNullOrWhiteSpace(processo.Tribunal) && !string.IsNullOrWhiteSpace(consulta.NomeTribunal))
            processo.Tribunal = consulta.NomeTribunal;
        if (string.IsNullOrWhiteSpace(processo.Vara) && !string.IsNullOrWhiteSpace(consulta.Vara))
            processo.Vara = consulta.Vara;

        processo.UltimoMonitoramento = agora;
        await _context.SaveChangesAsync(ct);

        return new MonitoramentoResultDto(processo.Id, processo.NumeroCNJ, true,
            novosAndamentos.Count, null, agora);
    }

    private async Task NotificarNovosAndamentosAsync(
        Processo processo, List<Andamento> novos, CancellationToken ct)
    {
        if (processo.AdvogadoResponsavelId == null) return;

        var advogado = await _context.Users
            .Where(u => u.Id == processo.AdvogadoResponsavelId)
            .Select(u => new { u.Nome, u.Email })
            .FirstOrDefaultAsync(ct);
        if (advogado == null) return;

        var notificacao = new Notificacao
        {
            Id = Guid.NewGuid(),
            TenantId = processo.TenantId,
            UsuarioId = processo.AdvogadoResponsavelId!.Value,
            Tipo = TipoNotificacao.NovoAndamento,
            Titulo = $"Novo andamento — {processo.NumeroCNJ}",
            Mensagem = $"{novos.Count} novo(s) andamento(s) no processo {processo.NumeroCNJ}.",
            Url = $"/pages/processo-detalhe.html?id={processo.Id}",
            Lida = false,
            CriadaEm = DateTime.UtcNow
        };
        _context.Notificacoes.Add(notificacao);

        if (!string.IsNullOrEmpty(advogado.Email))
        {
            try
            {
                await _emailService.EnviarNovoAndamentoAsync(
                    advogado.Email, advogado.Nome,
                    processo.NumeroCNJ, novos[0].Descricao, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao enviar e-mail de novo andamento");
            }
        }
    }

    private static TipoAndamento MapearTipo(string nomeMovimento) =>
        nomeMovimento?.ToLowerInvariant() switch
        {
            var s when s?.Contains("despacho") == true   => TipoAndamento.Despacho,
            var s when s?.Contains("decisão") == true ||
                       s?.Contains("decisao") == true    => TipoAndamento.Decisao,
            var s when s?.Contains("sentença") == true ||
                       s?.Contains("sentenca") == true   => TipoAndamento.Sentenca,
            var s when s?.Contains("acórdão") == true ||
                       s?.Contains("acordao") == true    => TipoAndamento.Acordao,
            var s when s?.Contains("audiência") == true ||
                       s?.Contains("audiencia") == true  => TipoAndamento.Audiencia,
            var s when s?.Contains("intimação") == true ||
                       s?.Contains("intimacao") == true  => TipoAndamento.Intimacao,
            var s when s?.Contains("publicação") == true ||
                       s?.Contains("publicacao") == true => TipoAndamento.Publicacao,
            var s when s?.Contains("petição") == true ||
                       s?.Contains("peticao") == true    => TipoAndamento.Peticao,
            _ => TipoAndamento.Outro
        };
}
