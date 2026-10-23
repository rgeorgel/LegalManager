using System.Text.Json.Serialization;
using LegalManager.Application.Interfaces;

namespace LegalManager.Infrastructure.Escavador;

/// <summary>
/// Movimentação no formato da API V2 — o mesmo na listagem
/// <c>/api/v2/processos/numero_cnj/{cnj}/movimentacoes</c> e no callback <c>nova_movimentacao</c>
/// do monitoramento de processo. Os dois caminhos convertem por <see cref="ParaDto"/>, para que a
/// mesma movimentação gere a mesma chave de dedup e o mesmo texto venha ela por onde vier.
/// </summary>
public sealed class EscavadorMovimentacaoV2
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("data")] public DateTime? Data { get; set; }
    [JsonPropertyName("tipo")] public string? Tipo { get; set; }
    [JsonPropertyName("conteudo")] public string? Conteudo { get; set; }
    [JsonPropertyName("fonte")] public EscavadorMovimentacaoV2Fonte? Fonte { get; set; }

    /// <summary>O V2 não traz uuid; o id da movimentação é estável e serve de chave de dedup.</summary>
    public static string UuidDe(long id) => id > 0 ? $"v2-mov-{id}" : string.Empty;

    public EscavadorMovimentacaoDto ParaDto(string? numeroCnj) => new(
        Id: Id,
        Uuid: UuidDe(Id),
        Data: Data,
        ConteudoHtml: Conteudo,
        Snippet: Conteudo,
        Tipo: Tipo,
        Diario: Fonte?.Nome,
        DiarioId: null,
        DiarioSigla: Fonte?.Sigla,
        OrigemEstado: null,
        Link: null, LinkApi: null, LinkPdf: null, LinkPdfApi: null,
        NumeroCnj: numeroCnj,
        JsonBruto: "{}");
}

public sealed class EscavadorMovimentacaoV2Fonte
{
    [JsonPropertyName("nome")] public string? Nome { get; set; }
    [JsonPropertyName("sigla")] public string? Sigla { get; set; }
}
