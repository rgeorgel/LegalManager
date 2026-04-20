using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LegalManager.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<Usuario> _userManager;
    private readonly IConfiguration _config;
    private readonly IEmailService _emailService;
    private readonly AppDbContext _context;

    public AuthService(
        UserManager<Usuario> userManager,
        IConfiguration config,
        IEmailService emailService,
        AppDbContext context)
    {
        _userManager = userManager;
        _config = config;
        _emailService = emailService;
        _context = context;
    }

    public async Task<AuthResponseDto> RegisterTenantAsync(RegisterTenantDto dto, CancellationToken ct = default)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = dto.NomeEscritorio,
            Cnpj = dto.Cnpj,
            Plano = PlanoTipo.Smart,
            Status = StatusTenant.Trial,
            CriadoEm = DateTime.UtcNow,
            TrialExpiraEm = DateTime.UtcNow.AddDays(10)
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync(ct);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = dto.NomeAdmin,
            Email = dto.Email,
            UserName = dto.Email,
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(usuario, dto.Senha);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(usuario, PerfilUsuario.Admin.ToString());

        _ = _emailService.EnviarBoasVindasAsync(dto.Email, dto.NomeEscritorio, ct);

        return await GerarAuthResponseAsync(usuario, tenant, ct);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var usuario = await _userManager.FindByEmailAsync(dto.Email)
            ?? throw new UnauthorizedAccessException("Credenciais inválidas.");

        if (!usuario.Ativo)
            throw new UnauthorizedAccessException("Usuário desativado.");

        if (!await _userManager.CheckPasswordAsync(usuario, dto.Senha))
            throw new UnauthorizedAccessException("Credenciais inválidas.");

        var tenant = await _context.Tenants.FindAsync([usuario.TenantId], ct)
            ?? throw new InvalidOperationException("Tenant não encontrado.");

        if (tenant.Status == StatusTenant.Cancelado)
            throw new UnauthorizedAccessException("Assinatura cancelada.");

        if (tenant.Status == StatusTenant.Trial && tenant.TrialExpiraEm < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Período de trial expirado.");

        return await GerarAuthResponseAsync(usuario, tenant, ct);
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var token = await _context.RefreshTokens
            .Include(r => r.Usuario)
            .FirstOrDefaultAsync(r => r.Token == refreshToken && !r.Revogado && r.ExpiresAt > DateTime.UtcNow, ct)
            ?? throw new UnauthorizedAccessException("Refresh token inválido ou expirado.");

        token.Revogado = true;
        _context.RefreshTokens.Update(token);

        var tenant = await _context.Tenants.FindAsync([token.Usuario.TenantId], ct)!;
        return await GerarAuthResponseAsync(token.Usuario, tenant!, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var token = await _context.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken, ct);

        if (token != null)
        {
            token.Revogado = true;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task ForgotPasswordAsync(ForgotPasswordDto dto, CancellationToken ct = default)
    {
        var usuario = await _userManager.FindByEmailAsync(dto.Email);
        if (usuario == null) return; // silently ignore unknown emails

        var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);
        var link = $"{_config["App:FrontendUrl"]}/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(dto.Email)}";

        await _emailService.EnviarResetSenhaAsync(dto.Email, link, ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default)
    {
        var usuario = await _userManager.FindByEmailAsync(dto.Email)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        var result = await _userManager.ResetPasswordAsync(usuario, dto.Token, dto.NovaSenha);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task ConvidarUsuarioAsync(ConvidarUsuarioDto dto, Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _context.Tenants.FindAsync([tenantId], ct)
            ?? throw new InvalidOperationException("Tenant não encontrado.");

        var usuariosAtivos = await _context.Users.CountAsync(u => u.TenantId == tenantId && u.Ativo, ct);
        if (usuariosAtivos >= 5)
            throw new InvalidOperationException("Limite de usuários do plano atingido (máximo 5).");

        if (!Enum.TryParse<PerfilUsuario>(dto.Perfil, true, out var perfil))
            throw new InvalidOperationException("Perfil inválido.");

        var convite = new ConviteUsuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = dto.Email,
            Perfil = perfil,
            Token = GenerateSecureToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CriadoEm = DateTime.UtcNow
        };

        _context.ConvitesUsuario.Add(convite);
        await _context.SaveChangesAsync(ct);

        var link = $"{_config["App:FrontendUrl"]}/aceitar-convite?token={convite.Token}";
        await _emailService.EnviarConviteUsuarioAsync(dto.Email, tenant.Nome, link, ct);
    }

    public async Task<AuthResponseDto> AceitarConviteAsync(AceitarConviteDto dto, CancellationToken ct = default)
    {
        var convite = await _context.ConvitesUsuario
            .Include(c => c.Tenant)
            .FirstOrDefaultAsync(c => c.Token == dto.Token && !c.Usado && c.ExpiresAt > DateTime.UtcNow, ct)
            ?? throw new InvalidOperationException("Convite inválido ou expirado.");

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = convite.TenantId,
            Nome = dto.Nome,
            Email = convite.Email,
            UserName = convite.Email,
            Perfil = convite.Perfil,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(usuario, dto.Senha);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(usuario, convite.Perfil.ToString());

        convite.Usado = true;
        await _context.SaveChangesAsync(ct);

        return await GerarAuthResponseAsync(usuario, convite.Tenant, ct);
    }

    private async Task<AuthResponseDto> GerarAuthResponseAsync(Usuario usuario, Tenant tenant, CancellationToken ct)
    {
        var accessToken = GerarJwt(usuario, tenant);
        var refreshToken = await CriarRefreshTokenAsync(usuario.Id, ct);

        return new AuthResponseDto(
            accessToken,
            refreshToken.Token,
            refreshToken.ExpiresAt,
            new UsuarioInfoDto(usuario.Id, usuario.Nome, usuario.Email!, usuario.Perfil.ToString(), tenant.Id, tenant.Nome)
        );
    }

    private string GerarJwt(Usuario usuario, Tenant tenant)
    {
        var jwtSettings = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email!),
            new Claim("tenantId", tenant.Id.ToString()),
            new Claim(ClaimTypes.Role, usuario.Perfil.ToString()),
            new Claim("nome", usuario.Nome),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<RefreshToken> CriarRefreshTokenAsync(Guid usuarioId, CancellationToken ct)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Token = GenerateSecureToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CriadoEm = DateTime.UtcNow
        };

        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync(ct);
        return token;
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
