using System.Globalization;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Importacoes;
using LegalManager.Application.DTOs.Onboarding;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

public class ImportadorProcessosOab : IImportadorProcessosOab
{
    private const string OrigemBuscaOab = "Importação por OAB — Busca de processos";

    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly DataJudAdapter _dataJud;
    private readonly EsajTjspProcessosAdapter _esaj;
    private readonly IEscavadorService _escavador;
    private readonly IProcessoService _processoService;
    private readonly IContatoService _contatoService;
    private readonly IContatoResolverService _contatoResolver;
    private readonly IConsultaExternaLogService _consultaLog;
    private readonly ILogger<ImportadorProcessosOab> _logger;

    public ImportadorProcessosOab(
        AppDbContext context,
        ITenantContext tenantContext,
        DataJudAdapter dataJud,
        EsajTjspProcessosAdapter esaj,
        IEscavadorService escavador,
        IProcessoService processoService,
        IContatoService contatoService,
        IContatoResolverService contatoResolver,
        IConsultaExternaLogService consultaLog,
        ILogger<ImportadorProcessosOab> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _dataJud = dataJud;
        _esaj = esaj;
        _escavador = escavador;
        _processoService = processoService;
        _contatoService = contatoService;
        _contatoResolver = contatoResolver;
        _consultaLog = consultaLog;
        _logger = logger;
    }

    private static (int? Total, object? Resumo) ResumoProcessosPreview(IEnumerable<ProcessoOabPreviewDto> lista)
    {
        var itens = lista.Select(p => new { p.NumeroCNJ, p.Tribunal, p.Classe, p.DataAjuizamento }).ToList();
        return (itens.Count, itens);
    }

