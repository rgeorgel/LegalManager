using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/configuracoes")]
[Authorize]
public class ConfiguracoesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly UserManager<Usuario> _userManager;

    public ConfiguracoesController(AppDbContext context, ITenantContext tenantContext, UserManager<Usuario> userManager)
    {
        _context = context;
        _tenantContext = tenantContext;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> GetConfiguracoes(CancellationToken ct)
    {
        var tenant = await _context.Tenants.FindAsync([_tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        return Ok(new
        {
            tenant.Id,
            tenant.Nome,
            tenant.Cnpj,
            tenant.Endereco,
            tenant.LogoUrl,
            tenant.Plano,
            tenant.Status,
            tenant.TrialExpiraEm,
            tenant.CriadoEm
        });
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateConfiguracoes([FromBody] UpdateConfiguracoesDto dto, CancellationToken ct)
    {
        var tenant = await _context.Tenants.FindAsync([_tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        tenant.Nome = dto.Nome;
        tenant.Cnpj = dto.Cnpj;
        tenant.Endereco = dto.Endereco;

        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("uso")]
    public async Task<IActionResult> GetUso(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;

        var processos = await _context.Processos.CountAsync(p => p.TenantId == tenantId, ct);
        var contatos = await _context.Contatos.CountAsync(c => c.TenantId == tenantId && c.Ativo, ct);
        var usuarios = await _context.Users.CountAsync(u => u.TenantId == tenantId && u.Ativo, ct);
        var tarefas = await _context.Tarefas.CountAsync(t => t.TenantId == tenantId &&
            t.Status != Domain.Enums.StatusTarefa.Concluida && t.Status != Domain.Enums.StatusTarefa.Cancelada, ct);

        return Ok(new
        {
            Processos = processos,
            ProcessosLimite = 500,
            Contatos = contatos,
            Usuarios = usuarios,
            UsuariosLimite = 5,
            TarefasAbertas = tarefas,
            ArmazenamentoUsadoMB = 0,
            ArmazenamentoLimiteMB = 20 * 1024
        });
    }

    [HttpPut("senha")]
    public async Task<IActionResult> AlterarSenha([FromBody] AlterarSenhaDto dto)
    {
        var usuario = await _userManager.GetUserAsync(User);
        if (usuario is null) return Unauthorized();

        var result = await _userManager.ChangePasswordAsync(usuario, dto.SenhaAtual, dto.NovaSenha);
        if (!result.Succeeded)
            return BadRequest(new { erros = result.Errors.Select(e => e.Description) });

        return NoContent();
    }
}

public record UpdateConfiguracoesDto(
    [Required, MaxLength(200)] string Nome,
    string? Cnpj,
    string? Endereco
);

public record AlterarSenhaDto(
    [Required] string SenhaAtual,
    [Required, MinLength(8)] string NovaSenha
);
