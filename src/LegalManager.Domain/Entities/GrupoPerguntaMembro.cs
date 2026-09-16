namespace LegalManager.Domain.Entities;

// Associação usuário ↔ grupo (GrupoPergunta). UsuarioId fica solto, sem FK/navegação —
// mesmo padrão de RespostaPergunta/AuditLog: não deve bloquear a exclusão de um tenant
// (que apaga seus Usuarios) nem exigir que este módulo entre na lista de tabelas
// varridas por TenantDeletionService/TenantImportService.
public class GrupoPerguntaMembro
{
    public Guid Id { get; set; }
    public Guid GrupoId { get; set; }
    public Guid UsuarioId { get; set; }

    public DateTime AdicionadoEm { get; set; }
}