    public async Task<PaginaBuscaOab> BuscarPaginaEscavadorAsync(
        string numeroOab, string uf, string? cursor, CancellationToken ct = default)
    {
        var pagina = await _consultaLog.RegistrarAsync(
            "Escavador", "BuscaProcessosPorOab", OrigemBuscaOab,
            new { oab = numeroOab, uf, continuacao = cursor != null },
            () => _escavador.BuscarPaginaPorOabAsync(numeroOab.Trim(), uf.Trim(), cursor, ct),
            r => (r.Data.Count, r.Data.Select(p => new { p.Numero, p.NomeTribunal, p.Classe, p.DataAjuizamento })),
            ct: ct);

        var processos = pagina.Data.Where(p => !string.IsNullOrWhiteSpace(p.Numero)).ToList();

        // O cache só enriquece a importação; falhar aqui não pode descartar a página.
        try
        {
            await SalvarCacheEscavadorAsync(processos, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[Escavador] Falha ao salvar cache de importação para OAB {Oab}/{Uf}", numeroOab, uf);
            DescartarPendentes<ProcessoImportacaoCache>();
        }

        return new PaginaBuscaOab(await MarcarJaCadastradosAsync(PreviewsEscavador(processos), ct), pagina.ProximoCursor);
    }

    public async Task<List<ProcessoOabPreviewDto>> BuscarTribunaisAsync(
        string numeroOab, string uf, CancellationToken ct = default)
    {
        var parametrosBusca = new { oab = numeroOab, uf };

        // TJSP usa e-SAJ; os demais tribunais, DataJud. Ambos gratuitos.
        var dataJud = await _consultaLog.RegistrarAsync(
            "DataJud", "BuscaProcessosPorOab", OrigemBuscaOab, parametrosBusca,
            () => _dataJud.BuscarPorOabAsync(numeroOab, uf, ct),
            ResumoProcessosPreview, ct: ct);
        var esaj = uf.Equals("SP", StringComparison.OrdinalIgnoreCase)
            ? await _consultaLog.RegistrarAsync(
                "EsajTjsp", "BuscaProcessosPorOab", OrigemBuscaOab, parametrosBusca,
                () => _esaj.BuscarPorOabAsync(numeroOab, uf, ct),
                ResumoProcessosPreview, ct: ct)
            : [];

        var processos = dataJud
            .Concat(esaj)
            .GroupBy(p => p.NumeroCNJ)
            .Select(MergePreview)
            .OrderByDescending(p => p.DataAjuizamento)
            .ToList();

        return await MarcarJaCadastradosAsync(processos, ct);
    }

    private async Task<List<ProcessoOabPreviewDto>> MarcarJaCadastradosAsync(
        List<ProcessoOabPreviewDto> processos, CancellationToken ct)
    {
        if (processos.Count == 0) return processos;

        var numeros = processos.Select(x => x.NumeroCNJ).ToList();
        var numerosExistentes = await _context.Processos
            .Where(p => p.TenantId == _tenantContext.TenantId && numeros.Contains(p.NumeroCNJ))
            .Select(p => p.NumeroCNJ)
            .ToHashSetAsync(ct);

        return processos
            .Select(p => p with { JaCadastrado = numerosExistentes.Contains(p.NumeroCNJ) })
            .ToList();
    }

    public async Task<ResultadoImportacaoItem> ImportarAsync(ImportarProcessoItem item, CancellationToken ct = default)
    {
        try
        {
            if (item.Fonte == "escavador")
                return await ImportarEscavadorAsync(item, ct);

            CreateProcessoDto? createDto;

            if (EhTjsp(item.NumeroCNJ, item.Tribunal))
                createDto = await MontarDtoEsajAsync(item.NumeroCNJ, item.Codigo, item.Foro, ct);
            else
                createDto = await MontarDtoDataJudAsync(item.NumeroCNJ, ct);

            if (createDto == null)
                return new ResultadoImportacaoItem(StatusItemImportacao.NaoEncontrado, "Processo não encontrado no tribunal");

            var criado = await _processoService.CreateAsync(createDto, ct);
            return new ResultadoImportacaoItem(StatusItemImportacao.Importado, null, criado.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("já cadastrado"))
        {
            _context.ChangeTracker.Clear();
            return new ResultadoImportacaoItem(StatusItemImportacao.JaCadastrado, "Já cadastrado");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Import] Erro ao importar {CNJ}", item.NumeroCNJ);
            // Entidades que falharam no SaveChanges continuam rastreadas e fariam
            // todos os processos seguintes falharem junto; o que já foi salvo não é afetado.
            _context.ChangeTracker.Clear();
            return new ResultadoImportacaoItem(StatusItemImportacao.Erro, "Erro ao importar");
        }
    }

    private async Task<ResultadoImportacaoItem> ImportarEscavadorAsync(ImportarProcessoItem item, CancellationToken ct)
    {
        var cnj = item.NumeroCNJ.Trim();

        var existente = await _context.Processos
            .Where(p => p.TenantId == _tenantContext.TenantId && p.NumeroCNJ == cnj)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (existente != null)
            return new ResultadoImportacaoItem(StatusItemImportacao.JaCadastrado, "Já cadastrado", existente);

        // Enriquecer com dados do cache (JSON bruto salvo durante a busca)
        var siglaTribunal = item.SiglaTribunal;
        var nomeTribunal = item.NomeTribunal;
        var vara = item.Vara;
        var comarca = item.Comarca;
        var classe = item.Classe;
        var assuntos = item.Assuntos;
        var dataAjuizamento = item.DataAjuizamento;

        var cache = await _context.ProcessosImportacaoCache.FirstOrDefaultAsync(
            c => c.TenantId == _tenantContext.TenantId
              && c.NumeroCNJ == cnj
              && c.Fonte == "escavador"
              && c.ExpiraEm > DateTime.UtcNow, ct);

        if (cache != null)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(cache.DadosJson);
                var r = doc.RootElement;
                siglaTribunal = JsonStr(r, "unidade_origem", "tribunal_sigla") ?? siglaTribunal;
                nomeTribunal = JsonStr(r, "unidade_origem", "nome") ?? nomeTribunal;
                comarca = JsonStr(r, "unidade_origem", "cidade") ?? comarca;
                if (r.TryGetProperty("fontes", out var fontesEl) &&
                    fontesEl.ValueKind == System.Text.Json.JsonValueKind.Array &&
                    fontesEl.GetArrayLength() > 0)
                {
                    var capa = fontesEl[0];
                    if (capa.TryGetProperty("capa", out var capaEl))
                    {
                        vara = JsonStr(capaEl, "orgao_julgador") ?? vara;
                        classe = JsonStr(capaEl, "classe") ?? classe;
                        assuntos = JsonStr(capaEl, "assunto") ?? assuntos;
                    }
                }
                if (r.TryGetProperty("data_inicio", out var dtEl)
                    && dtEl.TryGetDateTime(out var dtVal))
                    dataAjuizamento = dtVal;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Import] Falha ao ler cache Escavador para {CNJ}", cnj);
            }
        }

        var processoId = Guid.NewGuid();
        _context.Processos.Add(new Processo
        {
            Id = processoId,
            TenantId = _tenantContext.TenantId,
            NumeroCNJ = cnj,
            Tribunal = nomeTribunal ?? siglaTribunal,
            SiglaTribunal = siglaTribunal,
            Vara = vara,
            Comarca = comarca,
            Classe = classe,
            Assuntos = assuntos,
            DataAjuizamento = dataAjuizamento,
            AreaDireito = InferirAreaEscavador(siglaTribunal),
            Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo,
            Monitorado = false,
            EscavadorMonitoramentoId = null,
            AdvogadoResponsavelId = _tenantContext.UserId,
            // Sem andamentos na importação em massa (seria 1 consulta paga por processo):
            // buscados uma vez, quando o processo for aberto ou monitorado.
            AndamentosPendentes = true,
            CriadoEm = DateTime.Now
        });
        await _context.SaveChangesAsync(ct);

        await SalvarPartesEscavadorAsync(cache?.DadosJson, processoId, ct);

        // TODO (5B — Estratégia 3): ao implementar monitoramento de Diários Oficiais,
        // criar aqui 1 único termo por tenant (idempotente) com o número OAB formatado
        // (ex: "123456/SP"). NÃO criar segundo termo com nome do advogado — Estratégia 3.
        // POST /api/v1/monitoramento-de-diarios-oficiais/criar
        return new ResultadoImportacaoItem(StatusItemImportacao.Importado, null, processoId);
    }

    private async Task<CreateProcessoDto?> MontarDtoEsajAsync(string cnj, string? codigo, string? foro, CancellationToken ct)
    {
        foreach (var grau in new[] { "G1", "G2" })
        {
            var detalhe = await _esaj.ObterDetalhesAsync(cnj, grau, ct, codigo, foro);
            if (detalhe == null || detalhe.Sigiloso) continue;
            if (string.IsNullOrEmpty(detalhe.Vara) && string.IsNullOrEmpty(detalhe.Classe)
                && string.IsNullOrEmpty(detalhe.Foro)) continue;

            var partesDto = await ResolverPartesAsync(detalhe.Partes, ct);

            return new CreateProcessoDto(
                NumeroCNJ: cnj,
                Tribunal: "TJSP",
                Vara: detalhe.Vara,
                Comarca: detalhe.Foro,
                AreaDireito: MapearAreaDireito(detalhe.Area),
                TipoAcao: detalhe.Classe,
                Fase: FaseProcessual.Conhecimento,
                Status: MapearStatus(detalhe.Situacao),
                ValorCausa: ParseValor(detalhe.ValorAcao),
                AdvogadoResponsavelId: _tenantContext.UserId,
                Classe: detalhe.Classe,
                Assuntos: detalhe.Assunto,
                DataAjuizamento: ParseDataDistribuicao(detalhe.DataDistribuicao),
                Grau: grau,
                Partes: partesDto,
                Andamentos: detalhe.Movimentos.Select(m =>
                    new AndamentoDto(
                        m.Data ?? DateTime.UtcNow,
                        string.IsNullOrEmpty(m.Complemento) ? m.Titulo : $"{m.Titulo} — {m.Complemento}",
                        null,
                        null
                    )).ToList()
            );
        }
        return null;
    }

    private async Task<List<ProcessoParteDto>> ResolverPartesAsync(
        List<ParteEsajDto> partes, CancellationToken ct)
    {
        var resultado = new List<ProcessoParteDto>();

        foreach (var parte in partes)
        {
            if (string.IsNullOrWhiteSpace(parte.Nome)) continue;

            var contato = await _contatoService.GetByNomeAsync(parte.Nome, ct);
            if (contato == null)
            {
                var tipoPessoa = DetectarTipoPessoa(parte.Nome, parte.Polo);
                var obs = parte.Advogados.Count > 0
                    ? $"Advogado(s): {string.Join(", ", parte.Advogados)}"
                    : null;
                contato = await _contatoService.CreateAsync(new CreateContatoDto(
                    Tipo: tipoPessoa,
                    TipoContato: TipoContato.Cliente,
                    Nome: parte.Nome,
                    CpfCnpj: null,
                    Oab: null,
                    Email: null,
                    Telefone: null,
                    Endereco: null,
                    Cidade: null,
                    Estado: null,
                    Cep: null,
                    DataNascimento: null,
                    Observacoes: obs,
                    NotificacaoHabilitada: false,
                    Tags: null,
                    ImportadoAutomaticamente: true
                ), ct);
            }

            var tipoParte = MapearPolo(parte.Polo);
            resultado.Add(new ProcessoParteDto(contato.Id, tipoParte));
        }

        return resultado;
    }

    private static TipoParteProcesso MapearPolo(string polo)
    {
        var upper = polo.ToUpperInvariant();
        if (upper.Contains("EXEQUENTE") || upper.Contains("EXEQTE") ||
            upper.Contains("AUTOR") || upper.Contains("RECLAMANTE") ||
            upper.Contains("IMPETRANTE") || upper.Contains("REQUERENTE"))
            return TipoParteProcesso.Autor;
        if (upper.Contains("EXECUTADO") || upper.Contains("EXECTDO") ||
            upper.Contains("RÉU") || upper.Contains("REU") ||
            upper.Contains("RECLAMADO") || upper.Contains("INDICIADO") ||
            upper.Contains("REQUERIDO"))
            return TipoParteProcesso.Reu;
        if (upper.Contains("INTERESSADO"))
            return TipoParteProcesso.Interessado;
        return TipoParteProcesso.Terceiro;
    }

    private static TipoPessoa DetectarTipoPessoa(string nome, string polo)
    {
        var upperPolo = polo.ToUpperInvariant();
        if (upperPolo.Contains("EXEQUENTE") || upperPolo.Contains("EXEQTE") ||
            upperPolo.Contains("AUTOR") || upperPolo.Contains("RECLAMANTE") ||
            upperPolo.Contains("IMPETRANTE") || upperPolo.Contains("REQUERENTE"))
            return TipoPessoa.PF;
        if (upperPolo.Contains("EXECUTADO") || upperPolo.Contains("EXECTDO") ||
            upperPolo.Contains("RÉU") || upperPolo.Contains("REU") ||
            upperPolo.Contains("RECLAMADO") || upperPolo.Contains("INDICIADO") ||
            upperPolo.Contains("REQUERIDO"))
            return TipoPessoa.PF;
        if (nome.Length > 50 && (nome.Contains("S.A.") || nome.Contains("LTDA") || nome.Contains("EIRELI") || nome.Contains("MEI")))
            return TipoPessoa.PJ;
        return TipoPessoa.PF;
    }

    private static ProcessoOabPreviewDto MergePreview(IGrouping<string, ProcessoOabPreviewDto> grupo)
    {
        // Escavador na frente — dados tendem a ser mais completos
        var lista = grupo.OrderByDescending(p => p.Fonte == "escavador").ToList();
        if (lista.Count == 1) return lista[0];

        static string? PrimeiroValido(IEnumerable<string?> valores) =>
            valores.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var fontePreferida = lista.Any(p => p.Fonte == "escavador") ? "escavador"
            : PrimeiroValido(lista.Select(p => p.Fonte)) ?? "datajud";

        return new ProcessoOabPreviewDto(
            NumeroCNJ: grupo.Key,
            Tribunal: PrimeiroValido(lista.Select(p => p.Tribunal)) ?? grupo.Key,
            Vara: PrimeiroValido(lista.Select(p => p.Vara)),
            Classe: PrimeiroValido(lista.Select(p => p.Classe)),
            DataAjuizamento: lista.Select(p => p.DataAjuizamento).FirstOrDefault(v => v.HasValue),
            Grau: PrimeiroValido(lista.Select(p => p.Grau)),
            Codigo: PrimeiroValido(lista.Select(p => p.Codigo)),
            Foro: PrimeiroValido(lista.Select(p => p.Foro)),
            Fonte: fontePreferida,
            SiglaTribunal: PrimeiroValido(lista.Select(p => p.SiglaTribunal)),
            Comarca: PrimeiroValido(lista.Select(p => p.Comarca)),
            Assuntos: PrimeiroValido(lista.Select(p => p.Assuntos))
        );
    }

    private static bool EhTjsp(string numeroCNJ, string? tribunal = null)
    {
        if (!string.IsNullOrWhiteSpace(tribunal))
            return tribunal.Equals("TJSP", StringComparison.OrdinalIgnoreCase);
        // CNJ: NNNNNNNDDAAAAJTTOOOO — J (segmento) em [13], TT (tribunal) em [14-15]
        // TJSP = J=8 (estadual), TT=26
        var normalizado = numeroCNJ.Replace("-", "").Replace(".", "");
        return normalizado.Length == 20
            && normalizado[13] == '8'
            && normalizado.Substring(14, 2) == "26";
    }

    private async Task<CreateProcessoDto?> MontarDtoDataJudAsync(string cnj, CancellationToken ct)
    {
        var resultado = await _dataJud.ConsultarAsync(cnj, ct);
        if (!resultado.Encontrado) return null;

        var partesDto = resultado.Partes != null
            ? await _contatoResolver.ResolverPartesDataJudAsync(resultado.Partes, ct)
            : new List<ProcessoParteDto>();

        return new CreateProcessoDto(
            NumeroCNJ: cnj,
            Tribunal: resultado.SiglaTribunal ?? resultado.NomeTribunal,
            Vara: resultado.Vara,
            Comarca: resultado.Comarca,
            AreaDireito: AreaDireito.Outro,
            TipoAcao: resultado.Classe,
            Fase: FaseProcessual.Conhecimento,
            Status: StatusProcesso.Ativo,
            ValorCausa: resultado.ValorCaixa,
            AdvogadoResponsavelId: _tenantContext.UserId,
            Classe: resultado.Classe,
            Assuntos: resultado.Assuntos != null ? string.Join(", ", resultado.Assuntos) : null,
            DataAjuizamento: resultado.DataAjuizamento,
            Grau: resultado.Grau,
            UltimaAtualizacaoDataJud: resultado.DataHoraUltimaAtualizacao,
            Partes: partesDto,
            Andamentos: resultado.Movimentos.Select(m =>
                new AndamentoDto(m.Data, m.Descricao, m.CodigoCNJ, m.OrgaoJulgador)).ToList()
        );
    }

    private static List<ProcessoOabPreviewDto> PreviewsEscavador(List<EscavadorProcessoDto> processos) =>
        processos
            .GroupBy(p => p.Numero)
            .Select(g => g.First())
            .Select(p => new ProcessoOabPreviewDto(
                NumeroCNJ: p.Numero!,
                Tribunal: p.NomeTribunal ?? p.SiglaTribunal ?? "Escavador",
                Vara: p.Vara,
                Classe: p.Classe,
                DataAjuizamento: p.DataAjuizamento,
                Grau: null,
                Fonte: "escavador",
                SiglaTribunal: p.SiglaTribunal,
                Comarca: p.Comarca,
                Assuntos: p.Assuntos
            ))
            .ToList();

    private async Task SalvarCacheEscavadorAsync(List<EscavadorProcessoDto> processos, CancellationToken ct)
    {
        // O Escavador pode devolver o mesmo CNJ mais de uma vez (ex.: instâncias diferentes);
        // mantém o primeiro, como o preview, para não violar o índice único (TenantId, NumeroCNJ, Fonte).
        var unicos = processos
            .Where(p => !string.IsNullOrWhiteSpace(p.JsonBruto))
            .GroupBy(p => p.Numero!)
            .Select(g => g.First())
            .ToList();
        if (unicos.Count == 0) return;
        var expira = DateTime.UtcNow.AddHours(72);
        var agora = DateTime.UtcNow;

        var numeros = unicos.Select(p => p.Numero!).ToList();
        var existentes = await _context.ProcessosImportacaoCache
            .Where(c => c.TenantId == _tenantContext.TenantId
                     && c.Fonte == "escavador"
                     && numeros.Contains(c.NumeroCNJ))
            .ToDictionaryAsync(c => c.NumeroCNJ, ct);

        foreach (var p in unicos)
        {
            if (existentes.TryGetValue(p.Numero!, out var existing))
            {
                existing.DadosJson = p.JsonBruto!;
                existing.ExpiraEm = expira;
            }
            else
            {
                _context.ProcessosImportacaoCache.Add(new Domain.Entities.ProcessoImportacaoCache
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.TenantId,
                    NumeroCNJ = p.Numero!,
                    Fonte = "escavador",
                    DadosJson = p.JsonBruto!,
                    ExpiraEm = expira,
                    CriadoEm = agora
                });
            }
        }
        await _context.SaveChangesAsync(ct);
    }

    private static AreaDireito InferirAreaEscavador(string? sigla) =>
        sigla?.StartsWith("TRT", StringComparison.OrdinalIgnoreCase) == true
            ? AreaDireito.Trabalhista
            : sigla?.StartsWith("TRF", StringComparison.OrdinalIgnoreCase) == true
                ? AreaDireito.Civil
                : AreaDireito.Outro;

    private static AreaDireito MapearAreaDireito(string area) =>
        area.ToUpperInvariant() switch
        {
            var a when a.Contains("CÍVEL") || a.Contains("CIVIL") => AreaDireito.Civil,
            var a when a.Contains("TRABALH") => AreaDireito.Trabalhista,
            var a when a.Contains("CRIMIN") || a.Contains("PENAL") => AreaDireito.Criminal,
            var a when a.Contains("TRIBUT") || a.Contains("FISCAL") => AreaDireito.Tributario,
            var a when a.Contains("PREVID") => AreaDireito.Previdenciario,
            var a when a.Contains("FAMÍL") || a.Contains("FAMIL") => AreaDireito.Familia,
            var a when a.Contains("EMPRES") || a.Contains("COMERC") => AreaDireito.Empresarial,
            var a when a.Contains("ADMIN") => AreaDireito.Administrativo,
            var a when a.Contains("CONSUM") => AreaDireito.Consumidor,
            var a when a.Contains("IMOBIL") => AreaDireito.Imobiliario,
            var a when a.Contains("AMBIENT") => AreaDireito.Ambiental,
            _ => AreaDireito.Outro
        };

    private static StatusProcesso MapearStatus(string situacao)
    {
        if (string.IsNullOrWhiteSpace(situacao))
            return StatusProcesso.Ativo;

        var upper = situacao.ToUpperInvariant();
        if (upper.Contains("EXTINTO") || upper.Contains("ENCERRADO") || upper.Contains("JULGADO"))
            return StatusProcesso.Encerrado;
        if (upper.Contains("ARQUIVADO") || upper.Contains("ARQUIVAMENTO"))
            return StatusProcesso.Arquivado;
        if (upper.Contains("SUSPENSO") || upper.Contains("SUSPENSÃO"))
            return StatusProcesso.Suspenso;
        return StatusProcesso.Ativo;
    }

    private static decimal? ParseValor(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var limpo = System.Text.RegularExpressions.Regex.Replace(texto, @"[R$\s]", "")
            .Replace(".", "").Replace(",", ".");
        return decimal.TryParse(limpo, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? JsonStr(System.Text.Json.JsonElement el, string prop1, string? prop2 = null)
    {
        if (!el.TryGetProperty(prop1, out var p1)) return null;
        if (prop2 == null)
            return p1.ValueKind == System.Text.Json.JsonValueKind.String ? p1.GetString() : null;
        if (!p1.TryGetProperty(prop2, out var p2)) return null;
        return p2.ValueKind == System.Text.Json.JsonValueKind.String ? p2.GetString() : null;
    }

    private static DateTime? ParseDataDistribuicao(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        // Formato: "29/05/2002 às 12:24 - Livre"
        var match = System.Text.RegularExpressions.Regex.Match(texto, @"(\d{2}/\d{2}/\d{4})");
        if (!match.Success) return null;
        return DateTime.TryParseExact(match.Value, "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
    }

    private async Task SalvarPartesEscavadorAsync(string? dadosJson, Guid processoId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dadosJson)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(dadosJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("fontes", out var fontesEl) ||
                fontesEl.ValueKind != System.Text.Json.JsonValueKind.Array)
                return;

            var nomesVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var salvouAlguma = false;

            foreach (var fonte in fontesEl.EnumerateArray())
            {
                if (!fonte.TryGetProperty("envolvidos", out var envolvidosEl) ||
                    envolvidosEl.ValueKind != System.Text.Json.JsonValueKind.Array)
                    continue;

                foreach (var envEl in envolvidosEl.EnumerateArray())
                {
                    var nome = JsonStr(envEl, "nome");
                    if (string.IsNullOrWhiteSpace(nome) || !nomesVistos.Add(nome)) continue;

                    var polo = JsonStr(envEl, "polo") ?? "";
                    var cpf = JsonStr(envEl, "cpf");
                    var cnpj = JsonStr(envEl, "cnpj");
                    var tipoPessoa = cnpj != null ? TipoPessoa.PJ : TipoPessoa.PF;

                    string? oabFormatada = null;
                    if (envEl.TryGetProperty("oabs", out var oabsEl) &&
                        oabsEl.ValueKind == System.Text.Json.JsonValueKind.Array &&
                        oabsEl.GetArrayLength() > 0)
                    {
                        var o = oabsEl[0];
                        var oabUf = JsonStr(o, "uf");
                        var oabNum = o.TryGetProperty("numero", out var numEl) &&
                                     numEl.ValueKind == System.Text.Json.JsonValueKind.Number
                            ? numEl.GetInt32().ToString()
                            : null;
                        if (oabNum != null && oabUf != null)
                            oabFormatada = $"{oabNum}/{oabUf}";
                    }

                    var contato = await _contatoService.GetByNomeAsync(nome, ct);
                    if (contato == null)
                    {
                        contato = await _contatoService.CreateAsync(new CreateContatoDto(
                            Tipo: tipoPessoa,
                            TipoContato: TipoContato.Cliente,
                            Nome: nome,
                            CpfCnpj: cpf ?? cnpj,
                            Oab: oabFormatada,
                            Email: null,
                            Telefone: null,
                            Endereco: null,
                            Cidade: null,
                            Estado: null,
                            Cep: null,
                            DataNascimento: null,
                            Observacoes: null,
                            NotificacaoHabilitada: false,
                            Tags: null,
                            ImportadoAutomaticamente: true
                        ), ct);
                    }

                    _context.ProcessoPartes.Add(new ProcessoParte
                    {
                        Id = Guid.NewGuid(),
                        ProcessoId = processoId,
                        ContatoId = contato.Id,
                        TipoParte = MapearPoloEscavador(polo)
                    });
                    salvouAlguma = true;
                }
            }

            if (salvouAlguma) await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Import] Falha ao salvar partes Escavador para processo {Id}", processoId);
            DescartarPendentes<ProcessoParte>();
        }
    }

    /// <summary>Descarta inserções pendentes que falharam, para não contaminar os próximos SaveChanges.</summary>
    private void DescartarPendentes<T>() where T : class
    {
        foreach (var entry in _context.ChangeTracker.Entries<T>()
                     .Where(e => e.State == EntityState.Added).ToList())
            entry.State = EntityState.Detached;
    }

    private static TipoParteProcesso MapearPoloEscavador(string polo)
    {
        var upper = polo.ToUpperInvariant();
        if (upper is "ATIVO" || upper.Contains("AUTOR") || upper.Contains("RECLAMANTE") ||
            upper.Contains("IMPETRANTE") || upper.Contains("REQUERENTE") || upper.Contains("EXEQUENTE"))
            return TipoParteProcesso.Autor;
        if (upper is "PASSIVO" || upper.Contains("RÉU") || upper.Contains("REU") ||
            upper.Contains("RECLAMADO") || upper.Contains("REQUERIDO") || upper.Contains("EXECUTADO"))
            return TipoParteProcesso.Reu;
        if (upper.Contains("INTERESSADO"))
            return TipoParteProcesso.Interessado;
        return TipoParteProcesso.Terceiro;
    }
}
