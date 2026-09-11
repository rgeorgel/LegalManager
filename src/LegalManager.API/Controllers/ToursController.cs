using System.Text.Json;
using LegalManager.Application.DTOs.Tours;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.API.Controllers;

// Tutorial guiado (product tour) — status de conclusão por usuário. Segue o
// mesmo padrão de OnboardingController/AssinaturaController (trial-boas-vindas):
// GET .../status → checagem no carregamento da página; POST .../concluir →
// marcação idempotente ao terminar um tour.
[ApiController]
[Route("api/tours")]
[Authorize]
public class ToursController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ToursController> _logger;

    public ToursController(AppDbContext context, ITenantContext tenantContext, ILogger<ToursController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<TourStatusDto>> GetStatus(CancellationToken ct)
    {
        var usuario = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);

        return Ok(new TourStatusDto(ParseConcluidos(usuario?.ToursConcluidos)));
    }

    [HttpPost("concluir")]
    public async Task<ActionResult> Concluir([FromBody] MarcarTourConcluidoDto dto, CancellationToken ct)
    {
        var usuario = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);

        if (usuario == null) return NotFound();

        var concluidos = ParseConcluidos(usuario.ToursConcluidos);
        if (!concluidos.Contains(dto.TourId))
        {
            concluidos.Add(dto.TourId);
            usuario.ToursConcluidos = JsonSerializer.Serialize(concluidos);
            await _context.SaveChangesAsync(ct);
        }

        return Ok();
    }

    // Telemetria leve de uso/abandono por passo (Fase 3). Sem tabela dedicada
    // por enquanto — vira dado real de análise só via log estruturado até
    // haver necessidade concreta de um relatório.
    [HttpPost("evento")]
    public ActionResult Evento([FromBody] TourEventoDto dto)
    {
        _logger.LogInformation(
            "[Tour] tenant={TenantId} user={UserId} tour={TourId} step={StepId} acao={Acao}",
            _tenantContext.TenantId, _tenantContext.UserId, dto.TourId, dto.StepId, dto.Acao);
        return Ok();
    }

    private static List<string> ParseConcluidos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
