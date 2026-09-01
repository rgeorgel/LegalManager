using System.ComponentModel.DataAnnotations;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
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
    private readonly IAuditService _audit;

    public ConfiguracoesController(
        AppDbContext context,
        ITenantContext tenantContext,
        UserManager<Usuario> userManager,
        IAuditService audit)
    {
        _context = context;
        _tenantContext = tenantContext;
        _userManager = userManager;
        _audit = audit;
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

        var plano = _tenantContext.Plano;
        return Ok(new
        {
            Processos = processos,
            ProcessosMonitoradosLimite = PlanoRestricoes.MaxProcessosMonitorados(plano),
            Contatos = contatos,
            Usuarios = usuarios,
            UsuariosLimite = PlanoRestricoes.MaxUsuarios(plano),
            TarefasAbertas = tarefas,
            ArmazenamentoUsadoMB = 0,
            ArmazenamentoLimiteMB = PlanoRestricoes.ArmazenamentoLimiteMB(plano),
            Plano = plano.ToString()
        });
    }

    [HttpPost("upgrade")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Upgrade([FromBody] UpgradePlanoDto dto, CancellationToken ct)
    {
        var tenant = await _context.Tenants.FindAsync([_tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        if (!Enum.TryParse<PlanoTipo>(dto.Plano, true, out var novoPlano))
            return BadRequest(new { message = "Plano inválido." });

        tenant.Plano = novoPlano;
        tenant.Status = StatusTenant.Ativo;
        tenant.TrialExpiraEm = null;

        await _context.SaveChangesAsync(ct);
        return Ok(new { message = "Plano atualizado com sucesso." });
    }

    [HttpPost("cancelar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Cancelar(CancellationToken ct)
    {
        var tenant = await _context.Tenants.FindAsync([_tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        tenant.Plano = PlanoTipo.Free;
        tenant.Status = StatusTenant.Ativo;
        tenant.TrialExpiraEm = null;

        await _context.SaveChangesAsync(ct);
        return Ok(new { message = "Assinatura cancelada. Você foi movido para o plano Free." });
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

    [HttpDelete("conta")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ExcluirConta([FromBody] ExcluirContaDto dto, CancellationToken ct)
    {
        var tenant = await _context.Tenants.FindAsync([_tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        if (_tenantContext.ImpersonadoPorId is not null)
            return BadRequest(new { message = "Não é possível excluir a conta durante uma sessão de impersonação." });

        var assinaturaAtiva =
            tenant.Plano != PlanoTipo.Free
            && tenant.Status != StatusTenant.Trial
            && tenant.Status != StatusTenant.Cancelado;

        if (assinaturaAtiva)
        {
            return BadRequest(new
            {
                message = "Para excluir sua conta é necessário cancelar a assinatura ativa primeiro."
            });
        }

        var usuario = await _userManager.GetUserAsync(User);
        if (usuario is null) return Unauthorized();

        var senhaOk = await _userManager.CheckPasswordAsync(usuario, dto.Senha);
        if (!senhaOk)
            return BadRequest(new { message = "Senha incorreta." });

        var dadosAnteriores = new
        {
            tenant.Nome,
            tenant.Plano,
            tenant.Status,
            tenant.Cnpj,
            tenant.StripeCustomerId,
            tenant.StripeSubscriptionId,
            tenant.PlanoExpiraEm,
            tenant.TrialExpiraEm,
            Usuarios = await _context.Users
                .Where(u => u.TenantId == tenant.Id)
                .Select(u => new { u.Id, u.Nome, u.Email, u.Perfil })
                .ToListAsync(ct)
        };

        await _audit.LogAsync(_tenantContext.CreateEntry(
            AuditActions.Delete,
            "Tenant",
            tenant.Id,
            dadosAnteriores,
            null,
            HttpContext.GetClientIpAddress()), ct);

        var usuariosTenant = await _context.Users
            .Where(u => u.TenantId == tenant.Id)
            .ToListAsync(ct);
        foreach (var u in usuariosTenant)
        {
            await _userManager.DeleteAsync(u);
        }

        _context.Tenants.Remove(tenant);
        await _context.SaveChangesAsync(ct);

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

public record UpgradePlanoDto([Required] string Plano);

public record ExcluirContaDto([Required] string Senha);
