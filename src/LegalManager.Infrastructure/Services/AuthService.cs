using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
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
    private readonly ICreditoService _creditoService;
    private readonly AppDbContext _context;

    public AuthService(
        UserManager<Usuario> userManager,
        IConfiguration config,
        IEmailService emailService,
        ICreditoService creditoService,
        AppDbContext context)
    {
        _userManager = userManager;
        _config = config;
        _emailService = emailService;
        _creditoService = creditoService;
        _context = context;
    }

    public async Task<AuthResponseDto> RegisterTenantAsync(RegisterTenantDto dto, CancellationToken ct = default)
    {
        var beneficio = string.IsNullOrWhiteSpace(dto.Voucher)
            ? null
            : await AplicarVoucherAsync(dto.Voucher, ct);

        var planoFinal = beneficio?.Plano ?? dto.Plano;

        if (planoFinal is PlanoTipo.Enterprise)
            throw new InvalidOperationException("Este plano não está disponível para cadastro direto.");

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = dto.NomeEscritorio,
            Cnpj = dto.Cnpj,
            Plano = planoFinal,
            Status = beneficio != null ? StatusTenant.Ativo
                : planoFinal == PlanoTipo.Free ? StatusTenant.Ativo
                : StatusTenant.Trial,
            CriadoEm = DateTime.UtcNow,
            TrialExpiraEm = beneficio != null ? null
                : planoFinal == PlanoTipo.Free ? null
                : DateTime.UtcNow.AddDays(10),
            PlanoExpiraEm = beneficio?.PlanoExpiraEm,
            VoucherUtilizado = beneficio != null ? dto.Voucher!.Trim().ToLowerInvariant() : null
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync(ct);

        await _creditoService.InicializarCreditosPadraoAsync(tenant.Id, dto.Plano, ct);

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

        var expiraEm = tenant.TrialExpiraEm ?? tenant.PlanoExpiraEm;
        await _emailService.EnviarBoasVindasAsync(dto.Email, dto.NomeAdmin, dto.NomeEscritorio, planoFinal.ToString(), expiraEm);

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

        if (tenant.Status == StatusTenant.Trial && tenant.TrialExpiraEm < DateTime.UtcNow)
        {
            tenant.Plano = PlanoTipo.Free;
            tenant.Status = StatusTenant.Ativo;
            tenant.TrialExpiraEm = null;
            tenant.TrialConcedidoPorId = null;
            tenant.TrialConcedidoEm = null;
            tenant.TrialConcedidoDias = null;
            tenant.TrialConcedidoMotivo = null;
            await _context.SaveChangesAsync(ct);
        }

        // Downgrade to Free if Pro subscription expired (cancelled and past billing period)
        if (tenant.PlanoExpiraEm.HasValue && tenant.PlanoExpiraEm.Value < DateTime.UtcNow)
        {
            tenant.Plano = PlanoTipo.Free;
            tenant.Status = StatusTenant.Ativo;
            tenant.PlanoExpiraEm = null;
            tenant.AbacatePayBillingId = null;
            tenant.PeriodoBilling = null;
            await _context.SaveChangesAsync(ct);
        }

        usuario.UltimoAcessoEm = DateTime.UtcNow;
        await _userManager.UpdateAsync(usuario);

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

        token.Usuario.UltimoAcessoEm = DateTime.UtcNow;

        var tenant = await _context.Tenants.FindAsync([token.Usuario.TenantId], ct)!;

        string? impersonadoPorNome = null;
        if (token.ImpersonadoPorId.HasValue)
        {
            impersonadoPorNome = await _context.Users
                .Where(u => u.Id == token.ImpersonadoPorId.Value)
                .Select(u => u.Nome)
                .FirstOrDefaultAsync(ct);
        }

        return await GerarAuthResponseAsync(token.Usuario, tenant!, ct, token.ImpersonadoPorId, impersonadoPorNome);
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
        var limiteUsuarios = PlanoRestricoes.MaxUsuarios(tenant.Plano);
        if (usuariosAtivos >= limiteUsuarios)
            throw new InvalidOperationException($"Limite de usuários do plano atingido (máximo {limiteUsuarios}).");

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

        var link = $"{_config["App:FrontendUrl"]}/aceitar-convite.html?token={convite.Token}";
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
            CriadoEm = DateTime.UtcNow,
            UltimoAcessoEm = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(usuario, dto.Senha);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(usuario, convite.Perfil.ToString());

        convite.Usado = true;
        await _context.SaveChangesAsync(ct);

        return await GerarAuthResponseAsync(usuario, convite.Tenant, ct);
    }

    public async Task<AuthResponseDto> ImpersonarAsync(
        Guid superAdminId, string superAdminNome, Guid targetUserId, CancellationToken ct = default)
    {
        var targetUser = await _context.Users.FindAsync([targetUserId], ct)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        if (!targetUser.Ativo)
            throw new InvalidOperationException("Usuário desativado.");

        if (targetUser.Perfil == PerfilUsuario.SuperAdmin)
            throw new InvalidOperationException("Não é possível impersonar um SuperAdmin.");

        if (targetUser.TenantId == TenantConstants.SystemTenantId)
            throw new InvalidOperationException("Tenant inválido para impersonação.");

        var tenant = await _context.Tenants.FindAsync([targetUser.TenantId], ct)
            ?? throw new InvalidOperationException("Tenant não encontrado.");

        return await GerarAuthResponseAsync(targetUser, tenant, ct, superAdminId, superAdminNome);
    }

    private async Task<AuthResponseDto> GerarAuthResponseAsync(
        Usuario usuario, Tenant tenant, CancellationToken ct,
        Guid? impersonadoPorId = null, string? impersonadoPorNome = null)
    {
        var accessTokenTtl = impersonadoPorId.HasValue ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(1);
        var refreshTokenTtl = impersonadoPorId.HasValue ? TimeSpan.FromMinutes(30) : TimeSpan.FromDays(7);

        var accessToken = GerarJwt(usuario, tenant, impersonadoPorId, impersonadoPorNome, accessTokenTtl);
        var refreshToken = await CriarRefreshTokenAsync(usuario.Id, ct, impersonadoPorId, refreshTokenTtl);

        return new AuthResponseDto(
            accessToken,
            refreshToken.Token,
            refreshToken.ExpiresAt,
            new UsuarioInfoDto(usuario.Id, usuario.Nome, usuario.Email!, usuario.Perfil.ToString(), tenant.Id, tenant.Nome, tenant.Plano.ToString(), usuario.UltimoAcessoEm)
        );
    }

    private string GerarJwt(
        Usuario usuario, Tenant tenant,
        Guid? impersonadoPorId = null, string? impersonadoPorNome = null, TimeSpan? ttl = null)
    {
        var jwtSettings = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email!),
            new("tenantId", tenant.Id.ToString()),
            new(ClaimTypes.Role, usuario.Perfil.ToString()),
            new("nome", usuario.Nome),
            new("plano", tenant.Plano.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (impersonadoPorId.HasValue)
        {
            claims.Add(new Claim("impersonadoPorId", impersonadoPorId.Value.ToString()));
            if (!string.IsNullOrEmpty(impersonadoPorNome))
                claims.Add(new Claim("impersonadoPorNome", impersonadoPorNome));
        }

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(ttl ?? TimeSpan.FromHours(1)),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<RefreshToken> CriarRefreshTokenAsync(
        Guid usuarioId, CancellationToken ct, Guid? impersonadoPorId = null, TimeSpan? ttl = null)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Token = GenerateSecureToken(),
            ExpiresAt = DateTime.UtcNow.Add(ttl ?? TimeSpan.FromDays(7)),
            CriadoEm = DateTime.UtcNow,
            ImpersonadoPorId = impersonadoPorId
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

    private async Task<VoucherBeneficio> AplicarVoucherAsync(string voucher, CancellationToken ct)
    {
        var code = voucher.Trim().ToLowerInvariant();
        var section = _config.GetSection($"Vouchers:{code}");

        if (!section.Exists())
            throw new InvalidOperationException("Voucher inválido.");

        var maxUsos = section.GetValue<int>("MaxUsos");
        var usosAtuais = await _context.Tenants.CountAsync(t => t.VoucherUtilizado == code, ct);
        if (usosAtuais >= maxUsos)
            throw new InvalidOperationException("Voucher esgotado.");

        var planoTipo = (PlanoTipo)section.GetValue<int>("PlanoTipo");
        var meses = section.GetValue<int>("MesesGratuitos");

        return new VoucherBeneficio(planoTipo, DateTime.UtcNow.AddMonths(meses));
    }

    private record VoucherBeneficio(PlanoTipo Plano, DateTime PlanoExpiraEm);
}
