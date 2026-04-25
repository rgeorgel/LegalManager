using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace LegalManager.UnitTests;

public class AuthServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ConviteTenantId = Guid.NewGuid();

    private IConfiguration CreateConfig()
    {
        var configMock = new Mock<IConfiguration>();
        var sectionMock = new Mock<IConfigurationSection>();
        sectionMock.Setup(s => s["Key"]).Returns("meu-secret-key-minimo-32-caracteres-p好吗!");
        sectionMock.Setup(s => s["Issuer"]).Returns("LegalManager");
        sectionMock.Setup(s => s["Audience"]).Returns("LegalManager");
        configMock.Setup(c => c.GetSection("Jwt")).Returns(sectionMock.Object);
        configMock.Setup(c => c["App:FrontendUrl"]).Returns("https://app.legalmanager.com.br");
        return configMock.Object;
    }

    private Mock<UserManager<Usuario>> CreateUserManagerMock(Usuario? user = null, IdentityResult? result = null)
    {
        var storeMock = new Mock<IUserStore<Usuario>>();
        var userManagerMock = new Mock<UserManager<Usuario>>(storeMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        userManagerMock.Setup(u => u.CreateAsync(It.IsAny<Usuario>(), It.IsAny<string>()))
            .ReturnsAsync(result ?? IdentityResult.Success);

        userManagerMock.Setup(u => u.AddToRoleAsync(It.IsAny<Usuario>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        if (user != null)
        {
            userManagerMock.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            userManagerMock.Setup(u => u.CheckPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(true);
            userManagerMock.Setup(u => u.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token-123");
        }

        return userManagerMock;
    }

    private Mock<IEmailService> CreateEmailServiceMock()
    {
        var mock = new Mock<IEmailService>();
        mock.Setup(e => e.EnviarBoasVindasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(e => e.EnviarResetSenhaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(e => e.EnviarConviteUsuarioAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task RegisterTenantAsync_DeveCriarTenantEUsuario_QuandoDadosValidos()
    {
        var ctx = CreateContext();
        var userManager = CreateUserManagerMock();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        var dto = new RegisterTenantDto("Escritório Novo", "12.345.678/0001-90", "Admin Nome", "admin@novo.com", "Senha123!", PlanoTipo.Free);

        var result = await service.RegisterTenantAsync(dto);

        Assert.NotNull(result);
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
        Assert.Equal("Admin Nome", result.Usuario.Nome);
        Assert.Equal("admin@novo.com", result.Usuario.Email);
        Assert.Equal(PerfilUsuario.Admin.ToString(), result.Usuario.Perfil);
    }

    [Fact]
    public async Task RegisterTenantAsync_DeveLancarExcecao_QuandoUserManagerFalhar()
    {
        var ctx = CreateContext();
        var userManager = CreateUserManagerMock(result: IdentityResult.Failed(new IdentityError { Description = "Email já existe" }));
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        var dto = new RegisterTenantDto("Escritório", null, "Admin", "duplicado@teste.com", "Senha123!", PlanoTipo.Free);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterTenantAsync(dto));
    }

    [Fact]
    public async Task RegisterTenantAsync_DeveEnviarEmailDeBoasVindas()
    {
        var ctx = CreateContext();
        var userManager = CreateUserManagerMock();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        var dto = new RegisterTenantDto("Escritório Teste", null, "Admin", "admin@teste.com", "Senha123!", PlanoTipo.Free);

        await service.RegisterTenantAsync(dto);

        emailService.Verify(e => e.EnviarBoasVindasAsync("admin@teste.com", "Escritório Teste", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_DeveRetornarAuthResponse_QuandoCredenciaisValidas()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "login@teste.com", UserName = "login@teste.com", Nome = "User Teste", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        var result = await service.LoginAsync(new LoginDto("login@teste.com", "Senha123"));

        Assert.NotNull(result);
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_DeveLancarUnauthorizedAccessException_QuandoEmailNaoExiste()
    {
        var ctx = CreateContext();
        var userManager = CreateUserManagerMock(null);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginDto("naoexiste@teste.com", "Senha123")));
    }

    [Fact]
    public async Task LoginAsync_DeveLancarUnauthorizedAccessException_QuandoUsuarioDesativado()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "inativo@teste.com", UserName = "inativo@teste.com", Nome = "Inativo", Perfil = PerfilUsuario.Admin, Ativo = false, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginDto("inativo@teste.com", "Senha123")));
    }

    [Fact]
    public async Task LoginAsync_DeveLancarUnauthorizedAccessException_QuandoTrialExpirado()
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow.AddDays(-15),
            TrialExpiraEm = DateTime.UtcNow.AddDays(-5)
        };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "trial@teste.com", UserName = "trial@teste.com", Nome = "Trial", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginDto("trial@teste.com", "Senha123")));
    }

    [Fact]
    public async Task LoginAsync_DeveDowngradeParaFree_QuandoSubscriptionExpirou()
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow.AddMonths(-2),
            PlanoExpiraEm = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "expirado@teste.com", UserName = "expirado@teste.com", Nome = "Expirado", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        var result = await service.LoginAsync(new LoginDto("expirado@teste.com", "Senha123"));

        Assert.NotNull(result);
        Assert.Equal(PlanoTipo.Free.ToString(), result.Usuario.Plano);
    }

    [Fact]
    public async Task RefreshTokenAsync_DeveRetornarNovoToken_QuandoRefreshTokenValido()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "refresh@teste.com", UserName = "refresh@teste.com", Nome = "Refresh", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(), UsuarioId = UserId, Token = "token-valido-123",
            ExpiresAt = DateTime.UtcNow.AddDays(7), CriadoEm = DateTime.UtcNow, Revogado = false
        };
        ctx.RefreshTokens.Add(refreshToken);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        var result = await service.RefreshTokenAsync("token-valido-123");

        Assert.NotNull(result);
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_DeveLancarUnauthorizedAccessException_QuandoTokenInvalido()
    {
        var ctx = CreateContext();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(CreateUserManagerMock().Object, config, emailService.Object, ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RefreshTokenAsync("token-invalido"));
    }

    [Fact]
    public async Task RefreshTokenAsync_DeveRevogarTokenAntigo()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "revogar@teste.com", UserName = "revogar@teste.com", Nome = "Revogar", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);

        var oldToken = new RefreshToken
        {
            Id = Guid.NewGuid(), UsuarioId = UserId, Token = "token-antigo",
            ExpiresAt = DateTime.UtcNow.AddDays(7), CriadoEm = DateTime.UtcNow, Revogado = false
        };
        ctx.RefreshTokens.Add(oldToken);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await service.RefreshTokenAsync("token-antigo");

        var updatedToken = await ctx.RefreshTokens.FindAsync(oldToken.Id);
        Assert.True(updatedToken!.Revogado);
    }

    [Fact]
    public async Task LogoutAsync_DeveRevogarRefreshToken()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "logout@teste.com", UserName = "logout@teste.com", Nome = "Logout", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(), UsuarioId = UserId, Token = "token-logout",
            ExpiresAt = DateTime.UtcNow.AddDays(7), CriadoEm = DateTime.UtcNow, Revogado = false
        };
        ctx.RefreshTokens.Add(token);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await service.LogoutAsync("token-logout");

        var updatedToken = await ctx.RefreshTokens.FindAsync(token.Id);
        Assert.True(updatedToken!.Revogado);
    }

    [Fact]
    public async Task LogoutAsync_DeveNaoFalhar_QuandoTokenNaoExiste()
    {
        var ctx = CreateContext();
        var userManager = CreateUserManagerMock();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await service.LogoutAsync("token-nao-existe");
    }

    [Fact]
    public async Task ForgotPasswordAsync_DeveEnviarEmail_QuandoUsuarioExiste()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "forgot@teste.com", UserName = "forgot@teste.com", Nome = "Forgot", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await service.ForgotPasswordAsync(new ForgotPasswordDto("forgot@teste.com"));

        emailService.Verify(e => e.EnviarResetSenhaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_DeveSilenciar_QuandoEmailNaoExiste()
    {
        var ctx = CreateContext();
        var userManager = CreateUserManagerMock(null);
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await service.ForgotPasswordAsync(new ForgotPasswordDto("naoexiste@teste.com"));

        emailService.Verify(e => e.EnviarResetSenhaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_DeveRedefinirSenha_QuandoTokenValido()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "reset@teste.com", UserName = "reset@teste.com", Nome = "Reset", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        userManager.Setup(u => u.ResetPasswordAsync(user, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await service.ResetPasswordAsync(new ResetPasswordDto("token-valido", "reset@teste.com", "NovaSenha123!"));

        userManager.Verify(u => u.ResetPasswordAsync(user, "token-valido", "NovaSenha123!"), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_DeveLancarExcecao_QuandoFalhar()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "fail@teste.com", UserName = "fail@teste.com", Nome = "Fail", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock(user);
        userManager.Setup(u => u.ResetPasswordAsync(user, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Token inválido" }));

        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordDto("token-invalido", "fail@teste.com", "NovaSenha123!")));
    }

    [Fact]
    public async Task ConvidarUsuarioAsync_DeveCriarConvite_QuandoDentroDoLimite()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = ConviteTenantId, Nome = "Teste", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        for (int i = 0; i < 4; i++)
        {
            ctx.Users.Add(new Usuario
            {
                Id = Guid.NewGuid(), TenantId = ConviteTenantId,
                Email = $"user{i}@teste.com", UserName = $"user{i}@teste.com",
                Nome = $"User {i}", Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await service.ConvidarUsuarioAsync(new ConvidarUsuarioDto("novo@teste.com", "Advogado"), ConviteTenantId);

        var convite = await ctx.ConvitesUsuario.FirstOrDefaultAsync(c => c.Email == "novo@teste.com");
        Assert.NotNull(convite);
        Assert.Equal(PerfilUsuario.Advogado, convite.Perfil);
    }

    [Fact]
    public async Task ConvidarUsuarioAsync_DeveLancarExcecao_QuandoLimiteDeUsuariosAtingido()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = ConviteTenantId, Nome = "Teste", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        for (int i = 0; i < 5; i++)
        {
            ctx.Users.Add(new Usuario
            {
                Id = Guid.NewGuid(), TenantId = ConviteTenantId,
                Email = $"user{i}@teste.com", UserName = $"user{i}@teste.com",
                Nome = $"User {i}", Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConvidarUsuarioAsync(new ConvidarUsuarioDto("novo@teste.com", "Advogado"), ConviteTenantId));
    }

    [Fact]
    public async Task ConvidarUsuarioAsync_DeveLancarExcecao_QuandoPerfilInvalido()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = ConviteTenantId, Nome = "Teste", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConvidarUsuarioAsync(new ConvidarUsuarioDto("novo@teste.com", "PerfilInvalido"), ConviteTenantId));
    }

    [Fact]
    public async Task AceitarConviteAsync_DeveCriarUsuario_QuandoConviteValido()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = ConviteTenantId, Nome = "Teste", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var convite = new ConviteUsuario
        {
            Id = Guid.NewGuid(), TenantId = ConviteTenantId, Email = "convidado@teste.com",
            Perfil = PerfilUsuario.Advogado, Token = "token-valido-abc123",
            ExpiresAt = DateTime.UtcNow.AddDays(5), Usado = false, CriadoEm = DateTime.UtcNow
        };
        ctx.ConvitesUsuario.Add(convite);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        var result = await service.AceitarConviteAsync(new AceitarConviteDto("token-valido-abc123", "Nome Convidado", "Senha123!"));

        Assert.NotNull(result);
        Assert.Equal("Nome Convidado", result.Usuario.Nome);
        Assert.Equal("convidado@teste.com", result.Usuario.Email);
    }

    [Fact]
    public async Task AceitarConviteAsync_DeveLancarExcecao_QuandoConviteInvalido()
    {
        var ctx = CreateContext();
        var userManager = CreateUserManagerMock();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AceitarConviteAsync(new AceitarConviteDto("token-invalido", "Nome", "Senha123!")));
    }

    [Fact]
    public async Task AceitarConviteAsync_DeveMarcarConviteComoUsado()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = ConviteTenantId, Nome = "Teste", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);

        var convite = new ConviteUsuario
        {
            Id = Guid.NewGuid(), TenantId = ConviteTenantId, Email = "usado@teste.com",
            Perfil = PerfilUsuario.Colaborador, Token = "token-usado-xyz",
            ExpiresAt = DateTime.UtcNow.AddDays(5), Usado = false, CriadoEm = DateTime.UtcNow
        };
        ctx.ConvitesUsuario.Add(convite);
        await ctx.SaveChangesAsync();

        var userManager = CreateUserManagerMock();
        var config = CreateConfig();
        var emailService = CreateEmailServiceMock();
        var service = new AuthService(userManager.Object, config, emailService.Object, ctx);

        await service.AceitarConviteAsync(new AceitarConviteDto("token-usado-xyz", "Nome", "Senha123!"));

        var updatedConvite = await ctx.ConvitesUsuario.FindAsync(convite.Id);
        Assert.True(updatedConvite!.Usado);
    }
}

public class JwtGenerationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void GerarJwt_DeveContainClaimsCorretos()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("meu-secret-key-minimo-32-caracteres-p好吗!"));
        var tenant = new Tenant { Id = TenantId, Nome = "Teste", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        var user = new Usuario { Id = UserId, TenantId = TenantId, Email = "jwt@teste.com", UserName = "jwt@teste.com", Nome = "Jwt User", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };

        var handler = new JwtSecurityTokenHandler();
        var token = new JwtSecurityToken(
            issuer: "LegalManager",
            audience: "LegalManager",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!),
                new Claim("tenantId", tenant.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Perfil.ToString()),
                new Claim("nome", user.Nome),
                new Claim("plano", tenant.Plano.ToString())
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        var tokenString = handler.WriteToken(token);
        Assert.NotNull(tokenString);
        Assert.NotEmpty(tokenString);

        var parsedToken = handler.ReadJwtToken(tokenString);
        Assert.Equal(user.Id.ToString(), parsedToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(tenant.Id.ToString(), parsedToken.Claims.First(c => c.Type == "tenantId").Value);
    }

    [Fact]
    public void GenerateSecureToken_DeveGerarTokensDiferentes()
    {
        var method = typeof(AuthService).GetMethod("GenerateSecureToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var func = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), method);

        var token1 = func();
        var token2 = func();

        Assert.NotEqual(token1, token2);
        Assert.True(token1.Length >= 64);
    }
}