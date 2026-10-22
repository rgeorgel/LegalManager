using System.Text.RegularExpressions;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Maps an Escavador V1 movimentacao (callback payload or polling response) to a <see cref="Publicacao"/> row.
/// Single source of truth so the webhook and polling flows produce identical rows.
/// </summary>
public class PublicacaoMapper
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public Publicacao Montar(Processo processo, EscavadorMovimentacaoDto mov, DateTime agora)
    {
        return new Publicacao
        {
            Id = Guid.NewGuid(),
            TenantId = processo.TenantId,
            ProcessoId = processo.Id,
            NumeroCNJ = processo.NumeroCNJ,
            Diario = Cortar(mov.Diario ?? mov.DiarioSigla, 100) ?? "Diário Oficial",
            DataPublicacao = mov.Data ?? agora,
            Conteudo = StripHtml(mov.ConteudoHtml) ?? mov.Snippet ?? string.Empty,
            Tipo = InferirTipo(mov.Tipo, mov.ConteudoHtml),
            Status = StatusPublicacao.Nova,
            Urgente = false,
            FonteCaptura = FonteCaptura.Escavador,
            UuidExterno = mov.Uuid,
            LinkEscavador = Cortar(mov.Link ?? mov.LinkApi, 500),
            LinkPdf = Cortar(mov.LinkPdf ?? mov.LinkPdfApi, 500),
            DiarioId = mov.DiarioId,
            DiarioSigla = Cortar(mov.DiarioSigla, 50),
            OrigemEstado = Cortar(mov.OrigemEstado, 2),
            OrigemId = null,
            // No V2 o "snippet" é a publicação inteira; o texto completo fica em Conteudo.
            Snippet = Cortar(mov.Snippet, 1000),
            Tribunal = Cortar(ExtrairTribunal(processo.NumeroCNJ, mov.DiarioSigla), 50),
            CapturaEm = agora
        };
    }

    // Limites das colunas (PublicacaoConfiguration): um valor maior derruba o SaveChanges no Postgres.
    public static string? Cortar(string? valor, int max) =>
        valor != null && valor.Length > max ? valor[..max] : valor;

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var noTags = HtmlTagRegex.Replace(html, " ");
        return Regex.Replace(noTags, @"\s+", " ").Trim();
    }

    private static TipoPublicacao InferirTipo(string? tipo, string? conteudo)
    {
        var texto = (tipo + " " + conteudo)?.ToLowerInvariant() ?? "";
        return texto switch
        {
            var s when s.Contains("prazo") || s.Contains("recurso") => TipoPublicacao.Prazo,
            var s when s.Contains("audiên") || s.Contains("audienc") || s.Contains("julgament") => TipoPublicacao.Audiencia,
            var s when s.Contains("decis") || s.Contains("senten") || s.Contains("acórd") => TipoPublicacao.Decisao,
            var s when s.Contains("despacho") => TipoPublicacao.Despacho,
            var s when s.Contains("intim") || s.Contains("notif") => TipoPublicacao.Intimacao,
            _ => TipoPublicacao.Outro
        };
    }

    /// <summary>
    /// Extracts the tribunal from the CNJ number (positions 14-15: segment+TR) or falls back to the diario sigla.
    /// CNJ format: NNNNNNN-DD.AAAA.J.TR.OOOO (positions 14-15 hold "J.TR" as 2 digits)
    /// </summary>
    private static string? ExtrairTribunal(string? cnj, string? sigla)
    {
        if (!string.IsNullOrWhiteSpace(sigla)) return sigla;
        if (string.IsNullOrWhiteSpace(cnj)) return null;
        var digits = Regex.Replace(cnj, @"\D", "");
        if (digits.Length < 16) return null;
        var segmento = digits[13];
        var tr = digits[14..16];
        return segmento switch
        {
            '5' => $"TRT-{int.Parse(tr):D2}",
            '4' => $"TRF-{int.Parse(tr):D2}",
            '8' => $"TJ-{tr}",
            _ => $"Segmento{segmento}-TR{tr}"
        };
    }
}
