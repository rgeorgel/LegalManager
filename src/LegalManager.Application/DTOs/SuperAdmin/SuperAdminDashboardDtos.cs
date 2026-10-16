namespace LegalManager.Application.DTOs.SuperAdmin;

/// <summary>GET /api/superadmin/dashboard — indicadores do dashboard do superadmin.</summary>
public record SuperAdminDashboardDto(
    DateTime GeradoEm,
    ReceitaDashboardDto Receita,
    CadastrosDashboardDto Cadastros,
    AcessosDashboardDto Acessos,
    TenantsDashboardDto Tenants,
    TrialDashboardDto Trial,
    CuponsDashboardDto Cupons,
    PerguntasDashboardDto Perguntas,
    UsoDashboardDto Uso
);

/// <summary>
/// Receita dos tenants pagantes (status Ativo com assinatura Stripe). Valores vêm da prévia da
/// próxima fatura no Stripe (já com cupons e prorações); quando o Stripe falha para um tenant,
/// usa o preço de tabela com o desconto do código promocional e conta em <see cref="Estimados"/>.
/// </summary>
public record ReceitaDashboardDto(
    decimal MrrReal,
    decimal ReceberProximos30Dias,
    int Pagantes,
    int CancelamentoAgendado,
    int Estimados,
    decimal RecebidoUltimos30Dias,
    decimal RecebidoAnteriores30Dias,
    decimal MrrTabela,
    List<ReceitaPlanoDto> PorPlano,
    List<ProximaCobrancaDto> ProximasCobrancas,
    DateTime CalculadoEm
);

public record ReceitaPlanoDto(string Plano, int Tenants, decimal Mrr);

public record ProximaCobrancaDto(Guid TenantId, string Nome, string Plano, string? Periodo, decimal Valor, DateTime? Data, bool Estimado);

public record CadastrosDashboardDto(
    int Ultimos7Dias,
    int Anteriores7Dias,
    int UltimosTrintaDias,
    List<SerieSemanaDto> PorSemana,
    List<RecentTenantDto> Recentes
);

/// <summary>Semana móvel: <c>Inicio</c> é o primeiro dia (Brasília) da janela de 7 dias.</summary>
public record SerieSemanaDto(DateOnly Inicio, int Total);

public record SerieDiaDto(DateOnly Dia, int Total);

public record AcessosDashboardDto(
    int TotalUsuarios,
    int Acessaram24h,
    int Acessaram7Dias,
    int Acessaram30Dias,
    int NuncaAcessaram,
    int Logins7Dias,
    int LoginsAnteriores7Dias,
    List<SerieDiaDto> LoginsPorDia
);

public record TenantsDashboardDto(
    int Total,
    int Ativos,
    int Trial,
    int Suspensos,
    int Cancelados,
    Dictionary<string, int> PorPlano,
    int Cancelamentos30Dias,
    int WaitlistPendente
);

public record TrialDashboardDto(
    int Tenants,
    int Usuarios,
    int ConcedidosPeloSuperadmin,
    List<TrialExpirandoDto> Expirando
);

public record TrialExpirandoDto(Guid TenantId, string Nome, string Plano, DateTime ExpiraEm, int Usuarios);

public record CuponsDashboardDto(
    int TenantsComCodigo,
    int UsuariosComCodigo,
    int DescontoUsadoNoCheckout,
    int PagantesComCodigo,
    List<CodigoUsoDto> Codigos
);

public record CodigoUsoDto(string Codigo, int Tenants, int Pagantes);

/// <summary>
/// Perguntas de pesquisa ativas para advogados/equipe: quem está no público de alguma pergunta
/// ativa e quem ainda tem pergunta por responder (mesma regra de GET /api/perguntas/pendentes).
/// </summary>
public record PerguntasDashboardDto(
    int Ativas,
    int UsuariosNoPublico,
    int UsuariosComPendentes,
    int Respostas7Dias,
    List<PerguntaAtivaResumoDto> PorPergunta
);

public record PerguntaAtivaResumoDto(Guid Id, string Texto, string Publico, int Alcance, int Respostas);

public record UsoDashboardDto(
    int Processos,
    int ProcessosNovos7Dias,
    int Monitorados,
    int InteracoesIa7Dias,
    int UsuariosIa7Dias,
    int ConsultasExternas7Dias,
    int ConsultasExternasFalhas7Dias
);
