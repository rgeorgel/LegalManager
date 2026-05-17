using System.Security.Claims;
using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.DTOs.PortalCliente;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/portal")]
public class PortalClienteController : ControllerBase
{
    private readonly IPortalClienteService _service;
    private readonly IDocumentoService _documentoService;
    private readonly ITenantContext _tenantContext;

    public PortalClienteController(
        IPortalClienteService service,
        IDocumentoService documentoService,
        ITenantContext tenantContext)
    {
        _service = service;
        _documentoService = documentoService;
        _tenantContext = tenantContext;
    }

    [HttpPost("login")]
    public async Task<ActionResult<PortalAuthResponseDto>> Login([FromBody] LoginPortalDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _service.LoginAsync(dto, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpGet("me")]
    [Authorize(Roles = "Cliente")]
    public async Task<ActionResult<ClientePerfilDto>> GetMe(CancellationToken ct)
    {
        var acessoId = GetAcessoId();
        var result = await _service.GetPerfilAsync(acessoId, ct);
        return Ok(result);
    }

    [HttpGet("meus-processos")]
    [Authorize(Roles = "Cliente")]
    public async Task<ActionResult<IEnumerable<MeuProcessoDto>>> GetMeusProcessos(CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var result = await _service.GetMeusProcessosAsync(contatoId, tenantId, ct);
        return Ok(result);
    }

    [HttpGet("meus-processos/{processoId:guid}")]
    [Authorize(Roles = "Cliente")]
    public async Task<ActionResult<MeuProcessoDto>> GetProcesso(Guid processoId, CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var result = await _service.GetProcessoAsync(processoId, contatoId, tenantId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("meus-processos/{processoId:guid}/andamentos")]
    [Authorize(Roles = "Cliente")]
    public async Task<ActionResult<IEnumerable<MeuAndamentoDto>>> GetAndamentos(Guid processoId, CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var result = await _service.GetAndamentosAsync(processoId, contatoId, tenantId, ct);
        return Ok(result);
    }

    [HttpGet("meus-processos/{processoId:guid}/documentos")]
    [Authorize(Roles = "Cliente")]
    public async Task<ActionResult<IEnumerable<DocumentoDto>>> GetDocumentos(Guid processoId, CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var processo = await _service.GetProcessoAsync(processoId, contatoId, tenantId, ct);
        if (processo == null)
            return NotFound();

        var documentos = await _documentoService.GetByProcessoAsync(processoId, ct);
        return Ok(documentos);
    }

    [HttpPost("meus-processos/{processoId:guid}/documentos")]
    [Authorize(Roles = "Cliente")]
    [RequestSizeLimit(100_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)]
    public async Task<ActionResult<DocumentoDto>> UploadDocumento(
        Guid processoId,
        IFormFile file,
        [FromForm] TipoDocumento tipo = TipoDocumento.Prova,
        [FromForm] string? nome = null,
        CancellationToken ct = default)
    {
        var acessoId = GetAcessoId();
        var (contatoId, tenantId) = GetContatoTenant();
        var processo = await _service.GetProcessoAsync(processoId, contatoId, tenantId, ct);
        if (processo == null)
            return NotFound();

        if (file == null || file.Length == 0)
            return BadRequest("Arquivo não fornecido.");

        using var stream = file.OpenReadStream();
        var uploadInfo = new DocumentoUploadDto
        {
            ProcessoId = processoId,
            Tipo = tipo,
            Nome = nome
        };

        var result = await _documentoService.UploadAsync(stream, file.FileName, file.ContentType, uploadInfo, null, ct);
        return Created($"/api/portal/meus-processos/{processoId}/documentos/{result.Id}", result);
    }

    private Guid GetAcessoId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);

    private (Guid contatoId, Guid tenantId) GetContatoTenant() => (
        Guid.Parse(User.FindFirstValue("contatoId")!),
        Guid.Parse(User.FindFirstValue("tenantId")!)
    );
}
