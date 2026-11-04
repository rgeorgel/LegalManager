using LegalManager.Application.DTOs.PortalCliente;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
        new(ctx, CreateConfig(), _hasher, _emailMock.Object, NullLogger<PortalClienteService>.Instance);

    private async Task<(AppDbContext ctx, Tenant tenant, Contato contato, AcessoCliente acesso)> SeedAsync(
        string senha = "Senha@123", bool ativo = true)
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Escritório Teste",
            Plano = PlanoTipo.Free,
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
    public async Task LoginAsync_CincoSenhasErradas_BloqueiamOAcesso()
    {
        var (ctx, _, _, acesso) = await SeedAsync();
        var service = CreateService(ctx);

        for (var i = 0; i < 4; i++)
        {
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.LoginAsync(new LoginPortalDto("cliente@teste.com", "SenhaErrada")));
            Assert.Equal("E-mail ou senha inválidos.", ex.Message);
        }
        var quinta = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginPortalDto("cliente@teste.com", "SenhaErrada")));
        Assert.Equal(BloqueioLogin.Mensagem, quinta.Message);

        // Bloqueado: nem a senha certa entra.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginPortalDto("cliente@teste.com", "Senha@123")));

        // Passado o bloqueio, a senha certa volta a funcionar e zera a contagem.
        acesso.BloqueadoAte = DateTime.UtcNow.AddSeconds(-1);
        await ctx.SaveChangesAsync();
        await service.LoginAsync(new LoginPortalDto("cliente@teste.com", "Senha@123"));
        Assert.Equal(0, (await ctx.AcessosCliente.FindAsync(acesso.Id))!.TentativasLoginFalhas);
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

    // ── LoginAsync com o mesmo e-mail em dois escritórios ─

    /// <summary>
    /// Segundo escritório com acesso do mesmo e-mail (cliente@teste.com) e um processo em que o
    /// contato dele é parte. O primeiro acesso (de SeedAsync) foi usado por último.
    /// </summary>
    private async Task<(AcessoCliente outroAcesso, Processo processoDoOutro)> SeedOutroEscritorioAsync(
        AppDbContext ctx, AcessoCliente primeiro, string senha)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Outro Escritório", Plano = PlanoTipo.Free,
            Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow
        };
        var contato = new Contato
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Cliente no Outro", Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente, Email = "cliente@teste.com", Ativo = true, CriadoEm = DateTime.UtcNow
        };
        var acesso = new AcessoCliente
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ContatoId = contato.Id, Email = "cliente@teste.com",
            Ativo = true, CriadoEm = DateTime.UtcNow, Contato = contato, Tenant = tenant,
            UltimoAcessoEm = DateTime.UtcNow.AddDays(-2)
        };
        acesso.SenhaHash = _hasher.HashPassword(acesso, senha);
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0600095-47.2021.6.14.0098",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.Contatos.Add(contato);
        ctx.AcessosCliente.Add(acesso);
        ctx.Processos.Add(processo);
        ctx.ProcessoPartes.Add(new ProcessoParte
        {
            Id = Guid.NewGuid(), ProcessoId = processo.Id, ContatoId = contato.Id, TipoParte = TipoParteProcesso.Autor
        });
        primeiro.UltimoAcessoEm = DateTime.UtcNow.AddDays(-1);
        await ctx.SaveChangesAsync();
        return (acesso, processo);
    }

    [Fact]
    public async Task LoginAsync_DoisEscritorios_SemProcesso_EntraNoUsadoPorUltimo()
    {
        var (ctx, _, contato, acesso) = await SeedAsync();
        await SeedOutroEscritorioAsync(ctx, acesso, "Senha@123");

        var result = await CreateService(ctx).LoginAsync(new LoginPortalDto("cliente@teste.com", "Senha@123"));

        Assert.Equal(contato.Id, result.Perfil.ContatoId);
    }

    [Fact]
    public async Task LoginAsync_DoisEscritorios_ComProcesso_EntraNoAcessoQueEParteDele()
    {
        var (ctx, _, _, acesso) = await SeedAsync();
        var (outro, processo) = await SeedOutroEscritorioAsync(ctx, acesso, "Senha@123");

        var result = await CreateService(ctx).LoginAsync(
            new LoginPortalDto("cliente@teste.com", "Senha@123", processo.Id));

        Assert.Equal(outro.ContatoId, result.Perfil.ContatoId);
        Assert.Equal(outro.Id, result.Perfil.AcessoId);
        Assert.Equal("Outro Escritório", result.Perfil.NomeEscritorio);
    }

    [Fact]
    public async Task LoginAsync_DoisEscritorios_SenhaDeOutroAcesso_NaoEntraNoDoProcesso()
    {
        // A senha só vale para o acesso em que foi definida: com a senha do primeiro escritório,
        // o link de um processo do segundo não abre o acesso do segundo.
        var (ctx, _, contato, acesso) = await SeedAsync();
        var (outro, processo) = await SeedOutroEscritorioAsync(ctx, acesso, "OutraSenha@456");

        var result = await CreateService(ctx).LoginAsync(
            new LoginPortalDto("cliente@teste.com", "Senha@123", processo.Id));

        Assert.Equal(contato.Id, result.Perfil.ContatoId);
        Assert.Equal(0, (await ctx.AcessosCliente.FindAsync(outro.Id))!.TentativasLoginFalhas);
    }

    [Fact]
    public async Task LoginAsync_DoisEscritorios_SenhaErradaEmTodos_ContaFalhaEmCadaUm()
    {
        var (ctx, _, _, acesso) = await SeedAsync();
        var (outro, processo) = await SeedOutroEscritorioAsync(ctx, acesso, "OutraSenha@456");
        var service = CreateService(ctx);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginPortalDto("cliente@teste.com", "SenhaErrada", processo.Id)));

        Assert.Equal("E-mail ou senha inválidos.", ex.Message);
        Assert.Equal(1, (await ctx.AcessosCliente.FindAsync(acesso.Id))!.TentativasLoginFalhas);
        Assert.Equal(1, (await ctx.AcessosCliente.FindAsync(outro.Id))!.TentativasLoginFalhas);
    }

    [Fact]
    public async Task LoginAsync_DoisEscritorios_UmBloqueado_EntraNoOutro()
    {
        var (ctx, _, contato, acesso) = await SeedAsync();
        var (outro, processo) = await SeedOutroEscritorioAsync(ctx, acesso, "Senha@123");
        outro.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);
        await ctx.SaveChangesAsync();

        var result = await CreateService(ctx).LoginAsync(
            new LoginPortalDto("cliente@teste.com", "Senha@123", processo.Id));

        Assert.Equal(contato.Id, result.Perfil.ContatoId);
    }

    [Fact]
    public async Task LoginAsync_TodosBloqueados_AvisaBloqueio()
    {
        var (ctx, _, _, acesso) = await SeedAsync();
        acesso.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);
        await ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(ctx).LoginAsync(new LoginPortalDto("cliente@teste.com", "Senha@123")));

        Assert.Equal(BloqueioLogin.Mensagem, ex.Message);
    }

    // ── CriarAcessoAsync ──────────────────────────────────

    [Fact]
    public async Task CriarAcessoAsync_CriaAcesso_QuandoContatoSemAcesso()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
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
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
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

    // ── Esqueci minha senha ───────────────────────────────

    private static string TokenDoLink(string link) =>
        Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

    private async Task<string> SolicitarEObterTokenAsync(PortalClienteService service, string email)
    {
        string? link = null;
        _emailMock.Setup(e => e.EnviarResetSenhaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, l, _) => link = l)
            .Returns(Task.CompletedTask);
        await service.SolicitarRedefinicaoAsync(email);
        Assert.NotNull(link);
        Assert.StartsWith("http://localhost:6600/cliente/definir-senha.html?token=", link);
        return TokenDoLink(link!);
    }

    [Fact]
    public async Task Redefinicao_LinkDoEmailTrocaASenhaEDesbloqueia()
    {
        var (ctx, _, _, acesso) = await SeedAsync();
        acesso.TentativasLoginFalhas = 5;
        acesso.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        var token = await SolicitarEObterTokenAsync(service, "  Cliente@Teste.com ");
        // Só o hash fica no banco.
        Assert.NotEqual(token, acesso.TokenRedefinicao);

        await service.RedefinirSenhaAsync(new RedefinirSenhaPortalDto(token, "NovaSenha@456"));

        Assert.Null(acesso.TokenRedefinicao);
        Assert.Null(acesso.BloqueadoAte);
        var result = await service.LoginAsync(new LoginPortalDto("cliente@teste.com", "NovaSenha@456"));
        Assert.NotNull(result.AccessToken);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginPortalDto("cliente@teste.com", "Senha@123")));
    }

    [Fact]
    public async Task Redefinicao_LinkSoValeUmaVez()
    {
        var (ctx, _, _, _) = await SeedAsync();
        var service = CreateService(ctx);
        var token = await SolicitarEObterTokenAsync(service, "cliente@teste.com");

        await service.RedefinirSenhaAsync(new RedefinirSenhaPortalDto(token, "NovaSenha@456"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RedefinirSenhaAsync(new RedefinirSenhaPortalDto(token, "OutraSenha@789")));
    }

    [Fact]
    public async Task Redefinicao_LinkExpiradoOuInvalido_EhRecusado()
    {
        var (ctx, _, _, acesso) = await SeedAsync();
        var service = CreateService(ctx);
        var token = await SolicitarEObterTokenAsync(service, "cliente@teste.com");
        acesso.TokenRedefinicaoExpiraEm = DateTime.UtcNow.AddSeconds(-1);
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RedefinirSenhaAsync(new RedefinirSenhaPortalDto(token, "NovaSenha@456")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RedefinirSenhaAsync(new RedefinirSenhaPortalDto("token-inventado", "NovaSenha@456")));
    }

    [Fact]
    public async Task Redefinicao_EmailDesconhecidoOuInativo_NaoEnviaNada()
    {
        var (ctx, _, _, _) = await SeedAsync(ativo: false);
        var service = CreateService(ctx);

        await service.SolicitarRedefinicaoAsync("cliente@teste.com");
        await service.SolicitarRedefinicaoAsync("nao@existe.com");

        _emailMock.Verify(e => e.EnviarResetSenhaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Redefinicao_FalhaNoEnvioDoEmail_NaoRevelaQueOEmailExiste()
    {
        var (ctx, _, _, _) = await SeedAsync();
        _emailMock.Setup(e => e.EnviarResetSenhaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP fora do ar"));
        var service = CreateService(ctx);

        await service.SolicitarRedefinicaoAsync("cliente@teste.com"); // não lança
    }

    [Fact]
    public async Task Redefinicao_ClienteDeDoisEscritorios_SenhaNovaValeNosDois()
    {
        var (ctx, _, _, acesso1) = await SeedAsync();
        var tenant2 = new Tenant { Id = Guid.NewGuid(), Nome = "Outro Escritório", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        var contato2 = new Contato { Id = Guid.NewGuid(), TenantId = tenant2.Id, Nome = "Cliente Teste", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow };
        var acesso2 = new AcessoCliente { Id = Guid.NewGuid(), TenantId = tenant2.Id, ContatoId = contato2.Id, Email = "cliente@teste.com", Ativo = true, CriadoEm = DateTime.UtcNow };
        acesso2.SenhaHash = _hasher.HashPassword(acesso2, "SenhaDiferente@1");
        ctx.AddRange(tenant2, contato2, acesso2);
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        var token = await SolicitarEObterTokenAsync(service, "cliente@teste.com");
        await service.RedefinirSenhaAsync(new RedefinirSenhaPortalDto(token, "NovaSenha@456"));

        Assert.NotEqual(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(acesso1, acesso1.SenhaHash, "NovaSenha@456"));
        Assert.NotEqual(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(acesso2, acesso2.SenhaHash, "NovaSenha@456"));
        _emailMock.Verify(e => e.EnviarResetSenhaAsync("cliente@teste.com", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Documento enviado pelo cliente ────────────────────

    private static Usuario NovoUsuario(Guid tenantId, PerfilUsuario perfil, bool ativo = true) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Nome = perfil.ToString(), Perfil = perfil, Ativo = ativo,
        Email = $"{Guid.NewGuid():N}@x.com", UserName = $"{Guid.NewGuid():N}@x.com"
    };

    private static Processo NovoProcesso(Guid tenantId, Guid? responsavelId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "1234567-89.2024.8.26.0001",
        AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
        Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow, AdvogadoResponsavelId = responsavelId
    };

    [Fact]
    public async Task NotificarDocumentoEnviado_AvisaAdvogadoResponsavel()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var responsavel = NovoUsuario(tenant.Id, PerfilUsuario.Advogado);
        var admin = NovoUsuario(tenant.Id, PerfilUsuario.Admin);
        ctx.Users.AddRange(responsavel, admin);
        var processo = NovoProcesso(tenant.Id, responsavel.Id);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        await CreateService(ctx).NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "comprovante.pdf");

        var n = Assert.Single(ctx.Notificacoes);
        Assert.Equal(responsavel.Id, n.UsuarioId);
        Assert.Equal(TipoNotificacao.DocumentoCliente, n.Tipo);
        Assert.Contains(processo.NumeroCNJ, n.Titulo);
        Assert.Contains("Cliente Teste", n.Mensagem);
        Assert.Contains("comprovante.pdf", n.Mensagem);
        Assert.Equal($"/pages/processo-detalhe.html?id={processo.Id}&tab=documentos", n.Url);
    }

    [Fact]
    public async Task NotificarDocumentoEnviado_SemResponsavelAtivo_AvisaAdmins()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var responsavelInativo = NovoUsuario(tenant.Id, PerfilUsuario.Advogado, ativo: false);
        var admin1 = NovoUsuario(tenant.Id, PerfilUsuario.Admin);
        var admin2 = NovoUsuario(tenant.Id, PerfilUsuario.Admin);
        var colaborador = NovoUsuario(tenant.Id, PerfilUsuario.Colaborador);
        var adminOutroEscritorio = NovoUsuario(Guid.NewGuid(), PerfilUsuario.Admin);
        ctx.Users.AddRange(responsavelInativo, admin1, admin2, colaborador, adminOutroEscritorio);
        var processo = NovoProcesso(tenant.Id, responsavelInativo.Id);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        await CreateService(ctx).NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "doc.pdf");

        var destinatarios = ctx.Notificacoes.Select(n => n.UsuarioId).ToHashSet();
        Assert.Equal(new HashSet<Guid> { admin1.Id, admin2.Id }, destinatarios);
    }

    [Fact]
    public async Task NotificarDocumentoEnviado_ProcessoDeOutroEscritorio_NaoNotifica()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var admin = NovoUsuario(tenant.Id, PerfilUsuario.Admin);
        ctx.Users.Add(admin);
        var processo = NovoProcesso(Guid.NewGuid(), null);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        await CreateService(ctx).NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "doc.pdf");

        Assert.Empty(ctx.Notificacoes);
    }

    [Fact]
    public async Task NotificarDocumentoEnviado_VariosArquivosSeguidos_ViramUmAvisoSo()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var responsavel = NovoUsuario(tenant.Id, PerfilUsuario.Advogado);
        ctx.Users.Add(responsavel);
        var processo = NovoProcesso(tenant.Id, responsavel.Id);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        await service.NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "rg.pdf");
        await service.NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "cpf.pdf");
        await service.NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "comprovante.jpg");

        var n = Assert.Single(ctx.Notificacoes);
        Assert.Equal($"Documentos enviados pelo cliente — {processo.NumeroCNJ}", n.Titulo);
        Assert.Equal("Cliente Teste enviou 3 documentos pelo Portal do Cliente. Último: \"comprovante.jpg\".", n.Mensagem);
        Assert.False(n.Lida);
    }

    [Fact]
    public async Task NotificarDocumentoEnviado_AvisoJaLidoOuAntigo_CriaOutro()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var responsavel = NovoUsuario(tenant.Id, PerfilUsuario.Advogado);
        ctx.Users.Add(responsavel);
        var processo = NovoProcesso(tenant.Id, responsavel.Id);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        await service.NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "a.pdf");
        ctx.Notificacoes.Single().Lida = true;
        await ctx.SaveChangesAsync();
        await service.NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "b.pdf");
        var segunda = ctx.Notificacoes.Single(x => !x.Lida);
        segunda.CriadaEm = DateTime.UtcNow - PortalClienteService.JanelaAgrupamentoDocumentos - TimeSpan.FromMinutes(1);
        await ctx.SaveChangesAsync();
        await service.NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "c.pdf");

        Assert.Equal(3, ctx.Notificacoes.Count());
        Assert.All(ctx.Notificacoes, x => Assert.DoesNotContain("documentos pelo", x.Mensagem));
    }

    [Fact]
    public async Task NotificarDocumentoEnviado_OutroClienteNoMesmoProcesso_NaoSomaNoAvisoDoPrimeiro()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var responsavel = NovoUsuario(tenant.Id, PerfilUsuario.Advogado);
        ctx.Users.Add(responsavel);
        var processo = NovoProcesso(tenant.Id, responsavel.Id);
        ctx.Processos.Add(processo);
        var outro = new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Outra Parte", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, CriadoEm = DateTime.UtcNow };
        ctx.Contatos.Add(outro);
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        await service.NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, "a.pdf");
        await service.NotificarDocumentoEnviadoAsync(processo.Id, outro.Id, tenant.Id, "b.pdf");

        Assert.Equal(2, ctx.Notificacoes.Count());
        Assert.Contains(ctx.Notificacoes, x => x.Mensagem == "Outra Parte enviou \"b.pdf\" pelo Portal do Cliente.");
    }

    [Fact]
    public async Task NotificarDocumentoEnviado_NomeLongo_ETruncado()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var admin = NovoUsuario(tenant.Id, PerfilUsuario.Admin);
        ctx.Users.Add(admin);
        var processo = NovoProcesso(tenant.Id, null);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        await CreateService(ctx).NotificarDocumentoEnviadoAsync(processo.Id, contato.Id, tenant.Id, new string('a', 500));

        var n = Assert.Single(ctx.Notificacoes);
        Assert.Contains(new string('a', 200) + "…", n.Mensagem);
        Assert.DoesNotContain(new string('a', 201), n.Mensagem);
    }

    [Fact]
    public async Task NotificarDocumentoEnviado_ErroNoBanco_NaoLanca()
    {
        var (ctx, tenant, contato, _) = await SeedAsync();
        var service = CreateService(ctx);
        await ctx.DisposeAsync();

        await service.NotificarDocumentoEnviadoAsync(Guid.NewGuid(), contato.Id, tenant.Id, "doc.pdf");
    }

    // ── Perfil com tema do escritório ─────────────────────

    [Fact]
    public async Task GetPerfilAsync_RetornaTemaENomeDoEscritorio()
    {
        var (ctx, tenant, contato, acesso) = await SeedAsync();
        tenant.PrimaryColor = "#7c3aed";
        tenant.SidebarColor = "#ffffff";
        tenant.AccentColor = "#0ea5e9";
        tenant.LogoUrl = "http://localhost:6600/api/tenants/x/logo";
        await ctx.SaveChangesAsync();

        var perfil = await CreateService(ctx).GetPerfilAsync(acesso.Id);

        Assert.Equal(contato.Nome, perfil.Nome);
        Assert.Equal("Escritório Teste", perfil.NomeEscritorio);
        Assert.NotNull(perfil.Tema);
        Assert.Equal("#7c3aed", perfil.Tema!.PrimaryColor);
        Assert.Equal("#ffffff", perfil.Tema.SidebarColor);
        Assert.Equal("#0ea5e9", perfil.Tema.AccentColor);
        Assert.Equal("http://localhost:6600/api/tenants/x/logo", perfil.Tema.LogoUrl);
    }

    [Fact]
    public async Task LoginAsync_RetornaNomeDoEscritorio()
    {
        var (ctx, _, _, _) = await SeedAsync();

        var result = await CreateService(ctx).LoginAsync(new LoginPortalDto("cliente@teste.com", "Senha@123"));

        Assert.Equal("Escritório Teste", result.Perfil.NomeEscritorio);
        Assert.NotNull(result.Perfil.Tema);
    }
}
