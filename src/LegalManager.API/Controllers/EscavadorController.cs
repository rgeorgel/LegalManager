using LegalManager.Infrastructure.Escavador;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/escavador")]
public class EscavadorController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly EscavadorCallbackProcessor _processor;

    public EscavadorController(
        IConfiguration config,
        IHostEnvironment env,
        EscavadorCallbackProcessor processor)
    {
        _config = config;
        _env = env;
        _processor = processor;
    }

    /// <summary>
    /// Webhook receptor de callbacks do Escavador (V2, V1 e shape antigo).
    /// Valida Bearer token configurado em Escavador:CallbackSecret.
    /// O 200 é o recebimento: o Escavador marca o callback como entregue. Erro (5xx) faz o Escavador
    /// tentar de novo, e o que esgotar as tentativas é recuperado pelo EscavadorCallbacksPendentesJob.
    /// </summary>
    [HttpPost("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceberCallback([FromBody] EscavadorCallbackWebhookPayload payload)
    {
        if (!ValidarCallbackToken()) return Unauthorized();
        if (payload == null) return BadRequest();

        await _processor.ProcessarAsync(payload);
        return Ok();
    }

    private bool ValidarCallbackToken()
    {
        var secret = _config["Escavador:CallbackSecret"] ?? "";
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Bloqueia em produção para evitar webhook "aberto" por esquecer de configurar secret.
            if (!_env.IsDevelopment())
                throw new InvalidOperationException("Escavador:CallbackSecret não configurado em ambiente não-Development.");
            return true;
        }
        var auth = Request.Headers.Authorization.ToString();
        // Aceita tanto "Bearer <token>" quanto "<token>" diretamente
        var token = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..]
            : auth;
        return string.Equals(token, secret, StringComparison.Ordinal);
    }
}
