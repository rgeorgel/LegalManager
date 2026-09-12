namespace LegalManager.Application.Interfaces;

/// <summary>
/// Registra auditoria de chamadas a APIs jurídicas externas (Escavador, DataJud, e-SAJ/TJSP)
/// para a página de Super Admin "Consultas Externas". Ver <see cref="RegistrarAsync{T}"/>.
/// </summary>
public interface IConsultaExternaLogService
{
    /// <summary>
    /// Executa <paramref name="execucao"/> medindo duração e gravando um <c>ConsultaExterna</c>
    /// com o resultado (sucesso/erro). Nunca lança por conta própria — se a gravação do log
    /// falhar, apenas loga um warning; se <paramref name="execucao"/> lançar, o log é gravado
    /// como falha e a exceção original é relançada (comportamento do call site não muda).
    /// </summary>
    /// <param name="api">API integrada: "Escavador" | "DataJud" | "EsajTjsp".</param>
    /// <param name="tipoConsulta">Tipo da consulta, ex: "BuscaProcessosPorOab".</param>
    /// <param name="origem">Tela/fluxo que originou a chamada.</param>
    /// <param name="parametros">Parâmetros da consulta (serializados em JSON).</param>
    /// <param name="execucao">A chamada real à API externa.</param>
    /// <param name="extrairResumo">
    /// Opcional: extrai (quantidade de resultados, resumo compacto) do retorno de <paramref name="execucao"/>
    /// para exibição no detalhe da consulta. Omitir quando o retorno não tiver itens listáveis.
    /// </param>
    /// <param name="tenantId">
    /// Tenant da consulta. Omitir usa <c>ITenantContext.TenantId</c> (requisições HTTP); jobs em
    /// background devem sempre passar explicitamente, pois não há HttpContext.
    /// </param>
    /// <param name="usuarioId">Usuário da consulta. Omitir usa <c>ITenantContext.UserId</c>; null para jobs.</param>
    Task<T> RegistrarAsync<T>(
        string api,
        string tipoConsulta,
        string origem,
        object? parametros,
        Func<Task<T>> execucao,
        Func<T, (int? Total, object? Resumo)>? extrairResumo = null,
        Guid? tenantId = null,
        Guid? usuarioId = null,
        CancellationToken ct = default);
}
