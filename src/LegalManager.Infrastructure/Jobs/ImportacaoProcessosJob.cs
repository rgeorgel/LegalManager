using System.Text.Json;
using Hangfire;
using LegalManager.Application.DTOs.Importacoes;
using LegalManager.Application.DTOs.Onboarding;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Identity;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Executa uma <see cref="ImportacaoProcessos"/> em background: pesquisa todos os processos
/// da OAB (Escavador página a página, depois DataJud/e-SAJ), criando os itens conforme
/// encontra; depois importa item a item, gravando o progresso (lido pela barra de progresso
/// do frontend) e, no fim, notifica o usuário.
/// Retomável: se o servidor reiniciar no meio, o Hangfire reenfileira o job e ele segue do
/// cursor do Escavador / dos itens ainda pendentes.
/// </summary>
public class ImportacaoProcessosJob
{
    // Sem avanço há mais que isso = job morreu (ver ImportacoesController.Ativa).
    public static readonly TimeSpan TempoMaximoSemProgresso = TimeSpan.FromMinutes(30);

    // Teto de processos por importação (Escavador:MaxProcessosImportacaoOab): cada página de
    // 100 é uma consulta paga, e uma OAB gigante não pode gerar requisições sem fim.
    private const int MaxProcessosPadrao = 20_000;
    private static readonly TimeSpan[] EsperasEntreTentativas = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    private readonly AppDbContext _context;
    private readonly TenantContext _tenantContext;
    private readonly IImportadorProcessosOab _importador;
    private readonly ILogger<ImportacaoProcessosJob> _logger;
    private readonly int _maxProcessos;

    public ImportacaoProcessosJob(
        AppDbContext context,
        TenantContext tenantContext,
        IImportadorProcessosOab importador,
        IConfiguration configuration,
        ILogger<ImportacaoProcessosJob> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _importador = importador;
        _logger = logger;
        _maxProcessos = configuration.GetValue("Escavador:MaxProcessosImportacaoOab", MaxProcessosPadrao);
    }

