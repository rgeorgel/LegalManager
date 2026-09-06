using System.Diagnostics;

namespace LegalManager.Infrastructure.Observability;

public static class Telemetry
{
    public const string ServiceName = "causify-api";

    public static readonly ActivitySource Escavador = new("LegalManager.Escavador");
    public static readonly ActivitySource Ia = new("LegalManager.IA");
    public static readonly ActivitySource Stripe = new("LegalManager.Stripe");
    public static readonly ActivitySource Hangfire = new("LegalManager.Hangfire");
    public static readonly ActivitySource Tribunais = new("LegalManager.Tribunais");

    public static class Tags
    {
        public const string TenantId = "causify.tenant_id";
        public const string UserId = "causify.user_id";
        public const string UserRole = "causify.user_role";
        public const string Plano = "causify.plano";
        public const string ImpersonadoPorId = "causify.impersonado_por_id";
        public const string AuditAcao = "causify.audit.acao";
        public const string AuditEntidade = "causify.audit.entidade";
        public const string AuditEntidadeId = "causify.audit.entidade_id";
        public const string RequestId = "causify.request_id";
        public const string CorrelationId = "causify.correlation_id";
    }
}
