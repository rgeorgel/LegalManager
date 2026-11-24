using System.Globalization;
using System.Text.Json.Serialization;
using LegalManager.Application.Interfaces;

namespace LegalManager.Infrastructure.Escavador;

/// <summary>
/// Movimentação no formato da API V2 — o mesmo na listagem
/// <c>/api/v2/processos/numero_cnj/{cnj}/movimentacoes</c> e no callback <c>nova_movimentacao</c>
/// do monitoramento de processo. Os dois caminhos convertem por <see cref="ParaDto"/>, para que a
/// mesma movimentação gere a mesma chave de dedup e o mesmo texto venha ela por onde vier.
/// <para>
/// O callback <c>diario_movimentacao_nova</c> do monitoramento de OAB (termo) chega com o mesmo
/// <c>movimentacao</c> no topo, mas no formato do diário (V1): traz o processo em
/// <c>processo.numero_novo</c> — o único lugar com o número quando o processo é sigiloso e o texto
/// diz só "Processo Sigiloso" —, a vara em <c>secao</c>, o diário, a página e os links.
/// Na movimentação V2 esses campos não vêm e ficam nulos.
/// </para>
/// </summary>
public sealed class EscavadorMovimentacaoV2
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("data")] public DateTime? Data { get; set; }
    [JsonPropertyName("tipo")] public string? Tipo { get; set; }
    [JsonPropertyName("conteudo")] public string? Conteudo { get; set; }
    [JsonPropertyName("fonte")] public EscavadorMovimentacaoV2Fonte? Fonte { get; set; }

    // Campos do callback do diário (monitoramento de OAB).
    [JsonPropertyName("secao")] public string? Secao { get; set; }
    [JsonPropertyName("pagina")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? Pagina { get; set; }
    [JsonPropertyName("diario_oficial_id")] public long? DiarioOficialId { get; set; }
    [JsonPropertyName("link")] public string? Link { get; set; }
    [JsonPropertyName("link_api")] public string? LinkApi { get; set; }
    [JsonPropertyName("link_pdf")] public string? LinkPdf { get; set; }
    [JsonPropertyName("link_pdf_api")] public string? LinkPdfApi { get; set; }
    [JsonPropertyName("processo")] public EscavadorDiarioProcessoRef? Processo { get; set; }
    [JsonPropertyName("diario")] public EscavadorDiarioRef? Diario { get; set; }

    /// <summary>O V2 não traz uuid; o id da movimentação é estável e serve de chave de dedup.</summary>
    public static string UuidDe(long id) => id > 0 ? $"v2-mov-{id}" : string.Empty;

    /// <summary>Número do processo informado pelo diário (callback do monitoramento de OAB).</summary>
    public string? NumeroDoProcesso =>
        new[] { Processo?.NumeroNovo, Processo?.NumeroAntigo }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim();

    public EscavadorMovimentacaoDto ParaDto(string? numeroCnj) => new(
        Id: Id,
        Uuid: UuidDe(Id),
        Data: Data,
        ConteudoHtml: Conteudo,
        // O "snippet" do diário é o começo do texto com "..." — o card o mostraria repetido.
        Snippet: Conteudo,
        Tipo: Tipo,
        Diario: Fonte?.Nome ?? Diario?.Tipo ?? Diario?.Descricao ?? Diario?.Origem?.Nome,
        DiarioId: DiarioOficialId ?? Diario?.Id,
        DiarioSigla: Fonte?.Sigla ?? Diario?.Origem?.Sigla ?? Diario?.Plugin,
        OrigemEstado: Diario?.Origem?.Estado,
        Link: Link, LinkApi: LinkApi, LinkPdf: LinkPdf, LinkPdfApi: LinkPdfApi,
        NumeroCnj: numeroCnj ?? NumeroDoProcesso,
        JsonBruto: "{}",
        Secao: string.IsNullOrWhiteSpace(Secao) ? null : Secao.Trim(),
        Pagina: Pagina?.ToString(CultureInfo.InvariantCulture));
}

public sealed class EscavadorMovimentacaoV2Fonte
{
    [JsonPropertyName("nome")] public string? Nome { get; set; }
    [JsonPropertyName("sigla")] public string? Sigla { get; set; }
}

public sealed class EscavadorDiarioProcessoRef
{
    [JsonPropertyName("numero_novo")] public string? NumeroNovo { get; set; }
    [JsonPropertyName("numero_antigo")] public string? NumeroAntigo { get; set; }
}

public sealed class EscavadorDiarioRef
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("plugin")] public string? Plugin { get; set; }
    /// <summary>Ex.: "Diário de Justiça Eletrônico Nacional (DJEN)".</summary>
    [JsonPropertyName("tipo")] public string? Tipo { get; set; }
    /// <summary>Ex.: "Diário de Justiça do Estado de São Paulo".</summary>
    [JsonPropertyName("descricao")] public string? Descricao { get; set; }
    [JsonPropertyName("origem")] public EscavadorDiarioOrigemRef? Origem { get; set; }
}

public sealed class EscavadorDiarioOrigemRef
{
    [JsonPropertyName("nome")] public string? Nome { get; set; }
    [JsonPropertyName("sigla")] public string? Sigla { get; set; }
    [JsonPropertyName("estado")] public string? Estado { get; set; }
}
