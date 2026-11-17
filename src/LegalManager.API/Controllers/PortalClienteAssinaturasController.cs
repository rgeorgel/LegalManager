using System.Security.Claims;
using LegalManager.Application.DTOs.AssinaturaDigital;
using LegalManager.Infrastructure.AssinaturaDigital;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

/// <summary>
/// Portal do cliente: contratos enviados para o cliente assinar (pelo Contratos) e o PDF assinado.
/// Só os envios ligados ao contato do login; sem dados dos outros signatários.
/// </summary>
[ApiController]
[Route("api/portal/minhas-assinaturas")]
[Authorize(Roles = "Cliente")]
public class PortalClienteAssinaturasController(AssinaturaDigitalService service) : ControllerBase
{
    private (Guid contatoId, Guid tenantId) GetContatoTenant() => (
        Guid.TryParse(User.FindFirstValue("contatoId"), out var c) ? c : Guid.Empty,
        Guid.TryParse(User.FindFirstValue("tenantId"), out var t) ? t : Guid.Empty);

    [HttpGet]
    public async Task<ActionResult<List<AssinaturaPortalDto>>> Listar(CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        return Ok(await service.DoClienteAsync(tenantId, contatoId, ct));
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var arquivo = await service.PdfDoClienteAsync(tenantId, contatoId, id, ct);
        if (arquivo is not { } a) return NotFound();
        return File(a.Conteudo, "application/pdf", a.Nome);
    }
}
