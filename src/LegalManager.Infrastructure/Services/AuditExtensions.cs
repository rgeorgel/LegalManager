using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;

namespace LegalManager.Infrastructure.Services;

public static class AuditExtensions
{
    public static AuditLogEntry CreateEntry(
        this ITenantContext tenantContext,
        string action,
        string entity,
        Guid? entityId = null,
        object? dadosAnteriores = null,
        object? dadosNovos = null,
        string? ipAddress = null)
    {
        return new AuditLogEntry(
            tenantContext.TenantId,
            tenantContext.UserId,
            action,
            entity,
            entityId?.ToString(),
            dadosAnteriores,
            dadosNovos,
            ipAddress
        );
    }

    /// <summary>
    /// Registro de auditoria para adicionar ao mesmo SaveChanges da operação — para quem não
    /// passa por IAuditService (webhooks sem usuário logado, AuthService).
    /// </summary>
    public static Domain.Entities.AuditLog NovoAuditLog(
        Guid tenantId, Guid? usuarioId, string acao, string entidade, string? entidadeId,
        object? dadosAnteriores = null, object? dadosNovos = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UsuarioId = usuarioId,
        Acao = acao,
        Entidade = entidade,
        EntidadeId = entidadeId,
        DadosAnteriores = dadosAnteriores is null ? null : System.Text.Json.JsonSerializer.Serialize(dadosAnteriores),
        DadosNovos = dadosNovos is null ? null : System.Text.Json.JsonSerializer.Serialize(dadosNovos),
        CriadoEm = DateTime.UtcNow
    };

    public static string GetClientIpAddress(this Microsoft.AspNetCore.Http.HttpContext? context)
    {
        if (context == null) return null;

        // Produção fica atrás da Cloudflare, que sobrescreve este cabeçalho com o IP real
        // (o visitante não consegue forjá-lo passando pela Cloudflare).
        var cloudflare = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(cloudflare))
            return cloudflare.Trim();

        // Sem Cloudflare: o ÚLTIMO item do X-Forwarded-For é o que o nosso proxy anexou; os
        // anteriores vêm do cliente e podem ser forjados (furando o limite por IP e a auditoria).
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
            return forwardedFor.Split(',')[^1].Trim();

        return context.Connection.RemoteIpAddress?.ToString();
    }
}

/// <summary>
/// Ações de auditoria que alimentam a timeline do tenant no superadmin
/// (TenantTimelineService). Ficam em AuditLog como as demais.
/// </summary>
public static class TimelineAcoes
{
    public const string AssinaturaIniciada = "assinatura-iniciada";
    public const string AssinaturaAlterada = "assinatura-alterada";
    public const string AssinaturaCancelada = "assinatura-cancelada";
    public const string AssinaturaEncerrada = "assinatura-encerrada";
    public const string PlanoExpirado = "plano-expirado";
    public const string PlanoAlterado = "plano-alterado";
    public const string StatusAlterado = "status-alterado";
    public const string TrialPlusConcedido = "trial-plus-concedido";
    public const string TemaAlterado = "tema-alterado";
    public const string LogoAlterado = "logo-alterado";
    public const string LogoRemovido = "logo-removido";
    public const string FusoAlterado = "fuso-alterado";
    public const string MonitoramentoAlternado = "monitoramento-alternado";

    public const string EntidadeAssinatura = "Assinatura";
}

public static class AuditActions
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string Login = "LOGIN";
    public const string Logout = "LOGOUT";
    public const string Access = "ACCESS";
    public const string ImpersonationStart = "IMPERSONATION_START";
    public const string ImpersonationEnd = "IMPERSONATION_END";
}

public static class AuditEntities
{
    public const string Contato = "Contato";
    public const string Processo = "Processo";
    public const string Tarefa = "Tarefa";
    public const string Evento = "Evento";
    public const string Financeiro = "LancamentoFinanceiro";
    public const string Usuario = "Usuario";
    public const string Documento = "Documento";
    public const string Configuracao = "Configuracao";
    public const string ContratoHonorario = "ContratoHonorario";
    public const string ParcelaHonorario = "ParcelaHonorario";
    public const string ConfiguracaoHonorario = "ConfiguracaoHonorario";
    public const string Atendimento = "Atendimento";
    public const string RegistroTempo = "RegistroTempo";
}