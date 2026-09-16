using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

// Resposta de um usuário (advogado/equipe do escritório ou cliente do portal) a uma
// Pergunta de pesquisa. TenantId/RespondenteId ficam soltos, sem FK/navegação — mesmo
// padrão de AuditLog: são registros de log/analytics que não devem bloquear a exclusão
// de um tenant ou usuário (RespondenteId aponta para Usuario.Id ou AcessoCliente.Id,
// duas tabelas diferentes, conforme RespondenteTipo).
public class RespostaPergunta
{
    public Guid Id { get; set; }
    public Guid PerguntaId { get; set; }
    public Guid TenantId { get; set; }

    public TipoRespondente RespondenteTipo { get; set; }
    public Guid RespondenteId { get; set; }

    public string? RespostaTexto { get; set; }
    public string? OpcaoEscolhida { get; set; }

    public DateTime RespondidoEm { get; set; }
}
