using LegalManager.Application.DTOs.Onboarding;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/onboarding")]
[Authorize]
public class OnboardingController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IImportadorProcessosOab _importador;

    public OnboardingController(
        AppDbContext context,
        ITenantContext tenantContext,
        IImportadorProcessosOab importador)
    {
        _context = context;
        _tenantContext = tenantContext;
        _importador = importador;
    }

    [HttpGet("status")]
    public async Task<ActionResult<OnboardingStatusDto>> GetStatus(CancellationToken ct)
    {
        var usuario = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);
        return Ok(new OnboardingStatusDto(
            usuario?.OnboardingImportacaoCompleto ?? false,
            usuario?.OabImportadaNumero,
            usuario?.OabImportadaUf));
    }

    [HttpGet("oabs-importadas")]
    public async Task<ActionResult<List<OabImportadaDto>>> OabsImportadas(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var oabs = await _context.Users
            .Where(u => u.TenantId == tenantId && u.OabImportadaNumero != null)
            .OrderBy(u => u.Nome)
            .Select(u => new OabImportadaDto(
                u.OabImportadaNumero!,
                u.OabImportadaUf!,
                u.Nome,
                _context.Processos.Count(p => p.TenantId == tenantId && p.AdvogadoResponsavelId == u.Id)))
            .ToListAsync(ct);
        return Ok(oabs);
    }

    /// <summary>
    /// Busca síncrona para o usuário escolher o que importar. A importação em si roda em
    /// background — ver <see cref="ImportacoesController"/>.
    /// </summary>
    [HttpPost("buscar-por-oab")]
    public async Task<ActionResult<List<ProcessoOabPreviewDto>>> BuscarPorOab(
        BuscarPorOabDto dto, CancellationToken ct)
        => Ok(await _importador.BuscarAsync(dto.NumeroOAB, dto.Uf, ct));

    [HttpPost("completar")]
    public async Task<ActionResult> Completar(CancellationToken ct)
    {
        var usuario = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == _tenantContext.UserId, ct);

        if (usuario != null)
        {
            usuario.OnboardingImportacaoCompleto = true;
            await _context.SaveChangesAsync(ct);
        }

        return Ok();
    }
}
