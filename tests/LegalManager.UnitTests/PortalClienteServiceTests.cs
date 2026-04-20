using LegalManager.Application.DTOs.PortalCliente;
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

public class PortalClienteServiceTests
{
    private readonly IPasswordHasher<AcessoCliente> _hasher = new PasswordHasher<AcessoCliente>();
    private readonly Mock<IEmailService> _emailMock = new();

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "super-secret-key-for-testing-1234567890ab",
                ["Jwt:Issuer"] = "LegalManager",
                ["Jwt:Audience"] = "LegalManager",
                ["App:FrontendUrl"] = "http://localhost:6600"
            })
            .Build();

    private PortalClienteService CreateService(AppDbContext ctx) =>
        new(ctx, CreateConfig(), _hasher, _emailMock.Object);

    private async Task<(AppDbContext ctx, Tenant tenant, Contato contato, AcessoCliente acesso)> SeedAsync(
        string senha = "Senha@123", bool ativo = true)
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Escritório Teste",
            Plano = PlanoTipo.Smart,
            Status = StatusTenant.Trial,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);

        var contato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Cliente Teste",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            Email = "cliente@teste.com",
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Contatos.Add(contato);

        var acesso = new AcessoCliente
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ContatoId = contato.Id,
            Email = "cliente@teste.com",
            Ativo = ativo,
            CriadoEm = DateTime.UtcNow,
            Contato = contato,
            Tenant = tenant
        };
        acesso.SenhaHash = _hasher.HashPassword(acesso, senha);
        ctx.AcessosCliente.Add(acesso);

        await ctx.SaveChangesAsync();
        return (ctx, tenant, contato, acesso);
    }

    // ── LoginAsync ────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_RetornaToken_QuandoCredenciaisCorretas()
    {
        var (ctx, _, contato, _) = await SeedAsync();
        var service = CreateService(ctx);

        var result = await service.LoginAsync(new LoginPortalDto("cliente@teste.com", "Senha@123"));

        Assert.NotNull(result.AccessToken);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
        Assert.Equal("Cliente Teste", result.Perfil.Nome);
        Assert.Equal(contato.Id, result.Perfil.ContatoId);
    }

    [Fact]
    public async Task LoginAsync_AtualizaUltimoAcesso_QuandoLoginComSucesso()
    {
        var (ctx, _, _, acesso) = await SeedAsync();
        var service = CreateService(ctx);

        await service.LoginAsync(new LoginPortalDto("cliente@teste.com", "Senha@123"));

        var updated = await ctx.AcessosCliente.FindAsync(acesso.Id);
        Assert.NotNull(updated!.UltimoAcessoEm);
    }

    [Fact]
    public async Task LoginAsync_LancaUnauthorized_QuandoSenhaErrada()
    {
        var (ctx, _, _, _) = await SeedAsync();
        var service = CreateService(ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginPortalDto("cliente@teste.com", "SenhaErrada")));
    }

    [Fact]
    public async Task LoginAsync_LancaUnauthorized_QuandoEmailNaoExiste()
    {
        var (ctx, _, _, _) = await SeedAsync();
        var service = CreateService(ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginPortalDto("nao@existe.com", "Senha@123")));
    }

    [Fact]
    public async Task LoginAsync_LancaUnauthorized_QuandoContaInativa()
    {
        var (ctx, _, _, _) = await SeedAsync(ativo: false);
        var service = CreateService(ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginPortalDto("cliente@teste.com", "Senha@123")));
    }

    // ── CriarAcessoAsync ──────────────────────────────────

    [Fact]
    public async Task CriarAcessoAsync_CriaAcesso_QuandoContatoSemAcesso()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Smart, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Novo Cliente", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);

        var result = await service.CriarAcessoAsync(contato.Id,
            new CriarAcessoPortalDto("novo@cliente.com", "Senha@123"), tenant.Id);

        Assert.Equal("novo@cliente.com", result.Email);
        Assert.True(result.Ativo);
        Assert.Equal(1, await ctx.AcessosCliente.CountAsync());
    }

    [Fact]
    public async Task CriarAcessoAsync_EnviaEmail_QuandoAcessoCriado()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Smart, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Novo Cliente", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);
        await service.CriarAcessoAsync(contato.Id,
            new CriarAcessoPortalDto("novo@cliente.com", "Senha@123"), tenant.Id);

        _emailMock.Verify(e => e.EnviarAcessoPortalAsync(
            "novo@cliente.com", "Novo Cliente", "Escritório", "Senha@123",
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CriarAcessoAsync_AtualizaAcesso_QuandoJaExiste()
    {
        var (ctx, tenant, contato, acesso) = await SeedAsync();
        var service = CreateService(ctx);

        var result = await service.CriarAcessoAsync(contato.Id,
            new CriarAcessoPortalDto("novo@email.com", "NovaSenha@123"), tenant.Id);

        Assert.Equal("novo@email.com", result.Email);
        Assert.Equal(1, await ctx.AcessosCliente.CountAsync());
    }

    [Fact]
    public async Task CriarAcessoAsync_LancaExcecao_QuandoEmailEmUso()
    {
        var (ctx, tenant, _, _) = await SeedAsync();
        var outroContato = new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Outro", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Contatos.Add(outroContato);
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CriarAcessoAsync(outroContato.Id,
                new CriarAcessoPortalDto("cliente@teste.com", "Senha@123"), tenant.Id));
    }

    [Fact]
    public async Task CriarAcessoAsync_LancaExcecao_QuandoContatoNaoEncontrado()
    {
        var (ctx, tenant, _, _) = await SeedAsync();
        var service = CreateService(ctx);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CriarAcessoAsync(Guid.NewGuid(),
                new CriarAcessoPortalDto("x@x.com", "Senha@123"), tenant.Id));
    }

    // ── RevogarAcessoAsync ────────────────────────────────

    [Fact]
    public async Task RevogarAcessoAsync_RemoveAcesso_QuandoExiste()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var service = CreateService(ctx);

        await service.RevogarAcessoAsync(contato.Id, tenant.Id);

        Assert.Equal(0, await ctx.AcessosCliente.CountAsync());
    }

    [Fact]
    public async Task RevogarAcessoAsync_LancaExcecao_QuandoNaoExiste()
    {
        var (ctx, tenant, _, _) = await SeedAsync();
        var service = CreateService(ctx);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RevogarAcessoAsync(Guid.NewGuid(), tenant.Id));
    }

    // ── GetAcessoAsync ────────────────────────────────────

    [Fact]
    public async Task GetAcessoAsync_RetornaInfo_QuandoExiste()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var service = CreateService(ctx);

        var result = await service.GetAcessoAsync(contato.Id, tenant.Id);

        Assert.NotNull(result);
        Assert.Equal("cliente@teste.com", result!.Email);
    }

    [Fact]
    public async Task GetAcessoAsync_RetornaNull_QuandoNaoExiste()
    {
        var (ctx, tenant, _, _) = await SeedAsync();
        var service = CreateService(ctx);

        var result = await service.GetAcessoAsync(Guid.NewGuid(), tenant.Id);

        Assert.Null(result);
    }

    // ── GetMeusProcessosAsync ─────────────────────────────

    [Fact]
    public async Task GetMeusProcessosAsync_RetornaApenasProcessosDoContato()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();

        var outroContato = new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Outro", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Contatos.Add(outroContato);

        var processoMeu = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "1234567-89.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        };
        var processoAlheio = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "9999999-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Trabalhista, Fase = FaseProcessual.Execucao,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Processos.AddRange(processoMeu, processoAlheio);

        ctx.ProcessoPartes.AddRange(
            new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = processoMeu.Id, ContatoId = contato.Id, TipoParte = TipoParteProcesso.Autor },
            new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = processoAlheio.Id, ContatoId = outroContato.Id, TipoParte = TipoParteProcesso.Autor }
        );
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);
        var result = (await service.GetMeusProcessosAsync(contato.Id, tenant.Id)).ToList();

        Assert.Single(result);
        Assert.Equal(processoMeu.NumeroCNJ, result[0].NumeroCNJ);
    }

    [Fact]
    public async Task GetMeusProcessosAsync_NaoRetornaProcessosArquivados()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();

        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "1111111-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Arquivado, CriadoEm = DateTime.UtcNow
        };
        ctx.Processos.Add(processo);
        ctx.ProcessoPartes.Add(new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = processo.Id, ContatoId = contato.Id, TipoParte = TipoParteProcesso.Autor });
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);
        var result = await service.GetMeusProcessosAsync(contato.Id, tenant.Id);

        Assert.Empty(result);
    }
}
