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
    int ProcessoAtivoCount,
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
    bool TemaCustomizado,
    int HonorarioContratosCount,
    int CalculadoraPrazosCount,
    int CalculadoraHonorariosCount,
    bool TemBilling,
    // Mascotes do assistente escolhidos pelos usuários do escritório (sem os do portal do cliente).
    IReadOnlyList<MascoteUsoDto>? Mascotes = null,
    // Cores/layout salvos (null = nunca mexeu): o frontend descobre o preset com findPresetByTheme().
    TemaCoresDto? Tema = null
);

public record TemaCoresDto(string? PrimaryColor, string? SidebarColor, string? AccentColor, string? LayoutMode);

/// <summary>Quantos usuários usam cada mascote; Mascote null = nunca escolheu (padrão do frontend).</summary>
public record MascoteUsoDto(string? Mascote, int Usuarios);

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
    string? StripeSubscriptionId,
    int ProcessoCount,
    int ProcessoAtivoCount,
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
    List<TenantOabResumoDto> Oabs,
    bool TemaCustomizado,
    int HonorarioContratosCount,
    int CalculadoraPrazosCount,
    int CalculadoraHonorariosCount,
    string? VoucherUtilizado,
    string? CodigoDesconto,
    DateTime? DescontoPromocionalUsadoEm,
    List<OabImportadaResumoDto> OabsImportadas
);

/// <summary>OAB usada na importação de processos do onboarding (gravada no usuário, não em TenantOabs).</summary>
public record OabImportadaResumoDto(string Uf, string Numero, string NomeUsuario, int TotalProcessos, int ProcessosAtivos);

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
    DateTime? UltimoAcessoEm,
    string? AssistenteMascote = null
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
    DateTime CriadoEm,
    bool MfaAtivo = false
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

public record RecentTenantDto(
    Guid Id,
    string Nome,
    string Plano,
    string Status,
    DateTime CriadoEm
);
