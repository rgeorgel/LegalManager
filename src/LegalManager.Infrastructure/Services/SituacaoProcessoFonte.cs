using System.Globalization;
using System.Text;
using System.Text.Json;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Situação do processo segundo a fonte, com o motivo legível ("Arquivamento definitivo em 12/03/2021").
/// <paramref name="Conferir"/> preenchido = há movimentação que pede um olhar do advogado antes de
/// mudar o status ("movimentou em 10/09/2026, depois do arquivamento"); sem ele, aplica-se direto.
/// </summary>
public record SituacaoNaFonte(StatusProcesso Status, string Motivo, string? Conferir = null)
{
    /// <summary>Status com que a importação cria o processo: o da fonte, a não ser que precise conferir.</summary>
    public StatusProcesso StatusAoImportar => Conferir == null ? Status : StatusProcesso.Ativo;

    public string MensagemImportacao => Conferir == null ? Motivo : $"{Motivo}. Mantido Ativo para conferir: {Conferir}";
}

public readonly record struct MovimentoSituacao(DateTime? Data, int? CodigoCNJ, string? Descricao);

/// <summary>
/// Infere, dos dados que a importação já recebe (sem consulta extra), se o processo deixou de
/// tramitar no tribunal. Só detecta arquivamento definitivo/baixa (→ Arquivado) e arquivamento
/// provisório (→ Suspenso): Encerrado fica para o advogado, que registra decisão e resultado.
/// null = sem indício; o processo segue Ativo.
/// </summary>
public static class SituacaoProcessoFonte
{
    // Tabela Processual Unificada do CNJ (movimentos).
    public const int BaixaDefinitiva = 22;
    public const int ArquivamentoProvisorio = 245;
    public const int ArquivamentoDefinitivo = 246;
    public const int Reativacao = 849;
    public const int Desarquivamento = 893;

    public static readonly int[] CodigosRelevantes =
        [BaixaDefinitiva, ArquivamentoProvisorio, ArquivamentoDefinitivo, Reativacao, Desarquivamento];

    // Folga depois do arquivamento: a publicação/certidão do próprio arquivo costuma vir logo em seguida.
    private const int FolgaAposArquivamentoDias = 30;
    private const int MovimentacaoRecenteDias = 180;

    /// <summary>
    /// Situação ao fim da sequência de movimentos: o último arquivamento vale, a não ser que um
    /// desarquivamento/reativação venha depois. Movimentos sem código (e-SAJ) são lidos pelo texto.
    /// <paramref name="ultimaMovimentacao"/>: quando <paramref name="movimentos"/> traz só os
    /// relevantes, a data do último andamento do processo (senão, a maior data da lista).
    /// </summary>
    public static SituacaoNaFonte? PorMovimentos(
        IEnumerable<MovimentoSituacao> movimentos, string? grau,
        DateTime? ultimaMovimentacao = null, DateTime? hoje = null)
    {
        // Fora da 1ª instância, "baixa definitiva" é a volta dos autos à origem, não o fim do processo.
        var primeiraInstancia = grau is not null
            && (grau.Equals("G1", StringComparison.OrdinalIgnoreCase) || grau.Equals("JE", StringComparison.OrdinalIgnoreCase));

        var lista = movimentos.OrderBy(m => m.Data ?? DateTime.MinValue).ToList();
        SituacaoNaFonte? atual = null;
        DateTime? desde = null;
        foreach (var m in lista)
        {
            var evento = Classificar(m, primeiraInstancia);
            if (evento == null) continue;
            var (status, rotulo) = evento.Value;
            atual = status == null ? null : new SituacaoNaFonte(status.Value, ComData(rotulo, m.Data));
            desde = m.Data;
        }
        return atual == null
            ? null
            : ComConferencia(atual, desde, ultimaMovimentacao ?? lista.Max(m => m.Data), hoje);
    }

