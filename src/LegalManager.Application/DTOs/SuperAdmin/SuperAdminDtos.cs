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
    int DocumentoCount,
    long DocumentoTamanhoBytes,
    int TarefaCount
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
    int DocumentoCount,
    long DocumentoTamanhoBytes,
    int TarefaCount,
    List<TenantUserDto> Usuarios
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
