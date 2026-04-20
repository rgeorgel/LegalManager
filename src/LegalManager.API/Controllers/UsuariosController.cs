using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/usuarios")]
[Authorize]
public class UsuariosController : ControllerBase
{
    private readonly UserManager<Usuario> _userManager;
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public UsuariosController(UserManager<Usuario> userManager, AppDbContext context, ITenantContext tenantContext)
    {
        _userManager = userManager;
        _context = context;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var usuarios = await _context.Users
            .Where(u => u.TenantId == _tenantContext.TenantId)
            .Select(u => new { u.Id, u.Nome, u.Email, u.Perfil, u.Ativo, u.CriadoEm })
            .ToListAsync(ct);

        return Ok(usuarios);
    }

    [HttpPut("{id:guid}/desativar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken ct)
    {
        var usuario = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, ct);

        if (usuario == null) return NotFound();
        if (usuario.Id == _tenantContext.UserId) return BadRequest(new { message = "Não é possível desativar a própria conta." });

        usuario.Ativo = false;
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/perfil")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AlterarPerfil(Guid id, [FromBody] AlterarPerfilDto dto, CancellationToken ct)
    {
        var usuario = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, ct);

        if (usuario == null) return NotFound();

        if (!Enum.TryParse<PerfilUsuario>(dto.Perfil, true, out var novoPerfil))
            return BadRequest(new { message = "Perfil inválido." });

        var rolesAtuais = await _userManager.GetRolesAsync(usuario);
        await _userManager.RemoveFromRolesAsync(usuario, rolesAtuais);
        await _userManager.AddToRoleAsync(usuario, novoPerfil.ToString());

        usuario.Perfil = novoPerfil;
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var usuario = await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId);

        if (usuario == null) return NotFound();

        return Ok(new
        {
            usuario.Id,
            usuario.Nome,
            usuario.Email,
            usuario.Perfil,
            usuario.Ativo,
            Tenant = new { usuario.Tenant.Id, usuario.Tenant.Nome, usuario.Tenant.Plano, usuario.Tenant.Status, usuario.Tenant.TrialExpiraEm }
        });
    }
}

public record AlterarPerfilDto(string Perfil);