    /// <summary>
    /// Movimentação mais de 30 dias depois do arquivamento, ou nos últimos 6 meses, pede conferência
    /// (o processo pode ter voltado a tramitar). Em produção (out/2026) isso foi ~13% dos detectados.
    /// </summary>
    public static SituacaoNaFonte ComConferencia(
        SituacaoNaFonte situacao, DateTime? arquivadoEm, DateTime? ultimaMovimentacao, DateTime? hoje = null)
    {
        if (ultimaMovimentacao is not { } ultima) return situacao;
        var quando = ultima.ToString("dd/MM/yyyy", BrasiliaTime.PtBr);
        if (arquivadoEm is { } arquivado && ultima.Date > arquivado.Date.AddDays(FolgaAposArquivamentoDias))
            return situacao with { Conferir = $"movimentou em {quando}, depois do arquivamento" };
        if (ultima.Date >= (hoje ?? BrasiliaTime.Hoje).Date.AddDays(-MovimentacaoRecenteDias))
            return situacao with { Conferir = $"movimentou em {quando}, nos últimos 6 meses" };
        return situacao;
    }

    /// <summary>
    /// Etiqueta "Situação" do tribunal (e-SAJ ou <c>capa.situacao</c> do Escavador), ex.: "Tramitando",
    /// "Arquivado definitivamente", "Baixado", "Suspenso/sobrestado por decisão judicial".
    /// </summary>
    public static SituacaoNaFonte? PorSituacaoTexto(string? situacao, bool primeiraInstancia)
    {
        if (string.IsNullOrWhiteSpace(situacao)) return null;
        var t = Normalizar(situacao);
        var motivo = $"Situação no tribunal: {situacao.Trim()}";

        // "Baixado - requisição pagamento": aguarda RPV/precatório, o que ainda interessa ao escritório.
        if (t.Contains("requisi")) return null;
        // Inclui as abreviações e erros de digitação dos tribunais ("Susp/sobr-aguarda...", "Supenso/sobrestado").
        if (t.Contains("suspens") || t.Contains("supens") || t.Contains("sobrest") || t.StartsWith("susp/")
            || t.Contains("provisori") || t.Contains("arquivado administrativ"))
            return new(StatusProcesso.Suspenso, motivo);
        // "Julgado" fica de fora: ainda pode haver recurso ou cumprimento de sentença.
        if (t.Contains("arquiv") || t.Contains("extint") || t.Contains("encerrad") || t.Contains("cancelad"))
            return new(StatusProcesso.Arquivado, motivo);
        // Fora da 1ª instância, "baixado" é a devolução dos autos à origem.
        if (t.Contains("baixad"))
            return primeiraInstancia ? new(StatusProcesso.Arquivado, motivo) : null;
        return null;
    }

