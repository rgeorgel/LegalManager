using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

public record EntrarWaitlistDto(string Nome, string Email, string Plano);

[ApiController]
[Route("api/waitlist")]
public class WaitlistController(AppDbContext context) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Entrar([FromBody] EntrarWaitlistDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome) || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Plano))
            return BadRequest(new { message = "Nome, e-mail e plano são obrigatórios." });

        var planosValidos = new[] { "Plus", "Pro", "Max" };
        if (!planosValidos.Contains(dto.Plano))
            return BadRequest(new { message = "Plano inválido." });

        var jaRegistrado = await context.WaitlistEntries
            .AnyAsync(w => w.Email == dto.Email && w.Plano == dto.Plano, ct);

        if (jaRegistrado)
            return Ok(new { message = "Você já está na lista de espera para este plano.", jaRegistrado = true });

        Guid? userId = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var parsed))
                userId = parsed;
        }

        context.WaitlistEntries.Add(new WaitlistEntry
        {
            Id = Guid.NewGuid(),
            Nome = dto.Nome.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            Plano = dto.Plano,
            UserId = userId,
            CriadoEm = DateTime.UtcNow
        });

        await context.SaveChangesAsync(ct);

        return Ok(new { message = "Você entrou na lista de espera com sucesso!", jaRegistrado = false });
    }
}
