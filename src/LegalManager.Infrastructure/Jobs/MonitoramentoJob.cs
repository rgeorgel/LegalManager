using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
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
            await NotificarAsync(processo, novosAndamentos, agora);
        }

        if (string.IsNullOrWhiteSpace(processo.Tribunal) && !string.IsNullOrWhiteSpace(consulta.NomeTribunal))
            processo.Tribunal = consulta.NomeTribunal;

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
