using System.Globalization;
using System.Text.Json;
using LegalManager.Application.DTOs.Prazos;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/prazos")]
[Authorize]
public class PrazosController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost("calcular")]
    public async Task<IActionResult> Calcular([FromBody] CalcularPrazoDto dto, CancellationToken ct)
    {
        if (!PlanoRestricoes.PermiteCalculadoraPrazos(tenantContext.Plano))
            return StatusCode(402, new { message = "Calculadora de prazos disponível a partir do plano Plus." });

        var (dataFinal, feriados) = FeriadosService.CalcularPrazo(
            dto.DataInicio, dto.QuantidadeDias, dto.TipoCalculo, dto.FeriadosAdicionais);

        // Registro de uso com os parâmetros — o superadmin pode recalcular depois (timeline do tenant).
        db.CalculosPrazo.Add(new CalculoPrazo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.TenantId,
            UsuarioId = tenantContext.UserId,
            CriadoEm = DateTime.UtcNow,
            DataInicio = dto.DataInicio,
            QuantidadeDias = dto.QuantidadeDias,
            TipoCalculo = dto.TipoCalculo,
            FeriadosAdicionaisJson = dto.FeriadosAdicionais is { Length: > 0 }
                ? JsonSerializer.Serialize(dto.FeriadosAdicionais.Select(f => f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                : null,
            DataFinal = dataFinal
        });
        await db.SaveChangesAsync(ct);

        return Ok(new CalcularPrazoResultDto(
            dto.DataInicio, dto.QuantidadeDias, dto.TipoCalculo,
            dataFinal, 0, feriados));
    }
}
