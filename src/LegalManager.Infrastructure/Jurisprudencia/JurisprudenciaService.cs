using System.Globalization;
using System.Text;
using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace LegalManager.Infrastructure.Jurisprudencia;

public class JurisprudenciaService(AppDbContext context, ITenantContext tenantContext, IStorageService storage)
    : IJurisprudenciaService
{
    private const long TamanhoMaximoDocumento = 30 * 1024 * 1024;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // Palavras que não ajudam a achar precedentes (além das que o próprio full-text já ignora).
    private static readonly HashSet<string> Irrelevantes =
    [
        "para", "pelo", "pela", "pelos", "pelas", "como", "mais", "sobre", "entre", "desde", "quando", "onde",
        "esta", "este", "isso", "essa", "esse", "seus", "suas", "sendo", "foram", "pois", "porque", "qual",
        "quais", "cujo", "cuja", "contra", "ainda", "apos", "outro", "outra", "parte", "autor", "autora",
        "reu", "pedido", "acao", "processo", "cliente", "preciso", "quero", "peca", "peticao", "inicial",
        "contestacao", "recurso", "fatos", "fato", "direito", "deve", "pode", "caso", "favor", "valor",
        "tipo", "vara", "comarca", "tribunal", "area",
    ];

    public async Task<VerificacaoCitacoesDto> VerificarTextoAsync(string texto, bool consultarBase, CancellationToken ct = default)
    {
        var citacoes = ExtratorCitacoes.Extrair(texto);
        var fontes = consultarBase
            ? await context.FontesPrecedentes.AsNoTracking().ToListAsync(ct)
            : [];

        var resultado = new List<CitacaoVerificadaDto>(citacoes.Count);
        foreach (var c in citacoes)
            resultado.Add(consultarBase
                ? await ConferirAsync(c, fontes, ct)
                : Montar(c, StatusCitacao.NaoVerificada, "Não verificada — confira na fonte antes de usar."));

        return Resumir(resultado, consultarBase ? Cobertura(fontes) : "Conferência na base oficial não habilitada para o escritório.");
    }

    public async Task<VerificacaoCitacoesDto?> VerificarDocumentoAsync(Guid documentoId, CancellationToken ct = default)
    {
        var documento = await context.Documentos.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentoId && d.TenantId == tenantContext.TenantId, ct);
        if (documento is null) return null;
        if (documento.TamanhoBytes > TamanhoMaximoDocumento)
            throw new InvalidOperationException("Documento grande demais para verificar (máximo de 30 MB).");

        var ehPdf = documento.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                    || documento.Nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        var ehTexto = documento.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
        if (!ehPdf && !ehTexto)
            throw new InvalidOperationException("Só dá para verificar PDF ou texto. Cole o conteúdo do documento no campo de texto.");

        await using var stream = await storage.DownloadAsync(documento.ObjectKey, ct);
        using var memoria = new MemoryStream();
        await stream.CopyToAsync(memoria, ct);
        var texto = ehPdf ? TextoDoPdf(memoria.ToArray()) : Encoding.UTF8.GetString(memoria.ToArray());
        if (string.IsNullOrWhiteSpace(texto))
            throw new InvalidOperationException("Não foi possível ler o texto deste PDF (pode ser digitalizado, sem texto).");

        return await VerificarTextoAsync(texto, consultarBase: true, ct);
    }

    public async Task<IReadOnlyList<PrecedenteDto>> BuscarPrecedentesAsync(string consulta, int limite, CancellationToken ct = default)
    {
        var termos = TermosDeBusca(consulta);
        if (termos.Count == 0 || limite <= 0) return [];

        List<Precedente> achados;
        if (context.Database.IsNpgsql())
        {
            // OR entre os termos, ordenado pela relevância (ts_rank): mais termos em comum, mais acima.
            // Os termos só têm letras e dígitos (TermosDeBusca), então a sintaxe do tsquery é segura.
            var tsquery = string.Join(" | ", termos);
            achados = await context.Precedentes
                .FromSqlInterpolated($"""
                    SELECT * FROM "Precedentes"
                    WHERE NOT "Cancelado"
                      AND to_tsvector('portuguese', "Texto") @@ to_tsquery('portuguese', {tsquery})
                    ORDER BY ts_rank(to_tsvector('portuguese', "Texto"), to_tsquery('portuguese', {tsquery})) DESC,
                             "DataJulgamento" DESC NULLS LAST
                    LIMIT {limite}
                    """)
                .AsNoTracking()
                .ToListAsync(ct);
        }
        else
        {
            // Bancos sem full-text (testes com EF InMemory): conta os termos presentes.
            var candidatos = await context.Precedentes.AsNoTracking().Where(p => !p.Cancelado).Take(5000).ToListAsync(ct);
            achados = candidatos
                .Select(p => (p, pontos: termos.Count(t => Normalizar(p.Texto).Contains(Normalizar(t)))))
                .Where(x => x.pontos > 0)
                .OrderByDescending(x => x.pontos).ThenByDescending(x => x.p.DataJulgamento)
                .Take(limite)
                .Select(x => x.p)
                .ToList();
        }

        return achados.Select(ParaDto).ToList();
    }

    // ── Conferência ─────────────────────────────────────────────────────────

    private async Task<CitacaoVerificadaDto> ConferirAsync(CitacaoDto c, List<FontePrecedentes> fontes, CancellationToken ct)
    {
        switch (c.Tipo)
        {
            case TipoCitacao.Acordao:
                return await ConferirAcordaoAsync(c, ct);

            case TipoCitacao.Tema:
            {
                var tribunal = c.Tribunal ?? "STJ";
                var tema = await context.Precedentes.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Tribunal == tribunal && p.Tipo == TipoPrecedente.TemaRepetitivo && p.Numero == c.Numero, ct);
                if (tema is not null)
                    return tema.Cancelado
                        ? Montar(c, StatusCitacao.Cancelada, $"Tema {c.Numero}/{tribunal} consta como \"{tema.Situacao}\".", tema)
                        : Montar(c, StatusCitacao.Verificada, $"Tema {c.Numero}/{tribunal} encontrado.", tema);
                if (c.Tribunal == "STJ" && ListaCompleta(fontes, StjDadosAbertos.FonteTemas))
                    return Montar(c, StatusCitacao.Inexistente, $"Não existe Tema {c.Numero} de recursos repetitivos no STJ.");
                return Montar(c, StatusCitacao.NaoVerificada, c.Tribunal == "STF"
                    ? "Temas de repercussão geral do STF ainda não são conferidos — confira na fonte."
                    : "Tema não encontrado na base — confira o tribunal e o número na fonte.");
            }

            case TipoCitacao.Sumula:
            case TipoCitacao.SumulaVinculante:
            {
                var tipo = c.Tipo == TipoCitacao.SumulaVinculante ? TipoPrecedente.SumulaVinculante : TipoPrecedente.Sumula;
                var sumula = c.Tribunal is null ? null : await context.Precedentes.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Tribunal == c.Tribunal && p.Tipo == tipo && p.Numero == c.Numero, ct);
                if (sumula is not null)
                    return sumula.Cancelado
                        ? Montar(c, StatusCitacao.Cancelada, $"Súmula consta como \"{sumula.Situacao}\".", sumula)
                        : Montar(c, StatusCitacao.Verificada, "Súmula encontrada.", sumula);
                var chave = c.Tribunal is null ? null : $"{c.Tribunal.ToLowerInvariant()}-sumulas{(tipo == TipoPrecedente.SumulaVinculante ? "-vinculantes" : "")}";
                if (chave is not null && ListaCompleta(fontes, chave))
                    return Montar(c, StatusCitacao.Inexistente, $"Não existe essa súmula no {c.Tribunal}.");
                return Montar(c, StatusCitacao.NaoVerificada, "Súmulas ainda não são conferidas — confira o número e o tribunal na fonte.");
            }

            case TipoCitacao.OrientacaoJurisprudencial:
                return Montar(c, StatusCitacao.NaoVerificada, "Orientações jurisprudenciais do TST ainda não são conferidas — confira na fonte.");

            default:
                return Montar(c, StatusCitacao.NaoVerificada, "Acórdãos de tribunais locais ainda não são conferidos — confira no site do tribunal.");
        }
    }

    private async Task<CitacaoVerificadaDto> ConferirAcordaoAsync(CitacaoDto c, CancellationToken ct)
    {
        var classeBase = ExtratorCitacoes.ClasseBase(c.Classe);
        if (c.Tribunal is not null and not "STJ")
            return Montar(c, StatusCitacao.NaoVerificada, $"Decisões do {c.Tribunal} ainda não são conferidas — confira na fonte.");

        var acordaos = await context.Precedentes.AsNoTracking()
            .Where(p => p.Tribunal == "STJ" && p.Tipo == TipoPrecedente.Acordao && p.ClasseBase == classeBase && p.Numero == c.Numero)
            .OrderByDescending(p => p.DataJulgamento)
            .Take(20)
            .ToListAsync(ct);

        if (acordaos.Count == 0)
            return c.Tribunal == "STJ"
                ? Montar(c, StatusCitacao.NaoEncontrada,
                    "Não está na base de acórdãos do STJ (que cobre os acórdãos selecionados pelo STJ no período importado). Pode existir — confira na fonte.")
                : Montar(c, StatusCitacao.NaoVerificada, "Tribunal não identificado — confira na fonte.");

        // O mesmo processo pode ter vários acórdãos (AgInt, EDcl...): prefere o da classe citada.
        var escolhido = acordaos.FirstOrDefault(p => string.Equals(p.Classe, c.Classe, StringComparison.OrdinalIgnoreCase)) ?? acordaos[0];

        if (c.Relator is { } relatorCitado && !acordaos.Any(p => MesmoRelator(relatorCitado, p.Relator)))
            return Montar(c, StatusCitacao.Divergente,
                $"O texto diz relator(a) {relatorCitado}; na base do STJ é {Titulo(escolhido.Relator)}.", escolhido);

        return Montar(c, StatusCitacao.Verificada,
            $"Encontrado no STJ: {escolhido.Classe} {FormatarNumero(escolhido.Numero)}, {Titulo(escolhido.OrgaoJulgador)}, julgado em {escolhido.DataJulgamento?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.",
            escolhido);
    }

    private static bool MesmoRelator(string citado, string? naBase)
    {
        if (string.IsNullOrWhiteSpace(naBase)) return true; // nada a comparar
        var baseNorm = Normalizar(naBase);
        var partes = Normalizar(citado).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 2).ToList();
        return partes.Count > 0 && partes.All(baseNorm.Contains);
    }

    private static bool ListaCompleta(List<FontePrecedentes> fontes, string chave) =>
        fontes.Any(f => f.Chave == chave && f.ListaCompleta && f.Total > 0);

    private static string Cobertura(List<FontePrecedentes> fontes)
    {
        var partes = new List<string>();
        if (ListaCompleta(fontes, StjDadosAbertos.FonteTemas)) partes.Add("temas repetitivos do STJ (lista completa)");
        var espelhos = fontes.Where(f => f.Chave.StartsWith(StjDadosAbertos.PrefixoFonteEspelhos) && f.Total > 0).ToList();
        if (espelhos.Count > 0) partes.Add($"{espelhos.Sum(f => f.Total).ToString("N0", PtBr)} acórdãos do STJ");
        if (partes.Count == 0) return "A base oficial ainda está sendo carregada — por enquanto nada é conferido.";
        return $"Conferido na base oficial: {string.Join(" e ", partes)}. Súmulas, STF, TST e tribunais locais ainda não são conferidos.";
    }

    private static VerificacaoCitacoesDto Resumir(List<CitacaoVerificadaDto> citacoes, string cobertura) => new(
        citacoes,
        citacoes.Count(c => c.Status == StatusCitacao.Verificada),
        citacoes.Count(c => c.Status is StatusCitacao.Divergente or StatusCitacao.Cancelada or StatusCitacao.NaoEncontrada or StatusCitacao.NaoVerificada),
        citacoes.Count(c => c.Status == StatusCitacao.Inexistente),
        cobertura);

    private static CitacaoVerificadaDto Montar(CitacaoDto c, StatusCitacao status, string mensagem, Precedente? p = null) =>
        new(c.Tipo, c.Tribunal, c.Classe, c.Numero, c.Trecho, c.Inicio, c.Fim, status, mensagem, p is null ? null : ParaDto(p));

    // ── Apoio ───────────────────────────────────────────────────────────────

    private const int TamanhoTextoDto = 1500;

    public static PrecedenteDto ParaDto(Precedente p) => new(
        p.Id, p.Tipo.ToString(), p.Tribunal, p.Classe, p.Numero, Identificacao(p), Titulo(p.Relator),
        Titulo(p.OrgaoJulgador), p.DataJulgamento,
        p.Texto.Length <= TamanhoTextoDto ? p.Texto : p.Texto[..TamanhoTextoDto] + "…",
        p.Situacao, p.UrlFonte);

    public static string Identificacao(Precedente p) => p.Tipo switch
    {
        TipoPrecedente.Acordao => $"{p.Classe} {FormatarNumero(p.Numero)}/{p.Tribunal}",
        TipoPrecedente.Sumula => $"Súmula {p.Numero}/{p.Tribunal}",
        TipoPrecedente.SumulaVinculante => $"Súmula Vinculante {p.Numero}",
        TipoPrecedente.TemaRepetitivo => $"Tema {FormatarNumero(p.Numero)}/{p.Tribunal} (recursos repetitivos)",
        TipoPrecedente.TemaRepercussaoGeral => $"Tema {FormatarNumero(p.Numero)}/{p.Tribunal} (repercussão geral)",
        _ => $"{p.Classe ?? "Precedente"} {p.Numero}/{p.Tribunal}",
    };

    public static string FormatarNumero(string numero) =>
        long.TryParse(numero, out var n) ? n.ToString("N0", PtBr) : numero;

    /// <summary>"MARIA ISABEL GALLOTTI" → "Maria Isabel Gallotti".</summary>
    public static string? Titulo(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return texto;
        var minusculas = new HashSet<string> { "de", "da", "do", "das", "dos", "e" };
        return string.Join(' ', texto.Trim().ToLower(PtBr).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i > 0 && minusculas.Contains(p) ? p : char.ToUpper(p[0], PtBr) + p[1..]));
    }

    private static string Normalizar(string s) => ExtratorCitacoes.SemAcento(s).ToLowerInvariant();

    /// <summary>
    /// Até 15 palavras relevantes (≥ 4 letras), em minúsculas e só com letras e dígitos. Mantém os
    /// acentos: o stemmer "portuguese" do Postgres reduz "contratação" e "contratacao" de formas diferentes.
    /// </summary>
    public static List<string> TermosDeBusca(string texto) =>
        texto.ToLower(PtBr)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray()))
            .Where(p => p.Length >= 4 && !p.All(char.IsDigit) && !Irrelevantes.Contains(ExtratorCitacoes.SemAcento(p)))
            .Distinct()
            .Take(15)
            .ToList();

    private static string TextoDoPdf(byte[] bytes)
    {
        try
        {
            using var pdf = PdfDocument.Open(bytes);
            var sb = new StringBuilder();
            foreach (var pagina in pdf.GetPages()) sb.AppendLine(pagina.Text);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }
}
