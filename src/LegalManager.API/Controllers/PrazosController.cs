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

        var dataFinal = dto.TipoCalculo == TipoCalculo.DiasUteis
            ? FeriadosService.AdicionarDiasUteis(dto.DataInicio, dto.QuantidadeDias, dto.FeriadosAdicionais)
            : dto.DataInicio.Date.AddDays(dto.QuantidadeDias);

        var feriados = FeriadosService.ListarFeriadosNoIntervalo(dto.DataInicio, dataFinal, dto.FeriadosAdicionais);

        // Log leve de uso — não guarda o cálculo, só "quem usou, quando" (ver superadmin/tenants).
        db.CalculosPrazo.Add(new CalculoPrazo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.TenantId,
            UsuarioId = tenantContext.UserId,
            CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        return Ok(new CalcularPrazoResultDto(
            dto.DataInicio, dto.QuantidadeDias, dto.TipoCalculo,
            dataFinal, 0, feriados));
    }
}