    // Esperas entre tentativas de uma página; sobrescrito nos testes.
    internal Func<TimeSpan, CancellationToken, Task> Esperar { get; set; } = Task.Delay;

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecutarAsync(Guid importacaoId, CancellationToken ct)
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(ImportacaoProcessosJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("importacao.id", importacaoId);

        var importacao = await _context.ImportacoesProcessos.FirstOrDefaultAsync(i => i.Id == importacaoId, ct);
        if (importacao == null || importacao.Status is StatusImportacao.Concluida or StatusImportacao.Erro)
            return;

        var plano = await _context.Tenants
            .Where(t => t.Id == importacao.TenantId)
            .Select(t => t.Plano)
            .FirstOrDefaultAsync(ct);
        _tenantContext.Assumir(importacao.TenantId, importacao.UsuarioId, plano);

        try
        {
            if (importacao.Modo == ModoImportacao.Todos)
                await PesquisarAsync(importacaoId, ct);

            importacao = await RecarregarAsync(importacaoId, ct);
            importacao.Status = StatusImportacao.Importando;
            importacao.AtualizadoEm = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            var pendentes = await _context.ImportacaoProcessoItens
                .Where(i => i.ImportacaoId == importacaoId && i.Status == StatusItemImportacao.Pendente)
                .OrderBy(i => i.Ordem)
                .Select(i => i.Id)
                .ToListAsync(ct);

            foreach (var itemId in pendentes)
            {
                ct.ThrowIfCancellationRequested();
                await ImportarItemAsync(importacaoId, itemId, ct);
            }

            importacao = await RecarregarAsync(importacaoId, ct);
            importacao.Status = StatusImportacao.Concluida;
            importacao.ConcluidoEm = importacao.AtualizadoEm = DateTime.UtcNow;
            Notificar(importacao,
                "Importação de processos concluída",
                importacao.Total == 0
                    ? $"OAB {importacao.NumeroOab}/{importacao.Uf}: nenhum processo novo encontrado."
                    : $"OAB {importacao.NumeroOab}/{importacao.Uf}: {Resumo(importacao)}.");
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[ImportacaoProcessosJob] {Id} concluída: {Importados} importados, {Ja} já cadastrados, {Erros} erros de {Total}",
                importacaoId, importacao.Importados, importacao.JaCadastrados, importacao.Erros, importacao.Total);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Servidor desligando: o Hangfire reenfileira e o job retoma dos itens pendentes.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ImportacaoProcessosJob] Falha na importação {Id}", importacaoId);
            importacao = await RecarregarAsync(importacaoId, CancellationToken.None);
            importacao.Status = StatusImportacao.Erro;
            importacao.MensagemErro = "Falha inesperada durante a importação.";
            importacao.ConcluidoEm = importacao.AtualizadoEm = DateTime.UtcNow;
            Notificar(importacao,
                "Importação de processos interrompida",
                $"OAB {importacao.NumeroOab}/{importacao.Uf}: a importação falhou. {Resumo(importacao)}.");
            await _context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task PesquisarAsync(Guid importacaoId, CancellationToken ct)
    {
        var importacao = await RecarregarAsync(importacaoId, ct);
        if (importacao.BuscaEscavadorConcluida && importacao.BuscaTribunaisConcluida) return;

        importacao.Status = StatusImportacao.Buscando;
        importacao.AtualizadoEm = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        // Escavador primeiro: traz capa/partes prontas no JSON da busca e a importação desses
        // itens não chama nenhuma API. Os CNJs que só DataJud/e-SAJ acharem entram depois.
        while (!importacao.BuscaEscavadorConcluida)
        {
            if (importacao.Total >= _maxProcessos)
            {
                _logger.LogWarning("[ImportacaoProcessosJob] {Id}: teto de {Max} processos atingido; pesquisa no Escavador encerrada",
                    importacaoId, _maxProcessos);
                importacao.BuscaEscavadorConcluida = true;
                await _context.SaveChangesAsync(ct);
                break;
            }

            var cursor = importacao.CursorEscavador;
            var pagina = await ComTentativasAsync(
                () => _importador.BuscarPaginaEscavadorAsync(importacao.NumeroOab, importacao.Uf, cursor, ct), ct);

            importacao = await RecarregarAsync(importacaoId, ct);
            await AdicionarItensAsync(importacao, pagina.Processos, ct);
            importacao.CursorEscavador = pagina.ProximoCursor;
            importacao.BuscaEscavadorConcluida = pagina.ProximoCursor == null;
            importacao.AtualizadoEm = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        if (!importacao.BuscaTribunaisConcluida)
        {
            var previews = await ComTentativasAsync(
                () => _importador.BuscarTribunaisAsync(importacao.NumeroOab, importacao.Uf, ct), ct);

            importacao = await RecarregarAsync(importacaoId, ct);
            await AdicionarItensAsync(importacao, previews, ct);
            importacao.BuscaTribunaisConcluida = true;
            importacao.AtualizadoEm = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Cria itens para os processos ainda não cadastrados nem presentes nesta importação,
    /// respeitando o teto. Não salva — quem chama grava junto com o avanço da pesquisa.
    /// </summary>
    private async Task AdicionarItensAsync(
        ImportacaoProcessos importacao, List<ProcessoOabPreviewDto> previews, CancellationToken ct)
    {
        var jaNaImportacao = await _context.ImportacaoProcessoItens
            .Where(i => i.ImportacaoId == importacao.Id)
            .Select(i => i.NumeroCNJ)
            .ToHashSetAsync(ct);

        foreach (var p in previews)
        {
            if (importacao.Total >= _maxProcessos) break;
            var cnj = p.NumeroCNJ.Trim();
            if (p.JaCadastrado || !jaNaImportacao.Add(cnj)) continue;

            _context.ImportacaoProcessoItens.Add(NovoItem(importacao.Id, importacao.Total, ItemDoPreview(p), p.Tribunal));
            importacao.Total++;
        }
    }

    /// <summary>Falhas transitórias (rede, 5xx, rate limit) não derrubam uma pesquisa de dezenas de páginas.</summary>
    private async Task<T> ComTentativasAsync<T>(Func<Task<T>> acao, CancellationToken ct)
    {
        for (var tentativa = 0; ; tentativa++)
        {
            try
            {
                return await acao();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && tentativa < EsperasEntreTentativas.Length)
            {
                _logger.LogWarning(ex, "[ImportacaoProcessosJob] Falha na pesquisa (tentativa {N}); tentando de novo", tentativa + 1);
                await Esperar(EsperasEntreTentativas[tentativa], ct);
            }
        }
    }

    private async Task ImportarItemAsync(Guid importacaoId, Guid itemId, CancellationToken ct)
    {
        _context.ChangeTracker.Clear();
        var item = await _context.ImportacaoProcessoItens.FirstAsync(i => i.Id == itemId, ct);

        ResultadoImportacaoItem resultado;
        try
        {
            var dados = JsonSerializer.Deserialize<ImportarProcessoItem>(item.DadosJson)
                        ?? new ImportarProcessoItem(item.NumeroCNJ);
            resultado = await _importador.ImportarAsync(dados, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[ImportacaoProcessosJob] Erro inesperado no item {CNJ}", item.NumeroCNJ);
            resultado = new ResultadoImportacaoItem(StatusItemImportacao.Erro, "Erro ao importar");
        }

        // O importador pode ter deixado entidades rastreadas (ou falhadas); grava o
        // progresso a partir de um contexto limpo.
        _context.ChangeTracker.Clear();
        item = await _context.ImportacaoProcessoItens.FirstAsync(i => i.Id == itemId, ct);
        var importacao = await _context.ImportacoesProcessos.FirstAsync(i => i.Id == importacaoId, ct);

        item.Status = resultado.Status;
        item.Mensagem = resultado.Mensagem;
        item.ProcessoId = resultado.ProcessoId;
        item.ProcessadoEm = DateTime.UtcNow;

        importacao.Processados++;
        switch (resultado.Status)
        {
            case StatusItemImportacao.Importado: importacao.Importados++; break;
            case StatusItemImportacao.JaCadastrado: importacao.JaCadastrados++; break;
            default: importacao.Erros++; break;
        }
        importacao.AtualizadoEm = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    private async Task<ImportacaoProcessos> RecarregarAsync(Guid importacaoId, CancellationToken ct)
    {
        _context.ChangeTracker.Clear();
        return await _context.ImportacoesProcessos.FirstAsync(i => i.Id == importacaoId, ct);
    }

    private void Notificar(ImportacaoProcessos importacao, string titulo, string mensagem)
    {
        _context.Notificacoes.Add(new Notificacao
        {
            Id = Guid.NewGuid(),
            TenantId = importacao.TenantId,
            UsuarioId = importacao.UsuarioId,
            Tipo = TipoNotificacao.Geral,
            Titulo = titulo,
            Mensagem = mensagem,
            Url = $"/pages/importacao.html?id={importacao.Id}",
            CriadaEm = DateTime.UtcNow,
            ChaveDedup = $"importacao:{importacao.Id}"
        });
    }

    private static string Resumo(ImportacaoProcessos i)
    {
        var partes = new List<string> { $"{i.Importados} processo(s) importado(s)" };
        if (i.JaCadastrados > 0) partes.Add($"{i.JaCadastrados} já cadastrado(s)");
        if (i.Erros > 0) partes.Add($"{i.Erros} com erro");
        return string.Join(", ", partes);
    }

    public static ImportacaoProcessoItem NovoItem(
        Guid importacaoId, int ordem, ImportarProcessoItem dados, string? tribunalExibicao = null) => new()
    {
        Id = Guid.NewGuid(),
        ImportacaoId = importacaoId,
        Ordem = ordem,
        NumeroCNJ = dados.NumeroCNJ.Trim(),
        Tribunal = tribunalExibicao ?? dados.NomeTribunal ?? dados.Tribunal ?? dados.SiglaTribunal,
        DadosJson = JsonSerializer.Serialize(dados),
        Status = StatusItemImportacao.Pendente
    };

    // Mesmo mapeamento que o frontend faz (onboarding.js) ao enviar os selecionados.
    private static ImportarProcessoItem ItemDoPreview(ProcessoOabPreviewDto p) =>
        p.Fonte == "escavador"
            ? new ImportarProcessoItem(
                NumeroCNJ: p.NumeroCNJ,
                Fonte: "escavador",
                SiglaTribunal: p.SiglaTribunal,
                NomeTribunal: p.Tribunal,
                Vara: p.Vara,
                Comarca: p.Comarca,
                Classe: p.Classe,
                Assuntos: p.Assuntos,
                DataAjuizamento: p.DataAjuizamento)
            // Sem Tribunal: o importador identifica TJSP (e-SAJ) pelo próprio CNJ.
            : new ImportarProcessoItem(
                NumeroCNJ: p.NumeroCNJ,
                Codigo: p.Codigo,
                Foro: p.Foro,
                Fonte: p.Fonte);
}
