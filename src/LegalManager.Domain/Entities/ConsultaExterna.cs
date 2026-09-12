namespace LegalManager.Domain.Entities;

/// <summary>
/// Registro de auditoria de uma chamada a uma API jurídica externa (Escavador, DataJud,
/// e-SAJ/TJSP): quem consultou, quando, de qual tenant/tela, e o que foi retornado.
/// Usado pela página de Super Admin "Consultas Externas" (auditoria cross-tenant).
/// </summary>
public class ConsultaExterna
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UsuarioId { get; set; }
    public DateTime CriadoEm { get; set; }

    /// <summary>API integrada: "Escavador" | "DataJud" | "EsajTjsp".</summary>
    public string Api { get; set; } = string.Empty;

    /// <summary>Tipo da consulta, ex: "BuscaProcessosPorOab", "CriarMonitoramentoOab".</summary>
    public string TipoConsulta { get; set; } = string.Empty;

    /// <summary>Tela/fluxo do sistema que originou a chamada, ex: "Minha Caixa de Publicações — Busca por OAB".</summary>
    public string Origem { get; set; } = string.Empty;

    /// <summary>Parâmetros da consulta (OAB, UF, CNJ, ...) serializados em JSON.</summary>
    public string? ParametrosJson { get; set; }

    public bool Sucesso { get; set; }
    public string? MensagemErro { get; set; }
    public int? QuantidadeResultados { get; set; }

    /// <summary>Resumo compacto do resultado (andamentos/processos/publicações retornados) em JSON.</summary>
    public string? ResultadoResumoJson { get; set; }

    public int? DuracaoMs { get; set; }
}
