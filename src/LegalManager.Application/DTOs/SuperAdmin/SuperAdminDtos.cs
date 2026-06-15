namespace LegalManager.Application.DTOs.SuperAdmin;

public record TenantListItemDto(
    Guid Id,
    string Nome,
    string? Cnpj,
    string Plano,
    string Status,
    DateTime CriadoEm,
    int UserCount,
    DateTime? TrialExpiraEm,
    DateTime? PlanoExpiraEm,
    int ProcessoCount,
    int MonitoradoCount,
    int MonitoradoDiarioCount,
    int MonitoradoSemanalCount,
    int DocumentoCount,
    long DocumentoTamanhoBytes,
    int TarefaCount,
    int TarefaPendenteCount,
    int TarefaEmAndamentoCount,
    int TarefaConcluidaCount,
    int TarefaPerdidaCount,
    int UsuariosLimite,
    int MonitoradosLimite,
    long ArmazenamentoLimiteMB,
    int OabCount,
    int OabComErroSync,
    int PublicacoesMesAtual,
    DateTime? UltimoAcessoEm
);

public record TenantDetailDto(
    Guid Id,
    string Nome,
    string? Cnpj,
    string? Endereco,
    string Plano,
    string? PeriodoBilling,
    string Status,
    DateTime CriadoEm,
    int UserCount,
    DateTime? TrialExpiraEm,
    DateTime? PlanoExpiraEm,
    string? AbacatePayBillingId,
    int ProcessoCount,
    int MonitoradoCount,
    int MonitoradoDiarioCount,
    int MonitoradoSemanalCount,
    int DocumentoCount,
    long DocumentoTamanhoBytes,
    int TarefaCount,
    int TarefaPendenteCount,
    int TarefaEmAndamentoCount,
    int TarefaConcluidaCount,
    int TarefaPerdidaCount,
    int UsuariosLimite,
    int MonitoradosLimite,
    long ArmazenamentoLimiteMB,
    int OabCount,
    int OabComErroSync,
    int PublicacoesMesAtual,
    DateTime? UltimoAcessoEm,
    List<TenantUserDto> Usuarios,
    List<TenantOabResumoDto> Oabs
);

public record TenantOabResumoDto(
    Guid Id,
    string Uf,
    string Numero,
    string Nome,
    bool Ativo,
    bool Sincronizada,
    string? SyncError,
    DateTime? UltimoSyncEm
);

public record TenantUserDto(
    Guid Id,
    string Nome,
    string? Email,
    string Perfil,
    bool Ativo,
    DateTime? UltimoAcessoEm
);

public record UpdateTenantPlanoDto(string Plano, string? PeriodoBilling);

public record UpdateTenantStatusDto(string Status);

public record ConcederTrialPlusDto(int Dias, string? Motivo);

public record TenantTrialInfoDto(
    bool TrialConcedido,
    int? Dias,
    DateTime? ConcedidoEm,
    string? ConcedidoPorNome,
    DateTime? ExpiraEm,
    string? Motivo
);

public record UserListItemDto(
    Guid Id,
    string Nome,
    string? Email,
    Guid TenantId,
    string TenantNome,
    string TenantPlano,
    string Perfil,
    bool Ativo,
    DateTime? UltimoAcessoEm,
    DateTime CriadoEm
);

public record WaitlistListItemDto(
    Guid Id,
    string Nome,
    string Email,
    string Plano,
    DateTime CriadoEm,
    string Status
);

public record UpdateWaitlistStatusDto(string Status);

public record SystemMetricsDto(
    int TotalTenants,
    int ActiveTenants,
    int TrialTenants,
    int SuspendedTenants,
    int CanceledTenants,
    Dictionary<string, int> TenantsByPlan,
    int TotalUsers,
    int ActiveUsers,
    int WaitlistPending,
    decimal MrrEstimate,
    List<RecentTenantDto> RecentSignups
);

public record RecentTenantDto(
    Guid Id,
    string Nome,
    string Plano,
    string Status,
    DateTime CriadoEm
);
