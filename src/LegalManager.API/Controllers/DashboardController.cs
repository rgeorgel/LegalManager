using System.Text.Json;
using System.Text.RegularExpressions;
using LegalManager.Application.DTOs.Dashboard;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Layout personalizado do dashboard (ordem, largura e visibilidade dos blocos),
// salvo por usuário em Usuario.DashboardLayout. Os ids dos blocos são definidos
// pelo frontend: aqui só validamos formato e limites, para que blocos novos não
// exijam mudança no backend — ids desconhecidos são ignorados pela página.
[ApiController]
[Route("api/dashboard")]
[Authorize]
public partial class DashboardController : ControllerBase
{
    private const int MaxWidgets = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public DashboardController(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    [HttpGet("layout")]
    public async Task<ActionResult<DashboardLayoutDto>> GetLayout(CancellationToken ct)
    {
        var json = await _context.Users
            .Where(u => u.Id == _tenantContext.UserId)
            .Select(u => u.DashboardLayout)
            .FirstOrDefaultAsync(ct);

        return Ok(new DashboardLayoutDto(Parse(json)));
    }

    [HttpPut("layout")]
    public async Task<ActionResult<DashboardLayoutDto>> SalvarLayout([FromBody] DashboardLayoutDto dto, CancellationToken ct)
    {
        var widgets = dto.Widgets ?? [];
        if (widgets.Count == 0)
            return BadRequest(new { message = "Informe ao menos um bloco." });
        if (widgets.Count > MaxWidgets)
            return BadRequest(new { message = $"Máximo de {MaxWidgets} blocos." });
        if (widgets.Any(w => w.Id is null || !WidgetIdRegex().IsMatch(w.Id)))
            return BadRequest(new { message = "Id de bloco inválido." });
        if (widgets.Any(w => w.Largura is < 1 or > 3))
            return BadRequest(new { message = "Largura deve ser entre 1 e 3 colunas." });
        if (widgets.Select(w => w.Id).Distinct().Count() != widgets.Count)
            return BadRequest(new { message = "Blocos duplicados." });

        var usuario = await _context.Users.FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);
        if (usuario == null) return NotFound();

        usuario.DashboardLayout = JsonSerializer.Serialize(widgets, JsonOptions);
        await _context.SaveChangesAsync(ct);
        return Ok(new DashboardLayoutDto(widgets));
    }

    /// <summary>Volta ao layout padrão.</summary>
    [HttpDelete("layout")]
    public async Task<IActionResult> RestaurarPadrao(CancellationToken ct)
    {
        var usuario = await _context.Users.FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);
        if (usuario == null) return NotFound();

        usuario.DashboardLayout = null;
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    private static List<DashboardWidgetDto>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<DashboardWidgetDto>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex WidgetIdRegex();
}
