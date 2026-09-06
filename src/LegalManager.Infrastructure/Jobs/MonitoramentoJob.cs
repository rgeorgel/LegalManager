using System.Diagnostics;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

public class MonitoramentoJob
{
    private readonly AppDbContext _context;
    private readonly ITribunalAdapter _dataJud;
    private readonly IEmailService _emailService;
    private readonly ILogger<MonitoramentoJob> _logger;

    public MonitoramentoJob(
        AppDbContext context,
        ITribunalAdapter dataJud,
        IEmailService emailService,
        ILogger<MonitoramentoJob> logger)
    {
        _context = context;
        _dataJud = dataJud;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(MonitoramentoJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("job.cron", "monitoramento-processos");
        _logger.LogInformation("[MonitoramentoJob] Iniciando monitoramento automático.");
        var agora = DateTime.Now;

        var processos = await _context.Processos
            .Where(p => p.Monitorado && p.Status == StatusProcesso.Ativo)
            .ToListAsync();

        int total = 0, novosTotal = 0, erros = 0;

        foreach (var processo in processos)
        {
            total++;
            try
            {
                var novos = await MonitorarProcessoAsync(processo, agora);
                novosTotal += novos;
            }
            catch (Exception ex)
            {
                erros++;
                _logger.LogError(ex, "[MonitoramentoJob] Erro no processo {Id} ({CNJ})",
                    processo.Id, processo.NumeroCNJ);
            }
        }

        _logger.LogInformation(
            "[MonitoramentoJob] Concluído. Total={Total} NovosAndamentos={Novos} Erros={Erros}",
            total, novosTotal, erros);
    }

    private async Task<int> MonitorarProcessoAsync(Processo processo, DateTime agora)
    {
        Application.Interfaces.TribunalConsultaResult consulta;

        if (!string.IsNullOrWhiteSpace(processo.Tribunal))
            consulta = await _dataJud.ConsultarPorTribunalAsync(processo.NumeroCNJ, processo.Tribunal);
        else
            consulta = await _dataJud.ConsultarAsync(processo.NumeroCNJ);

        if (!consulta.Encontrado)
        {
            processo.UltimoMonitoramento = agora;
            await _context.SaveChangesAsync();
            return 0;
        }

        var datasExistentes = await _context.Andamentos
            .Where(a => a.ProcessoId == processo.Id)
            .Select(a => a.Data)
            .ToHashSetAsync();

        var novosAndamentos = new List<Andamento>();
        foreach (var mov in consulta.Movimentos)
        {
            if (datasExistentes.Contains(mov.Data)) continue;

            var dadosExtras = new
            {
                Complementos = mov.Complementos,
                ComplementosNaoEstruturados = mov.ComplementosNaoEstruturados,
                CamposNaoEstruturados = mov.CamposNaoEstruturados
            };
            var dadosExtrasJson = mov.Complementos?.Count > 0 || mov.ComplementosNaoEstruturados?.Count > 0 || mov.CamposNaoEstruturados?.Count > 0
                ? JsonSerializer.Serialize(dadosExtras)
                : null;

            novosAndamentos.Add(new Andamento
            {
                Id = Guid.NewGuid(),
                ProcessoId = processo.Id,
                TenantId = processo.TenantId,
                Data = mov.Data,
                Tipo = MapearTipo(mov.TipoNome),
                Descricao = mov.Descricao,
                Fonte = FonteAndamento.Automatico,
                CriadoEm = agora,
                CodigoCNJ = mov.CodigoCNJ,
                OrgaoJulgador = mov.OrgaoJulgador,
                DadosExtras = dadosExtrasJson
            });
        }

        if (novosAndamentos.Count > 0)
        {
            _context.Andamentos.AddRange(novosAndamentos);
            await NotificarAsync(processo, novosAndamentos, agora);
        }

        if (string.IsNullOrWhiteSpace(processo.Tribunal) && !string.IsNullOrWhiteSpace(consulta.NomeTribunal))
            processo.Tribunal = consulta.NomeTribunal;

        if (!string.IsNullOrWhiteSpace(consulta.Vara))
            processo.Vara = consulta.Vara;
        if (!string.IsNullOrWhiteSpace(consulta.Comarca))
            processo.Comarca = consulta.Comarca;
        if (!string.IsNullOrWhiteSpace(consulta.Classe))
            processo.Classe = consulta.Classe;
        if (consulta.Assuntos != null && consulta.Assuntos.Count > 0)
            processo.Assuntos = string.Join("; ", consulta.Assuntos);
        if (consulta.DataAjuizamento.HasValue)
            processo.DataAjuizamento = consulta.DataAjuizamento;
        if (consulta.DataDistribuicao.HasValue)
            processo.DataDistribuicao = consulta.DataDistribuicao;
        if (!string.IsNullOrWhiteSpace(consulta.Grau))
            processo.Grau = consulta.Grau;
        if (!string.IsNullOrWhiteSpace(consulta.SiglaTribunal))
            processo.SiglaTribunal = consulta.SiglaTribunal;
        if (!string.IsNullOrWhiteSpace(consulta.Segmento))
            processo.Segmento = consulta.Segmento;
        if (consulta.ValorCaixa.HasValue)
            processo.ValorCausa = consulta.ValorCaixa;
        if (!string.IsNullOrWhiteSpace(consulta.Ementa))
            processo.Ementa = consulta.Ementa;
        if (!string.IsNullOrWhiteSpace(consulta.Decisao))
            processo.DecisaoDataJud = consulta.Decisao;
        if (!string.IsNullOrWhiteSpace(consulta.Observacao))
            processo.Observacao = consulta.Observacao;
        if (!string.IsNullOrWhiteSpace(consulta.Relator))
            processo.Relator = consulta.Relator;
        if (!string.IsNullOrWhiteSpace(consulta.TipoDecisao))
            processo.TipoDecisao = consulta.TipoDecisao;
        if (!string.IsNullOrWhiteSpace(consulta.ResultadoJulgamento))
            processo.ResultadoJulgamento = consulta.ResultadoJulgamento;
        if (consulta.CodigoClasse.HasValue)
            processo.CodigoClasse = consulta.CodigoClasse;
        if (consulta.NivelSigilo.HasValue)
            processo.NivelSigilo = consulta.NivelSigilo;
        if (!string.IsNullOrWhiteSpace(consulta.Instancia))
            processo.Instancia = consulta.Instancia;
        if (consulta.DataJulgamento.HasValue)
            processo.DataJulgamento = consulta.DataJulgamento;
        if (consulta.DataPublicacao.HasValue)
            processo.DataPublicacao = consulta.DataPublicacao;
        if (consulta.DataHoraUltimaAtualizacao.HasValue)
            processo.UltimaAtualizacaoDataJud = consulta.DataHoraUltimaAtualizacao;

        processo.UltimoMonitoramento = agora;
        await _context.SaveChangesAsync();
        return novosAndamentos.Count;
    }

    private async Task NotificarAsync(Processo processo, List<Andamento> novos, DateTime agora)
    {
        if (processo.AdvogadoResponsavelId == null) return;

        _context.Notificacoes.Add(new Notificacao
        {
            Id = Guid.NewGuid(),
            TenantId = processo.TenantId,
            UsuarioId = processo.AdvogadoResponsavelId!.Value,
            Tipo = TipoNotificacao.NovoAndamento,
            Titulo = $"Novo andamento — {processo.NumeroCNJ}",
            Mensagem = $"{novos.Count} novo(s) andamento(s) no processo {processo.NumeroCNJ}.",
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
                    processo.NumeroCNJ, novos[0].Descricao);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao enviar e-mail de andamento");
            }
        }
    }

    private static TipoAndamento MapearTipo(string nome) =>
        nome?.ToLowerInvariant() switch
        {
            var s when s?.Contains("despacho") == true   => TipoAndamento.Despacho,
            var s when s?.Contains("decis") == true      => TipoAndamento.Decisao,
            var s when s?.Contains("senten") == true     => TipoAndamento.Sentenca,
            var s when s?.Contains("acórd") == true ||
                       s?.Contains("acord") == true      => TipoAndamento.Acordao,
            var s when s?.Contains("audiên") == true ||
                       s?.Contains("audien") == true     => TipoAndamento.Audiencia,
            var s when s?.Contains("intim") == true      => TipoAndamento.Intimacao,
            var s when s?.Contains("public") == true     => TipoAndamento.Publicacao,
            var s when s?.Contains("petic") == true ||
                       s?.Contains("petic") == true      => TipoAndamento.Peticao,
            _ => TipoAndamento.Outro
        };
}