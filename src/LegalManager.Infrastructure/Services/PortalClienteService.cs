using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LegalManager.Application.DTOs.PortalCliente;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LegalManager.Infrastructure.Services;

public class PortalClienteService : IPortalClienteService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IPasswordHasher<AcessoCliente> _hasher;

    public PortalClienteService(
        AppDbContext context,
        IConfiguration config,
        IPasswordHasher<AcessoCliente> hasher)
    {
        _context = context;
        _config = config;
        _hasher = hasher;
    }

    public async Task<PortalAuthResponseDto> LoginAsync(LoginPortalDto dto, CancellationToken ct = default)
    {
        var acesso = await _context.AcessosCliente
            .Include(a => a.Contato)
            .Include(a => a.Tenant)
            .FirstOrDefaultAsync(a => a.Email == dto.Email && a.Ativo, ct)
            ?? throw new UnauthorizedAccessException("E-mail ou senha inválidos.");

        var result = _hasher.VerifyHashedPassword(acesso, acesso.SenhaHash, dto.Senha);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException("E-mail ou senha inválidos.");

        acesso.UltimoAcessoEm = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var (token, expires) = GerarJwt(acesso);

        return new PortalAuthResponseDto(
            token,
            expires,
            new ClientePerfilDto(acesso.Id, acesso.ContatoId, acesso.Contato.Nome, acesso.Email, acesso.Contato.Telefone));
    }

    public async Task<ClientePerfilDto> GetPerfilAsync(Guid acessoId, CancellationToken ct = default)
    {
        var acesso = await _context.AcessosCliente
            .Include(a => a.Contato)
            .FirstOrDefaultAsync(a => a.Id == acessoId, ct)
            ?? throw new KeyNotFoundException("Acesso não encontrado.");

        return new ClientePerfilDto(acesso.Id, acesso.ContatoId, acesso.Contato.Nome, acesso.Email, acesso.Contato.Telefone);
    }

    public async Task<IEnumerable<MeuProcessoDto>> GetMeusProcessosAsync(
        Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        return await _context.Processos
            .Where(p => p.TenantId == tenantId &&
                        p.Partes.Any(pt => pt.ContatoId == contatoId) &&
                        p.Status != StatusProcesso.Arquivado)
            .Select(p => new
            {
                p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca, p.AreaDireito,
                p.Fase, p.Status, p.CriadoEm,
                TipoParte = p.Partes
                    .Where(pt => pt.ContatoId == contatoId)
                    .Select(pt => pt.TipoParte)
                    .FirstOrDefault(),
                TotalAndamentos = p.Andamentos.Count
            })
            .ToListAsync(ct)
            .ContinueWith(t => t.Result.Select(p => new MeuProcessoDto(
                p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca, p.AreaDireito,
                p.Fase, p.Status, p.TipoParte, p.TotalAndamentos, p.CriadoEm)));
    }

    public async Task<MeuProcessoDto?> GetProcessoAsync(
        Guid processoId, Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var p = await _context.Processos
            .Where(p => p.Id == processoId && p.TenantId == tenantId &&
                        p.Partes.Any(pt => pt.ContatoId == contatoId))
            .Select(p => new
            {
                p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca, p.AreaDireito,
                p.Fase, p.Status, p.CriadoEm,
                TipoParte = p.Partes
                    .Where(pt => pt.ContatoId == contatoId)
                    .Select(pt => pt.TipoParte)
                    .FirstOrDefault(),
                TotalAndamentos = p.Andamentos.Count
            })
            .FirstOrDefaultAsync(ct);

        if (p == null) return null;

        return new MeuProcessoDto(p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca,
            p.AreaDireito, p.Fase, p.Status, p.TipoParte, p.TotalAndamentos, p.CriadoEm);
    }

    public async Task<IEnumerable<MeuAndamentoDto>> GetAndamentosAsync(
        Guid processoId, Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var autorizado = await _context.Processos
            .AnyAsync(p => p.Id == processoId && p.TenantId == tenantId &&
                           p.Partes.Any(pt => pt.ContatoId == contatoId), ct);

        if (!autorizado) throw new UnauthorizedAccessException("Acesso negado ao processo.");

        return await _context.Andamentos
            .Where(a => a.ProcessoId == processoId)
            .OrderByDescending(a => a.Data)
            .Select(a => new MeuAndamentoDto(
                a.Id, a.Data, a.Tipo, a.Descricao, a.DescricaoTraduzidaIA, a.Fonte, a.CriadoEm))
            .ToListAsync(ct);
    }

    public async Task<AcessoPortalInfoDto> CriarAcessoAsync(
        Guid contatoId, CriarAcessoPortalDto dto, Guid tenantId, CancellationToken ct = default)
    {
        var contato = await _context.Contatos
            .FirstOrDefaultAsync(c => c.Id == contatoId && c.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contato não encontrado.");

        var existente = await _context.AcessosCliente
            .FirstOrDefaultAsync(a => a.ContatoId == contatoId && a.TenantId == tenantId, ct);

        if (existente != null)
        {
            existente.Email = dto.Email.Trim().ToLowerInvariant();
            existente.SenhaHash = _hasher.HashPassword(existente, dto.Senha);
            existente.Ativo = true;
            await _context.SaveChangesAsync(ct);
            return MapInfo(existente);
        }

        if (await _context.AcessosCliente.AnyAsync(
                a => a.TenantId == tenantId && a.Email == dto.Email.Trim().ToLowerInvariant(), ct))
            throw new InvalidOperationException("Este e-mail já está em uso no portal.");

        var acesso = new AcessoCliente
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContatoId = contatoId,
            Email = dto.Email.Trim().ToLowerInvariant(),
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        acesso.SenhaHash = _hasher.HashPassword(acesso, dto.Senha);

        _context.AcessosCliente.Add(acesso);
        await _context.SaveChangesAsync(ct);

        return MapInfo(acesso);
    }

    public async Task<AcessoPortalInfoDto?> GetAcessoAsync(Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var a = await _context.AcessosCliente
            .FirstOrDefaultAsync(x => x.ContatoId == contatoId && x.TenantId == tenantId, ct);
        return a == null ? null : MapInfo(a);
    }

    public async Task RevogarAcessoAsync(Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var acesso = await _context.AcessosCliente
            .FirstOrDefaultAsync(a => a.ContatoId == contatoId && a.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Acesso ao portal não encontrado.");

        _context.AcessosCliente.Remove(acesso);
        await _context.SaveChangesAsync(ct);
    }

    private (string token, DateTime expires) GerarJwt(AcessoCliente acesso)
    {
        var jwtSettings = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!));
        var expires = DateTime.UtcNow.AddHours(24);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, acesso.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, acesso.Email),
            new Claim("tenantId", acesso.TenantId.ToString()),
            new Claim("contatoId", acesso.ContatoId.ToString()),
            new Claim(ClaimTypes.Role, "Cliente"),
            new Claim("tipo", "portal"),
            new Claim("nome", acesso.Contato.Nome),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static AcessoPortalInfoDto MapInfo(AcessoCliente a) =>
        new(a.Id, a.ContatoId, a.Email, a.Ativo, a.CriadoEm, a.UltimoAcessoEm);
}
