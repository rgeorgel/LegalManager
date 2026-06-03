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
            Diario = mov.Diario ?? mov.DiarioSigla ?? "Diário Oficial",
            DataPublicacao = mov.Data ?? agora,
            Conteudo = StripHtml(mov.ConteudoHtml) ?? mov.Snippet ?? string.Empty,
            Tipo = InferirTipo(mov.Tipo, mov.ConteudoHtml),
            Status = StatusPublicacao.Nova,
            Urgente = false,
            FonteCaptura = FonteCaptura.Escavador,
            UuidExterno = mov.Uuid,
            LinkEscavador = mov.Link ?? mov.LinkApi,
            LinkPdf = mov.LinkPdf ?? mov.LinkPdfApi,
            DiarioId = mov.DiarioId,
            DiarioSigla = mov.DiarioSigla,
            OrigemEstado = mov.OrigemEstado,
            OrigemId = null,
            Snippet = mov.Snippet,
            Tribunal = ExtrairTribunal(processo.NumeroCNJ, mov.DiarioSigla),
            CapturaEm = agora
        };
    }

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
