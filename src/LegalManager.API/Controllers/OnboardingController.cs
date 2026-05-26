using System.Globalization;
using System.Linq;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Onboarding;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/onboarding")]
[Authorize]
public class OnboardingController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly DataJudAdapter _dataJud;
    private readonly EsajTjspProcessosAdapter _esaj;
    private readonly IEscavadorService _escavador;
    private readonly IProcessoService _processoService;
    private readonly IContatoService _contatoService;
    private readonly ILogger<OnboardingController> _logger;

    public OnboardingController(
        AppDbContext context,
        ITenantContext tenantContext,
        DataJudAdapter dataJud,
        EsajTjspProcessosAdapter esaj,
        IEscavadorService escavador,
        IProcessoService processoService,
        IContatoService contatoService,
        ILogger<OnboardingController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _dataJud = dataJud;
        _esaj = esaj;
        _escavador = escavador;
        _processoService = processoService;
        _contatoService = contatoService;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<OnboardingStatusDto>> GetStatus(CancellationToken ct)
    {
        var usuario = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);
        return Ok(new OnboardingStatusDto(usuario?.OnboardingImportacaoCompleto ?? false));
    }

    [HttpPost("buscar-por-oab")]
    public async Task<ActionResult<List<ProcessoOabPreviewDto>>> BuscarPorOab(
        BuscarPorOabDto dto, CancellationToken ct)
    {
        // TJSP usa ESAJ; demais tribunais estaduais usam DataJud; TRF/TRT usam Escavador
        var tarefaDataJud = _dataJud.BuscarPorOabAsync(dto.NumeroOAB, dto.Uf, ct);
        var tarefaEsaj = dto.Uf.Equals("SP", StringComparison.OrdinalIgnoreCase)
            ? _esaj.BuscarPorOabAsync(dto.NumeroOAB, dto.Uf, ct)
            : Task.FromResult(new List<ProcessoOabPreviewDto>());
        var tarefaEscavador = BuscarEscavadorOabAsync(dto.NumeroOAB, dto.Uf, ct);

        await Task.WhenAll(tarefaDataJud, tarefaEsaj, tarefaEscavador);

        // DataJud preferred over Escavador when same CNJ appears in both
        var processos = tarefaDataJud.Result
            .Concat(tarefaEsaj.Result)
            .Concat(tarefaEscavador.Result)
            .GroupBy(p => p.NumeroCNJ)
            .Select(g => g.First())
            .OrderByDescending(p => p.DataAjuizamento)
            .ToList();

        if (processos.Count == 0)
            return Ok(processos);

        var numerosExistentes = await _context.Processos
            .Where(p => p.TenantId == _tenantContext.TenantId &&
                        processos.Select(x => x.NumeroCNJ).Contains(p.NumeroCNJ))
            .Select(p => p.NumeroCNJ)
            .ToHashSetAsync(ct);

        return Ok(processos
            .Select(p => p with { JaCadastrado = numerosExistentes.Contains(p.NumeroCNJ) })
            .ToList());
    }

    [HttpPost("importar")]
    public async Task<ActionResult<ImportarResultadoDto>> Importar(
        ImportarProcessosDto dto, CancellationToken ct)
    {
        var importados = 0;
        var mensagens = new List<string>();

        // Pre-fetch plan limits if any item comes from Escavador
        var temEscavador = dto.Processos.Any(x => x.Fonte == "escavador");
        var monitoradosCount = 0;
        var limiteMonitorados = 0;
        if (temEscavador)
        {
            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, ct);
            monitoradosCount = await _context.Processos
                .CountAsync(p => p.TenantId == _tenantContext.TenantId && p.Monitorado, ct);
            limiteMonitorados = PlanoRestricoes.MaxProcessosMonitorados(tenant?.Plano ?? PlanoTipo.Free);
        }

        foreach (var item in dto.Processos.GroupBy(x => x.NumeroCNJ).Select(g => g.First()))
        {
            try
            {
                if (item.Fonte == "escavador")
                {
                    var cnj = item.NumeroCNJ.Trim();

                    if (await _context.Processos.AnyAsync(
                            p => p.TenantId == _tenantContext.TenantId && p.NumeroCNJ == cnj, ct))
                    {
                        mensagens.Add($"{cnj}: já cadastrado");
                        continue;
                    }

                    var criarMon = monitoradosCount < limiteMonitorados;
                    string? monitoramentoId = null;
                    if (criarMon)
                    {
                        var mon = await _escavador.CriarMonitoramentoAsync(cnj, ct);
                        monitoramentoId = mon?.Id.ToString();
                        monitoradosCount++;
                    }

                    _context.Processos.Add(new Processo
                    {
                        Id = Guid.NewGuid(),
                        TenantId = _tenantContext.TenantId,
                        NumeroCNJ = cnj,
                        Tribunal = item.NomeTribunal ?? item.SiglaTribunal,
                        SiglaTribunal = item.SiglaTribunal,
                        Vara = item.Vara,
                        Comarca = item.Comarca,
                        Classe = item.Classe,
                        Assuntos = item.Assuntos,
                        DataAjuizamento = item.DataAjuizamento,
                        AreaDireito = InferirAreaEscavador(item.SiglaTribunal),
                        Fase = FaseProcessual.Conhecimento,
                        Status = StatusProcesso.Ativo,
                        Monitorado = criarMon,
                        EscavadorMonitoramentoId = monitoramentoId,
                        AdvogadoResponsavelId = _tenantContext.UserId,
                        CriadoEm = DateTime.Now
                    });
                    await _context.SaveChangesAsync(ct);
                    importados++;
                    continue;
                }

                CreateProcessoDto? createDto;

                if (EhTjsp(item.NumeroCNJ, item.Tribunal))
                    createDto = await MontarDtoEsajAsync(item.NumeroCNJ, item.Codigo, item.Foro, ct);
                else
                    createDto = await MontarDtoDataJudAsync(item.NumeroCNJ, ct);

                if (createDto == null)
                {
                    mensagens.Add($"{item.NumeroCNJ}: não encontrado");
                    continue;
                }

                await _processoService.CreateAsync(createDto, ct);
                importados++;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("já cadastrado"))
            {
                mensagens.Add($"{item.NumeroCNJ}: já cadastrado");
            }
            catch (Exception)
            {
                mensagens.Add($"{item.NumeroCNJ}: erro ao importar");
            }
        }

        return Ok(new ImportarResultadoDto(importados, mensagens.Count, mensagens));
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
                Status: StatusProcesso.Ativo,
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
                    Tags: null
                ), ct);
            }

            var tipoParte = MapearPolo(parte.Polo);
            resultado.Add(new ProcessoParteDto(contato.Id, tipoParte));
        }

        return resultado;
    }

    private async Task<List<ProcessoParteDto>> ResolverPartesDataJudAsync(
        List<TribunalParte> partes, CancellationToken ct)
    {
        var resultado = new List<ProcessoParteDto>();

        foreach (var parte in partes)
        {
            if (string.IsNullOrWhiteSpace(parte.Nome)) continue;

            var contato = await _contatoService.GetByNomeAsync(parte.Nome, ct);
            if (contato == null)
            {
                var tipoPessoa = !string.IsNullOrEmpty(parte.Cpf)
                    ? TipoPessoa.PF
                    : !string.IsNullOrEmpty(parte.Cnpj)
                        ? TipoPessoa.PJ
                        : TipoPessoa.PF;

                contato = await _contatoService.CreateAsync(new CreateContatoDto(
                    Tipo: tipoPessoa,
                    TipoContato: TipoContato.Cliente,
                    Nome: parte.Nome,
                    CpfCnpj: parte.Cpf ?? parte.Cnpj,
                    Oab: parte.OAB,
                    Email: null,
                    Telefone: null,
                    Endereco: null,
                    Cidade: null,
                    Estado: null,
                    Cep: null,
                    DataNascimento: null,
                    Observacoes: null,
                    NotificacaoHabilitada: false,
                    Tags: null
                ), ct);
            }

            var tipoParte = MapearPoloDataJud(parte.Polo);
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

    private static TipoParteProcesso MapearPoloDataJud(string? polo)
    {
        if (string.IsNullOrWhiteSpace(polo)) return TipoParteProcesso.Terceiro;
        var upper = polo.ToUpperInvariant();
        if (upper.Contains("AUTOR") || upper.Contains("RECLAMANTE") || upper.Contains("IMPETRANTE") || upper.Contains("REQUERENTE") || upper.Contains("EXEQUENTE"))
            return TipoParteProcesso.Autor;
        if (upper.Contains("RÉU") || upper.Contains("REU") || upper.Contains("RECLAMADO") || upper.Contains("INDICIADO") || upper.Contains("REQUERIDO") || upper.Contains("EXECUTADO"))
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

    [HttpPost("completar")]
    public async Task<ActionResult> Completar(CancellationToken ct)
    {
        var usuario = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);

        if (usuario != null)
        {
            usuario.OnboardingImportacaoCompleto = true;
            await _context.SaveChangesAsync(ct);
        }

        return Ok();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
            ? await ResolverPartesDataJudAsync(resultado.Partes.ToList(), ct)
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

    private async Task<List<ProcessoOabPreviewDto>> BuscarEscavadorOabAsync(
        string oab, string uf, CancellationToken ct)
    {
        try
        {
            var todos = new List<EscavadorProcessoDto>();
            for (var pagina = 1; pagina <= 2; pagina++)
            {
                var resultado = await _escavador.BuscarPorOabAsync(oab.Trim(), uf.Trim(), pagina, ct);
                foreach (var p in resultado.Data)
                    if (EhFederalOuTrabalhista(p.SiglaTribunal) && !string.IsNullOrWhiteSpace(p.Numero))
                        todos.Add(p);
                if (!resultado.TemProxima) break;
            }

            return todos
                .GroupBy(p => p.Numero)
                .Select(g => g.First())
                .Select(p => new ProcessoOabPreviewDto(
                    NumeroCNJ: p.Numero!,
                    Tribunal: p.NomeTribunal ?? p.SiglaTribunal ?? "Tribunal Federal/Trabalhista",
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Escavador OAB search failed, continuing without results");
            return [];
        }
    }

    private static bool EhFederalOuTrabalhista(string? sigla) =>
        !string.IsNullOrWhiteSpace(sigla) &&
        (sigla.StartsWith("TRF", StringComparison.OrdinalIgnoreCase) ||
         sigla.StartsWith("TRT", StringComparison.OrdinalIgnoreCase));

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

    private static decimal? ParseValor(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var limpo = System.Text.RegularExpressions.Regex.Replace(texto, @"[R$\s]", "")
            .Replace(".", "").Replace(",", ".");
        return decimal.TryParse(limpo, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
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
}
