using System.Text.Json;
using System.Text.RegularExpressions;
using LegalManager.Application.DTOs.Dashboard;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Layout personalizado do dashboard (ordem, largura, altura e visibilidade dos blocos e
// dos KPIs do topo), salvo por usuário em Usuario.DashboardLayout. Os ids dos blocos são definidos
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

        return Ok(Parse(json));
    }

    [HttpPut("layout")]
    public async Task<ActionResult<DashboardLayoutDto>> SalvarLayout([FromBody] DashboardLayoutDto dto, CancellationToken ct)
    {
        if (dto.Widgets is null && dto.Kpis is null)
            return BadRequest(new { message = "Informe os blocos ou os KPIs." });
        if ((Validar(dto.Widgets) ?? Validar(dto.Kpis)) is { } erro)
            return BadRequest(new { message = erro });

        var usuario = await _context.Users.FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);
        if (usuario == null) return NotFound();

        var layout = new DashboardLayoutDto(dto.Widgets, dto.Kpis);
        usuario.DashboardLayout = JsonSerializer.Serialize(layout, JsonOptions);
        await _context.SaveChangesAsync(ct);
        return Ok(layout);
    }

    /// <summary>Valida uma seção do layout; null = válida (seção ausente também é válida).</summary>
    private static string? Validar(List<DashboardWidgetDto>? itens)
    {
        if (itens is null) return null;
        if (itens.Count == 0) return "Informe ao menos um item.";
        if (itens.Count > MaxWidgets) return $"Máximo de {MaxWidgets} itens por seção.";
        if (itens.Any(w => w.Id is null || !WidgetIdRegex().IsMatch(w.Id))) return "Id de item inválido.";
        if (itens.Any(w => w.Largura is < 1 or > 3)) return "Largura deve ser entre 1 e 3 colunas.";
        if (itens.Any(w => w.Altura is not (0.5 or 1 or 2))) return "Altura deve ser ½, 1 ou 2 blocos.";
        if (itens.Select(w => w.Id).Distinct().Count() != itens.Count) return "Itens duplicados.";
        return null;
    }

    /// <summary>Versão do dashboard escolhida pelo usuário ("novo" ou "classico").</summary>
    [HttpPut("versao")]
    public async Task<IActionResult> SalvarVersao([FromBody] DashboardVersaoDto dto, CancellationToken ct)
    {
        if (dto.Versao is not ("novo" or "classico"))
            return BadRequest(new { message = "Versão deve ser \"novo\" ou \"classico\"." });

        var usuario = await _context.Users.FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);
        if (usuario == null) return NotFound();

        usuario.DashboardVersao = dto.Versao;
        await _context.SaveChangesAsync(ct);
        return NoContent();
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

    private static DashboardLayoutDto Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DashboardLayoutDto(null);
        try
        {
            // Formato antigo (antes dos KPIs personalizáveis): só a lista de blocos.
            if (json.TrimStart().StartsWith('['))
                return new DashboardLayoutDto(JsonSerializer.Deserialize<List<DashboardWidgetDto>>(json, JsonOptions));
            return JsonSerializer.Deserialize<DashboardLayoutDto>(json, JsonOptions) ?? new DashboardLayoutDto(null);
        }
        catch (JsonException)
        {
            return new DashboardLayoutDto(null);
        }
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex WidgetIdRegex();
}
