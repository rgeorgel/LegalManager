using System.Diagnostics;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

public class ConsultaExternaLogService : IConsultaExternaLogService
{
    // Defesa contra respostas gigantes (ex: página cheia de andamentos com snippets longos)
    // inchando a tabela — o essencial para o detalhe da consulta cabe bem antes disso.
    private const int MaxJsonLength = 20_000;

    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ConsultaExternaLogService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public ConsultaExternaLogService(
        AppDbContext context, ITenantContext tenantContext, ILogger<ConsultaExternaLogService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<T> RegistrarAsync<T>(
        string api,
        string tipoConsulta,
        string origem,
        object? parametros,
        Func<Task<T>> execucao,
        Func<T, (int? Total, object? Resumo)>? extrairResumo = null,
        Guid? tenantId = null,
        Guid? usuarioId = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var resultado = await execucao();
            sw.Stop();

            int? total = null;
            object? resumo = null;
            if (extrairResumo != null)
            {
                try { (total, resumo) = extrairResumo(resultado); }
                catch (Exception ex) { _logger.LogWarning(ex, "[ConsultaExterna] Falha ao extrair resumo de {Api}/{Tipo}", api, tipoConsulta); }
            }

            await SalvarAsync(api, tipoConsulta, origem, parametros, tenantId, usuarioId,
                sucesso: true, mensagemErro: null, total, resumo, (int)sw.ElapsedMilliseconds, ct);

            return resultado;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await SalvarAsync(api, tipoConsulta, origem, parametros, tenantId, usuarioId,
                sucesso: false, mensagemErro: ex.Message, total: null, resumo: null, (int)sw.ElapsedMilliseconds, ct);
            throw;
        }
    }

    private async Task SalvarAsync(
        string api, string tipoConsulta, string origem, object? parametros,
        Guid? tenantId, Guid? usuarioId,
        bool sucesso, string? mensagemErro, int? total, object? resumo, int duracaoMs,
        CancellationToken ct)
    {
        try
        {
            var log = new ConsultaExterna
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId ?? _tenantContext.TenantId,
                UsuarioId = usuarioId ?? (_tenantContext.UserId == Guid.Empty ? null : _tenantContext.UserId),
                CriadoEm = DateTime.UtcNow,
                Api = api,
                TipoConsulta = tipoConsulta,
                Origem = origem,
                ParametrosJson = Truncar(parametros != null ? JsonSerializer.Serialize(parametros, JsonOptions) : null),
                Sucesso = sucesso,
                MensagemErro = mensagemErro,
                QuantidadeResultados = total,
                ResultadoResumoJson = Truncar(resumo != null ? JsonSerializer.Serialize(resumo, JsonOptions) : null),
                DuracaoMs = duracaoMs
            };

            _context.ConsultasExternas.Add(log);
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Auditoria nunca pode derrubar a chamada real à API externa.
            _logger.LogWarning(ex, "[ConsultaExterna] Falha ao gravar log de {Api}/{Tipo} ({Origem})", api, tipoConsulta, origem);
        }
    }

    private static string? Truncar(string? json) =>
        json != null && json.Length > MaxJsonLength ? json[..MaxJsonLength] : json;
}
