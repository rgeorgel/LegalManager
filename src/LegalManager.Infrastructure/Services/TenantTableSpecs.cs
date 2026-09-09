using LegalManager.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace LegalManager.Infrastructure.Services;

internal enum TenantFilterKind
{
    TenantId,
    NullableTenantId,
    GlobalTable,
    ContatoIdInTenant,
    TarefaIdInTenant,
    ProcessoIdInTenant,
}

internal record TenantTableSpec(
    string JsonKey,
    Type EntityType,
    TenantFilterKind TenantFilter,
    string? NullableTenantProperty = null,
    string? IncludeNavigation = null
);

internal record IdentityJoinSpec(
    string JsonKey,
    Type EntityType,
    string UserIdProperty
);

internal static class TenantTableSpecs
{
    public static readonly List<TenantTableSpec> ExportOrder = new()
    {
        new("Usuarios",                 typeof(Usuario),                      TenantFilterKind.TenantId),
        new("TenantOabs",               typeof(TenantOab),                    TenantFilterKind.TenantId),

        new("Contatos",                 typeof(Contato),                      TenantFilterKind.TenantId),
        new("ContatoTags",              typeof(ContatoTag),                   TenantFilterKind.ContatoIdInTenant),

        new("Pastas",                   typeof(Pasta),                        TenantFilterKind.TenantId),
        new("ModelosDocumento",         typeof(ModeloDocumento),              TenantFilterKind.TenantId),

        new("AreasAtuacao",             typeof(AreaAtuacao),                  TenantFilterKind.TenantId),
        new("CategoriasFinanceiras",    typeof(CategoriaFinanceira),          TenantFilterKind.TenantId),
        new("PreferenciasNotificacoes", typeof(PreferenciasNotificacao),      TenantFilterKind.TenantId),
        new("ConfiguracoesCalculadora", typeof(ConfiguracaoCalculadora),      TenantFilterKind.TenantId),
        new("Feriados",                 typeof(Feriado),                      TenantFilterKind.NullableTenantId, nameof(Feriado.TenantId)),
        new("IndicesCorrecaoMonetaria", typeof(IndiceCorrecaoMonetaria),      TenantFilterKind.GlobalTable),

        new("ConfiguracoesHonorario",   typeof(ConfiguracaoHonorario),        TenantFilterKind.TenantId),
        new("HonorariosCalculos",       typeof(HonorarioCalculo),             TenantFilterKind.TenantId),

        new("Processos",                typeof(Processo),                     TenantFilterKind.TenantId),
        new("ProcessoPartes",           typeof(ProcessoParte),                TenantFilterKind.ProcessoIdInTenant),
        new("Andamentos",               typeof(Andamento),                    TenantFilterKind.TenantId),
        new("TraducoesAndamentos",      typeof(TraducaoAndamento),            TenantFilterKind.TenantId),

        new("Eventos",                  typeof(Evento),                       TenantFilterKind.TenantId),
        new("Tarefas",                  typeof(Tarefa),                       TenantFilterKind.TenantId),
        new("TarefaTags",               typeof(TarefaTag),                    TenantFilterKind.TarefaIdInTenant),

        new("Notificacoes",             typeof(Notificacao),                  TenantFilterKind.TenantId),
        new("Publicacoes",              typeof(Publicacao),                   TenantFilterKind.TenantId),

        new("PecasGeradas",             typeof(PecaGerada),                   TenantFilterKind.TenantId),
        new("ResumosProcesso",          typeof(ResumoProcesso),               TenantFilterKind.TenantId),

        // ContratosHonorario/ParcelasHonorario precisam vir antes de Documentos e
        // LancamentosFinanceiros: Documento.ContratoId e LancamentoFinanceiro.
        // ContratoHonorarioId/ParcelaHonorarioId referenciam essas tabelas. A ordem aqui
        // também é usada (revertida) como ordem de DELETE no wipe — nessa direção,
        // LancamentosFinanceiros precisa ser apagado antes de ContratosHonorario/
        // ParcelasHonorario, senão o Postgres bloqueia o DELETE por violação de FK.
        new("ContratosHonorario",       typeof(ContratoHonorario),            TenantFilterKind.TenantId),
        new("ParcelasHonorario",        typeof(ParcelaHonorario),             TenantFilterKind.TenantId),
        new("HistoricosContratoHonorario", typeof(HistoricoContratoHonorario),TenantFilterKind.TenantId),

        new("Documentos",               typeof(Documento),                    TenantFilterKind.TenantId),
        new("LancamentosFinanceiros",   typeof(LancamentoFinanceiro),         TenantFilterKind.TenantId),

        new("Atendimentos",             typeof(Atendimento),                  TenantFilterKind.TenantId),

        new("AcessosCliente",           typeof(AcessoCliente),                TenantFilterKind.TenantId),
    };

    public static readonly List<IdentityJoinSpec> IdentityJoinTables = new()
    {
        new("AspNetUserRoles",   typeof(IdentityUserRole<Guid>), nameof(IdentityUserRole<Guid>.UserId)),
        new("AspNetUserClaims",  typeof(IdentityUserClaim<Guid>), nameof(IdentityUserClaim<Guid>.UserId)),
        new("AspNetUserLogins",  typeof(IdentityUserLogin<Guid>), nameof(IdentityUserLogin<Guid>.UserId)),
        new("AspNetUserTokens",  typeof(IdentityUserToken<Guid>), nameof(IdentityUserToken<Guid>.UserId)),
    };

    // "Usuarios" já é o primeiro item de ExportOrder — não duplicar aqui. Duplicá-lo fazia
    // essa entrada extra virar a PRIMEIRA da lista revertida (TableKeysForDeletionReversed),
    // apagando AspNetUsers antes de Tarefas/Processos/etc., que referenciam o usuário por FK
    // (ex.: Tarefas.CriadoPorId) — violando a constraint no DELETE.
    public static readonly List<string> TableKeysForDeletion = ExportOrder
        .Select(s => s.JsonKey)
        .Concat(IdentityJoinTables.Select(s => s.JsonKey))
        .ToList();

    public static readonly List<string> TableKeysForDeletionReversed = TableKeysForDeletion
        .AsEnumerable()
        .Reverse()
        .ToList();

    public static readonly List<TenantTableSpec> ImportOrder = ExportOrder;

    public static readonly List<IdentityJoinSpec> IdentityJoinOrder = IdentityJoinTables;
}
