using LegalManager.Infrastructure.Services;

namespace LegalManager.UnitTests;

public class TenantTableSpecsTests
{
    [Fact]
    public void TableKeysForDeletion_NaoDuplicaUsuarios()
    {
        // Regressão: "Usuarios" já é o primeiro item de ExportOrder — uma segunda entrada
        // duplicada no fim de TableKeysForDeletion virava a PRIMEIRA entrada de
        // TableKeysForDeletionReversed, fazendo o wipe apagar AspNetUsers antes de
        // Tarefas/Processos/etc. (que referenciam o usuário por FK, ex.: Tarefas.CriadoPorId)
        // e violando a constraint no DELETE contra o Postgres real (EF InMemory não pega
        // isso, pois não valida FKs — por isso o teste checa a lista diretamente).
        var ocorrencias = TenantTableSpecs.TableKeysForDeletion.Count(k => k == "Usuarios");
        Assert.Equal(1, ocorrencias);
    }

    [Fact]
    public void TableKeysForDeletionReversed_ApagaUsuariosPorUltimo()
    {
        // "Usuarios" precisa ser a ÚLTIMA tabela apagada no wipe: toda outra tabela do
        // tenant (Tarefas.CriadoPorId, Processos.AdvogadoResponsavelId, os joins de
        // Identity, etc.) pode referenciá-la por FK.
        Assert.Equal("Usuarios", TenantTableSpecs.TableKeysForDeletionReversed.Last());
    }

    [Fact]
    public void TableKeysForDeletionReversed_ApagaJoinsDeIdentityAntesDeUsuarios()
    {
        var reversed = TenantTableSpecs.TableKeysForDeletionReversed;
        var usuariosIndex = reversed.IndexOf("Usuarios");

        foreach (var identitySpec in TenantTableSpecs.IdentityJoinTables)
        {
            var index = reversed.IndexOf(identitySpec.JsonKey);
            Assert.True(index >= 0, $"{identitySpec.JsonKey} deveria estar na lista de deleção.");
            Assert.True(index < usuariosIndex,
                $"{identitySpec.JsonKey} referencia UserId e precisa ser apagada antes de Usuarios.");
        }
    }

    [Fact]
    public void TableKeysForDeletionReversed_ApagaTarefasAntesDeUsuarios()
    {
        // Tarefas.CriadoPorId é uma FK obrigatória (não-nula) para Usuario.
        var reversed = TenantTableSpecs.TableKeysForDeletionReversed;
        Assert.True(reversed.IndexOf("Tarefas") < reversed.IndexOf("Usuarios"));
    }

    [Fact]
    public void TableKeysForDeletionReversed_ApagaDocumentosELancamentosAntesDeContratosEParcelas()
    {
        // Documento.ContratoId e LancamentoFinanceiro.ContratoHonorarioId/ParcelaHonorarioId
        // referenciam ContratosHonorario/ParcelasHonorario — precisam ser apagados antes,
        // senão o Postgres bloqueia o DELETE por violação de FK (visto em produção: erro
        // em ContratosHonorarios enquanto LancamentosFinanceiros ainda referenciava a linha).
        var reversed = TenantTableSpecs.TableKeysForDeletionReversed;
        var documentosIdx = reversed.IndexOf("Documentos");
        var lancamentosIdx = reversed.IndexOf("LancamentosFinanceiros");
        var contratosIdx = reversed.IndexOf("ContratosHonorario");
        var parcelasIdx = reversed.IndexOf("ParcelasHonorario");

        Assert.True(documentosIdx < contratosIdx);
        Assert.True(lancamentosIdx < contratosIdx);
        Assert.True(lancamentosIdx < parcelasIdx);
    }
}
