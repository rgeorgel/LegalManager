using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

/// <summary>
/// Um contrato criado no Contratos a partir do Causify. O estado espelha o do Contratos (webhook e
/// conferência periódica); os signatários ficam como JSON, do jeito que vieram na última atualização.
/// </summary>
public class SolicitacaoAssinatura
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ContratoHonorarioId { get; set; }
    public Guid? ContatoId { get; set; }
    public Guid? ProcessoId { get; set; }

    /// <summary>Id do contrato no Contratos.</summary>
    public string ContratoExternoId { get; set; } = string.Empty;
    public int? NumeroExterno { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public string TemplateNome { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public StatusSolicitacaoAssinatura Status { get; set; }
    public string SignatariosJson { get; set; } = "[]";
    /// <summary>Erro de envio informado pelo Contratos (sendError / contract.send_failed).</summary>
    public string? Erro { get; set; }
    public string? MotivoRecusa { get; set; }

    public Guid? DocumentoAssinadoId { get; set; }
    public Guid? CriadoPorId { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? EnviadaEm { get; set; }
    public DateTime? ConcluidaEm { get; set; }
    public DateTime? ExpiraEm { get; set; }
    /// <summary>updatedAt do Contratos na última atualização aplicada — evento antigo que chega depois não volta o estado.</summary>
    public DateTime? AtualizadoExternoEm { get; set; }
    public DateTime? ConferidoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ContratoHonorario? ContratoHonorario { get; set; }
    public Contato? Contato { get; set; }
    public Processo? Processo { get; set; }
    public Documento? DocumentoAssinado { get; set; }
    public Usuario? CriadoPor { get; set; }
}

/// <summary>De onde vem cada variável de um template do Contratos (lembrado a cada envio).</summary>
public class MapeamentoModeloAssinatura
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    /// <summary>JSON: chave da variável → fonte do Causify (ex.: "cliente.nome").</summary>
    public string FontesJson { get; set; } = "{}";
    public DateTime AtualizadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
}

/// <summary>Ids de webhook já tratados: o Contratos entrega "pelo menos uma vez".</summary>
public class EventoAssinaturaRecebido
{
    public string Id { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public DateTime RecebidoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