    /// <summary>
    /// Item bruto da busca por OAB do Escavador (API v2). Usa o que vem do tribunal:
    /// <c>fontes[].arquivado</c> (true ou null, nunca false) e <c>fontes[].capa.situacao</c>.
    /// A previsão do Escavador (<c>status_predito</c>) não basta, e a flag da raiz
    /// <c>fontes_tribunais_estao_arquivadas</c> acompanha essa previsão: em produção (out/2026) veio
    /// true em ~1.250 de 5.000 processos sem nenhuma fonte arquivada, todos com status_predito
    /// INATIVO e metade com movimentação nos últimos 2 anos. Por isso ela só vale junto de ao
    /// menos uma fonte de tribunal arquivada.
    /// </summary>
    public static SituacaoNaFonte? PorEscavador(string? json, DateTime? hoje = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return null;

            var fontes = raiz.TryGetProperty("fontes", out var f) && f.ValueKind == JsonValueKind.Array
                ? f.EnumerateArray().Where(EhTribunal).ToList()
                : [];

            var porSituacao = fontes
                .Select(fonte => fonte.TryGetProperty("capa", out var capa) && capa.ValueKind == JsonValueKind.Object
                                 && capa.TryGetProperty("situacao", out var s) && s.ValueKind == JsonValueKind.String
                    ? PorSituacaoTexto(s.GetString(), Grau(fonte) == 1)
                    : null)
                .OfType<SituacaoNaFonte>()
                .ToList();

            var arquivadoEm = MaiorData(fontes, "capa", "data_arquivamento");
            var arquivadas = fontes.Count(x => x.TryGetProperty("arquivado", out var a) && a.ValueKind == JsonValueKind.True);
            var flagRaiz = raiz.TryGetProperty("fontes_tribunais_estao_arquivadas", out var arq) && arq.ValueKind == JsonValueKind.True;

            // Arquivamento provisório/suspensão também marca a fonte como arquivada: o texto é mais preciso.
            var situacao = porSituacao.FirstOrDefault(x => x.Status == StatusProcesso.Suspenso)
                ?? (arquivadas > 0 && (arquivadas == fontes.Count || flagRaiz)
                    ? new(StatusProcesso.Arquivado, ComData("Arquivado no tribunal", arquivadoEm))
                    : porSituacao.FirstOrDefault());

            return situacao == null
                ? null
                : ComConferencia(situacao, arquivadoEm, MaiorData(fontes, null, "data_ultima_movimentacao"), hoje);
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static int? Grau(JsonElement fonte) =>
        fonte.TryGetProperty("grau", out var g) && g.ValueKind == JsonValueKind.Number && g.TryGetInt32(out var n) ? n : null;

    // status null = o processo voltou a tramitar.
    private static (StatusProcesso? Status, string Rotulo)? Classificar(MovimentoSituacao m, bool primeiraInstancia)
    {
        switch (m.CodigoCNJ)
        {
            case ArquivamentoDefinitivo: return (StatusProcesso.Arquivado, "Arquivamento definitivo");
            case BaixaDefinitiva: return primeiraInstancia ? (StatusProcesso.Arquivado, "Baixa definitiva") : null;
            case ArquivamentoProvisorio: return (StatusProcesso.Suspenso, "Arquivamento provisório");
            case Desarquivamento or Reativacao: return (null, "");
            case not null: return null;
        }

        if (string.IsNullOrWhiteSpace(m.Descricao)) return null;
        var t = Normalizar(m.Descricao);
        if (t.Contains("desarquiv") || t.Contains("reativ")) return (null, "");
        if (t.Contains("arquivado definitiv") || t.Contains("arquivados definitiv") || t.Contains("arquivamento definitiv"))
            return (StatusProcesso.Arquivado, "Arquivamento definitivo");
        if (t.Contains("baixa definitiva"))
            return primeiraInstancia ? (StatusProcesso.Arquivado, "Baixa definitiva") : null;
        if (t.Contains("arquivado provisori") || t.Contains("arquivados provisori") || t.Contains("arquivamento provisori"))
            return (StatusProcesso.Suspenso, "Arquivamento provisório");
        return null;
    }

    private static bool EhTribunal(JsonElement fonte) =>
        fonte.ValueKind == JsonValueKind.Object
        && (!fonte.TryGetProperty("tipo", out var tipo) || tipo.ValueKind != JsonValueKind.String
            || string.Equals(tipo.GetString(), "TRIBUNAL", StringComparison.OrdinalIgnoreCase));

    // Maior data (yyyy-MM-dd...) de fontes[].{objeto?}.{campo}.
    private static DateTime? MaiorData(List<JsonElement> fontes, string? objeto, string campo) =>
        fontes
            .Select(f => objeto == null ? f : f.TryGetProperty(objeto, out var o) ? o : default)
            .Select(o => o.ValueKind == JsonValueKind.Object
                         && o.TryGetProperty(campo, out var d) && d.ValueKind == JsonValueKind.String
                         && d.GetString() is { Length: >= 10 } texto
                         && DateTime.TryParseExact(texto[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
                ? data
                : (DateTime?)null)
            .Max();

    private static string ComData(string rotulo, DateTime? data) =>
        data.HasValue ? $"{rotulo} em {data.Value.ToString("dd/MM/yyyy", BrasiliaTime.PtBr)}" : rotulo;

    private static string Normalizar(string texto)
    {
        var decomposto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}
