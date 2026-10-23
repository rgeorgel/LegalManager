using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Escavador;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Backstop do webhook do Escavador: lista os callbacks cuja entrega esgotou as tentativas
/// (status <c>erro</c> — ex.: o servidor estava fora do ar) e os processa pelo mesmo caminho do
/// webhook. Substitui o polling diário por processo (<see cref="EscavadorMovimentacoesPollingJob"/>),
/// que é cobrado por consulta; a listagem de callbacks não aparece na tabela de preços.
/// Callbacks ainda <c>em_tentativa</c> ficam de fora: o próprio Escavador vai reenviá-los.
/// </summary>
public class EscavadorCallbacksPendentesJob
{
    /// <summary>Janela que cobre um fim de semana inteiro com o webhook fora do ar.</summary>
    public static readonly TimeSpan Janela = TimeSpan.FromDays(7);
    private const int MaxPaginas = 50;

    private readonly AppDbContext _context;
    private readonly IEscavadorService _escavador;
    private readonly EscavadorCallbackProcessor _processor;
    private readonly ILogger<EscavadorCallbacksPendentesJob> _logger;

    public EscavadorCallbacksPendentesJob(
        AppDbContext context,
        IEscavadorService escavador,
        EscavadorCallbackProcessor processor,
        ILogger<EscavadorCallbacksPendentesJob> logger)
    {
        _context = context;
        _escavador = escavador;
        _processor = processor;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(EscavadorCallbacksPendentesJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("job.cron", "escavador-callbacks-pendentes");

        // Não é consulta de tenant (não vai para Consultas Externas); o custo fica no log.
        using var creditos = CreditosConsultaExterna.Medir();
        var desde = DateTime.UtcNow - Janela;
        var processados = new List<long>();
        var erros = 0;
        string? cursor = null;

        for (var pagina = 0; pagina < MaxPaginas; pagina++)
        {
            var resultado = await _escavador.ListarCallbacksAsync(desde, status: "erro", cursor: cursor);
            foreach (var cb in resultado.Data)
            {
                try
                {
                    var payload = cb.ResultadoJson != null ? EscavadorCallbackProcessor.Desserializar(cb.ResultadoJson) : null;
                    if (payload != null)
                    {
                        payload.Uuid ??= cb.Uuid;
                        await _processor.ProcessarAsync(payload);
                    }
                    processados.Add(cb.Id);
                }
                catch (Exception ex)
                {
                    // Entidades que falharam não podem contaminar o SaveChanges dos próximos callbacks.
                    _context.ChangeTracker.Clear();
                    erros++;
                    _logger.LogError(ex, "[EscavadorCallbacks] Erro ao processar callback {Id}", cb.Id);
                }
            }

            cursor = resultado.ProximoCursor;
            if (cursor == null) break;
        }

        // Só os processados saem da lista; o que deu erro aqui volta na próxima execução.
        await _escavador.MarcarCallbacksRecebidosAsync(processados);
        _logger.LogInformation("[EscavadorCallbacks] Concluído: {N} callbacks processados, {E} erros, {C} centavos em créditos",
            processados.Count, erros, creditos.Total ?? 0);
    }
}
