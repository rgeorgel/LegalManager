using System.IdentityModel.Tokens.Jwt;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// "Lembrar de mim" no login: refresh token de 12h a partir do login, que não renova na rotação.
/// </summary>
public class LembrarDeMimTests
{
    private static readonly TimeSpan Folga = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _ctx = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Usuario _usuario;
    private readonly AuthService _service;

    public LembrarDeMimTests()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        _usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "lembrar@teste.com", UserName = "lembrar@teste.com",
            Nome = "Lembrar", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        _ctx.Tenants.Add(tenant);
        _ctx.Users.Add(_usuario);
        _ctx.SaveChanges();

        var userManager = new Mock<UserManager<Usuario>>(new Mock<IUserStore<Usuario>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(_usuario);
        userManager.Setup(u => u.CheckPasswordAsync(_usuario, It.IsAny<string>())).ReturnsAsync(true);
        userManager.Setup(u => u.UpdateAsync(It.IsAny<Usuario>())).ReturnsAsync(IdentityResult.Success);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "chave-de-teste-com-pelo-menos-32-caracteres!!",
            ["Jwt:Issuer"] = "Causify",
            ["Jwt:Audience"] = "CausifyUsers",
        }).Build();

        _service = new AuthService(userManager.Object, config, new Mock<IEmailService>().Object,
            new Mock<ICreditoService>().Object, _ctx, new Mock<IGoogleTokenValidator>().Object);
    }

    private static DateTime ExpiracaoDoJwt(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt).ValidTo;

    private Task<RefreshToken> TokenSalvoAsync(string token) =>
        _ctx.RefreshTokens.AsNoTracking().SingleAsync(r => r.Token == token);

    [Fact]
    public async Task Login_ComLembrar_RefreshTokenVale12HorasEMarcado()
    {
        var resp = await _service.LoginAsync(new LoginDto("lembrar@teste.com", "Senha123", Lembrar: true));

        var salvo = await TokenSalvoAsync(resp.RefreshToken);
        Assert.True(salvo.Lembrar);
        Assert.InRange(salvo.ExpiresAt, DateTime.UtcNow.AddHours(12) - Folga, DateTime.UtcNow.AddHours(12) + Folga);
        Assert.Equal(salvo.ExpiresAt, resp.ExpiresAt);
        Assert.InRange(ExpiracaoDoJwt(resp.AccessToken), DateTime.UtcNow.AddHours(1) - Folga, DateTime.UtcNow.AddHours(1) + Folga);
    }

    [Fact]
    public async Task Login_SemLembrar_MantemRefreshDe7Dias()
    {
        var resp = await _service.LoginAsync(new LoginDto("lembrar@teste.com", "Senha123"));

        var salvo = await TokenSalvoAsync(resp.RefreshToken);
        Assert.False(salvo.Lembrar);
        Assert.InRange(salvo.ExpiresAt, DateTime.UtcNow.AddDays(7) - Folga, DateTime.UtcNow.AddDays(7) + Folga);
    }

    [Fact]
    public async Task Refresh_DeSessaoLembrada_HerdaOVencimentoDoLogin()
    {
        var expiraEm = DateTime.UtcNow.AddHours(5);
        _ctx.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UsuarioId = _usuario.Id, Token = "lembrado", Lembrar = true,
            ExpiresAt = expiraEm, CriadoEm = DateTime.UtcNow.AddHours(-7)
        });
        await _ctx.SaveChangesAsync();

        var resp = await _service.RefreshTokenAsync("lembrado");

        var novo = await TokenSalvoAsync(resp.RefreshToken);
        Assert.True(novo.Lembrar);
        Assert.Equal(expiraEm, novo.ExpiresAt);
        Assert.True((await TokenSalvoAsync("lembrado")).Revogado);
    }

    [Fact]
    public async Task Refresh_PertoDoFimDaSessaoLembrada_AccessTokenNaoPassaDoFim()
    {
        var expiraEm = DateTime.UtcNow.AddMinutes(20);
        _ctx.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UsuarioId = _usuario.Id, Token = "quase-vencido", Lembrar = true,
            ExpiresAt = expiraEm, CriadoEm = DateTime.UtcNow.AddHours(-12).AddMinutes(20)
        });
        await _ctx.SaveChangesAsync();

        var resp = await _service.RefreshTokenAsync("quase-vencido");

        // JWT guarda o exp em segundos inteiros.
        Assert.True(ExpiracaoDoJwt(resp.AccessToken) <= expiraEm.AddSeconds(1));
    }

    [Fact]
    public async Task Refresh_DeSessaoComum_ContinuaRenovando7Dias()
    {
        _ctx.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UsuarioId = _usuario.Id, Token = "comum",
            ExpiresAt = DateTime.UtcNow.AddDays(1), CriadoEm = DateTime.UtcNow.AddDays(-6)
        });
        await _ctx.SaveChangesAsync();

        var resp = await _service.RefreshTokenAsync("comum");

        var novo = await TokenSalvoAsync(resp.RefreshToken);
        Assert.False(novo.Lembrar);
        Assert.InRange(novo.ExpiresAt, DateTime.UtcNow.AddDays(7) - Folga, DateTime.UtcNow.AddDays(7) + Folga);
    }

    [Fact]
    public async Task Refresh_DeSessaoLembradaVencida_PedeLoginDeNovo()
    {
        _ctx.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UsuarioId = _usuario.Id, Token = "vencido", Lembrar = true,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), CriadoEm = DateTime.UtcNow.AddHours(-12)
        });
        await _ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.RefreshTokenAsync("vencido"));
    }
}
