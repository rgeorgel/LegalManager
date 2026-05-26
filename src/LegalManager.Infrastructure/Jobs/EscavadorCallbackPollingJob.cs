using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Fallback para quando o webhook do Escavador não for recebido (ex: dev local sem URL pública).
/// Consulta callbacks pendentes a cada hora, processa e marca como recebidos.
/// </summary>
public class EscavadorCallbackPollingJob
{
    private readonly AppDbContext _context;
    private readonly IEscavadorService _escavador;
    private readonly IEmailService _emailService;
    private readonly ILogger<EscavadorCallbackPollingJob> _logger;

    public EscavadorCallbackPollingJob(
        AppDbContext context,
        IEscavadorService escavador,
        IEmailService emailService,
        ILogger<EscavadorCallbackPollingJob> logger)
    {
        _context = context;
        _escavador = escavador;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        _logger.LogInformation("[EscavadorCallbackPollingJob] Iniciando polling de callbacks");

        var pagina = 1;
        var continuar = true;
        var processados = 0;

        while (continuar)
        {
            var resultado = await _escavador.ListarCallbacksPendentesAsync(pagina);
            if (resultado.Data.Count == 0) break;

            var idsParaMarcar = new List<long>();

            foreach (var cb in resultado.Data)
            {
                try
                {
                    await ProcessarCallbackAsync(cb);
                    idsParaMarcar.Add(cb.Id);
                    processados++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[EscavadorCallbackPollingJob] Erro ao processar callback {Id}", cb.Id);
                }
            }

            if (idsParaMarcar.Count > 0)
                await _escavador.MarcarCallbacksRecebidosAsync(idsParaMarcar);

            continuar = resultado.TemProxima;
            pagina++;
        }

        _logger.LogInformation("[EscavadorCallbackPollingJob] Concluído: {N} callbacks processados", processados);
    }

    private async Task ProcessarCallbackAsync(EscavadorCallbackDto cb)
    {
        if (string.IsNullOrWhiteSpace(cb.NumeroCNJ)) return;

        var processo = await EncontrarProcessoAsync(cb);
        if (processo == null) return;

        var jaExiste = await _context.Andamentos.AnyAsync(a =>
            a.ProcessoId == processo.Id &&
            a.Descricao == cb.Descricao &&
            a.Data == (cb.DataAndamento ?? cb.CriadoEm.Date));

        if (jaExiste) return;

        var agora = DateTime.Now;
        var andamento = new Andamento
        {
            Id = Guid.NewGuid(),
            ProcessoId = processo.Id,
            TenantId = processo.TenantId,
            Data = cb.DataAndamento ?? agora,
            Tipo = MapearTipo(cb.Descricao),
            Descricao = cb.Descricao ?? "Atualização via Escavador",
            Fonte = FonteAndamento.Automatico,
            CriadoEm = agora
        };

        _context.Andamentos.Add(andamento);
        processo.UltimoMonitoramento = agora;

        await _context.SaveChangesAsync();
        await NotificarAsync(processo, andamento, agora);
    }

    private async Task<Processo?> EncontrarProcessoAsync(EscavadorCallbackDto cb)
    {
        if (cb.MonitoramentoId.HasValue)
        {
            var monId = cb.MonitoramentoId.Value.ToString();
            var por = await _context.Processos
                .FirstOrDefaultAsync(p => p.EscavadorMonitoramentoId == monId);
            if (por != null) return por;
        }

        return await _context.Processos
            .FirstOrDefaultAsync(p => p.NumeroCNJ == cb.NumeroCNJ);
    }

    private async Task NotificarAsync(Processo processo, Andamento andamento, DateTime agora)
    {
        if (processo.AdvogadoResponsavelId == null) return;

        _context.Notificacoes.Add(new Notificacao
        {
            Id = Guid.NewGuid(),
            TenantId = processo.TenantId,
            UsuarioId = processo.AdvogadoResponsavelId.Value,
            Tipo = TipoNotificacao.NovoAndamento,
            Titulo = $"Novo andamento — {processo.NumeroCNJ}",
            Mensagem = andamento.Descricao,
            Url = $"/pages/processo-detalhe.html?id={processo.Id}",
            Lida = false,
            CriadaEm = agora
        });

        var advogado = await _context.Users
            .Where(u => u.Id == processo.AdvogadoResponsavelId)
            .Select(u => new { u.Nome, u.Email })
            .FirstOrDefaultAsync();

        if (advogado?.Email != null)
        {
            try
            {
                await _emailService.EnviarNovoAndamentoAsync(
                    advogado.Email, advogado.Nome,
                    processo.NumeroCNJ, andamento.Descricao);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao enviar e-mail de andamento Escavador");
            }
        }

        await _context.SaveChangesAsync();
    }

    private static TipoAndamento MapearTipo(string? descricao) =>
        descricao?.ToLowerInvariant() switch
        {
            var s when s?.Contains("despacho") == true => TipoAndamento.Despacho,
            var s when s?.Contains("decis") == true => TipoAndamento.Decisao,
            var s when s?.Contains("senten") == true => TipoAndamento.Sentenca,
            var s when s?.Contains("acórd") == true || s?.Contains("acord") == true => TipoAndamento.Acordao,
            var s when s?.Contains("audiên") == true || s?.Contains("audien") == true => TipoAndamento.Audiencia,
            var s when s?.Contains("intim") == true => TipoAndamento.Intimacao,
            var s when s?.Contains("public") == true => TipoAndamento.Publicacao,
            var s when s?.Contains("petic") == true => TipoAndamento.Peticao,
            _ => TipoAndamento.Outro
        };
}
