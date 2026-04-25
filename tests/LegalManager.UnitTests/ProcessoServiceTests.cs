using LegalManager.Application.DTOs.Processos;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class ProcessoServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private ITenantContext CreateTenantContext(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Tenant tenant, Usuario usuario, Contato contato)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Escritório Teste",
            Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Advogado Teste",
            Email = "adv@teste.com", UserName = "adv@teste.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);

        var contato = new Contato
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Cliente Teste",
            Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente,
            Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();

        return (ctx, tenant, usuario, contato);
    }

    [Fact]
    public async Task CreateAsync_DeveCriarProcesso_ComDadosValidos()
    {
        var (ctx, tenant, usuario, contato) = await SeedAsync();
        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateProcessoDto(
            "0000001-00.2024.8.26.0001", "TJSP", "1ª Vara Cível", "São Paulo",
            AreaDireito.Civil, "Ação de Cobrança", FaseProcessual.Conhecimento,
            10000m, usuario.Id, "Observação teste", false,
            new List<ProcessoParteDto> { new(contato.Id, TipoParteProcesso.Autor) });

        var result = await service.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.Equal("0000001-00.2024.8.26.0001", result.NumeroCNJ);
        Assert.Equal(StatusProcesso.Ativo, result.Status);
        Assert.Single(result.Partes);
        Assert.Equal(TipoParteProcesso.Autor, result.Partes[0].TipoParte);
    }

    [Fact]
    public async Task CreateAsync_DeveLancarExcecao_QuandoCNJDuplicado()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateProcessoDto("1111111-11.2024.8.26.0001", null, null, null,
            AreaDireito.Trabalhista, null, FaseProcessual.Conhecimento, null, null);

        await service.CreateAsync(dto);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(dto));
    }

    [Fact]
    public async Task GetAllAsync_DeveRetornarApenasDadosDoTenant()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();

        var outroTenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Outro", Plano = PlanoTipo.Free,
            Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(outroTenant);

        ctx.Processos.AddRange(
            new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0000001-00.2024.8.26.0001",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow },
            new Processo { Id = Guid.NewGuid(), TenantId = outroTenant.Id, NumeroCNJ = "9999999-99.2024.8.26.0001",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var result = await service.GetAllAsync(new ProcessoFiltroDto(null, null, null, null, null));

        Assert.Equal(1, result.Total);
        Assert.Equal("0000001-00.2024.8.26.0001", result.Items.First().NumeroCNJ);
    }

    [Fact]
    public async Task EncerrarAsync_DeveSetarStatusEData()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0000002-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        await service.EncerrarAsync(processo.Id, new EncerrarProcessoDto("Sentença", "Ganho"));

        var updated = await ctx.Processos.FindAsync(processo.Id);
        Assert.Equal(StatusProcesso.Encerrado, updated!.Status);
        Assert.Equal("Sentença", updated.Decisao);
        Assert.Equal("Ganho", updated.Resultado);
        Assert.NotNull(updated.EncerradoEm);
    }

    [Fact]
    public async Task AddAndamentoAsync_DeveRegistrarAndamento()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0000003-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var dto = new CreateAndamentoDto(DateTime.UtcNow, TipoAndamento.Despacho, "Despacho de distribuição");
        var result = await service.AddAndamentoAsync(processo.Id, dto);

        Assert.NotNull(result);
        Assert.Equal(FonteAndamento.Manual, result.Fonte);
        Assert.Equal("Despacho de distribuição", result.Descricao);
    }

    [Fact]
    public async Task DeleteAndamentoAsync_NaoDeveRemoverAndamentoAutomatico()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0000004-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Processos.Add(processo);
        var andamento = new Andamento
        {
            Id = Guid.NewGuid(), ProcessoId = processo.Id, TenantId = tenant.Id,
            Data = DateTime.UtcNow, Tipo = TipoAndamento.Despacho,
            Descricao = "Andamento automático", Fonte = FonteAndamento.Automatico,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Andamentos.Add(andamento);
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.DeleteAndamentoAsync(processo.Id, andamento.Id));
    }

    [Fact]
    public async Task GetAllAsync_DeveFilterPorStatus()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        ctx.Processos.AddRange(
            new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0001",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow },
            new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0002",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Encerrado, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var result = await service.GetAllAsync(new ProcessoFiltroDto(null, StatusProcesso.Ativo, null, null, null));

        Assert.Equal(1, result.Total);
        Assert.Equal(StatusProcesso.Ativo, result.Items.First().Status);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorAreaDireito()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        ctx.Processos.AddRange(
            new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0001",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow },
            new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0002",
                AreaDireito = AreaDireito.Trabalhista, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var result = await service.GetAllAsync(new ProcessoFiltroDto(null, null, AreaDireito.Trabalhista, null, null));

        Assert.Equal(1, result.Total);
        Assert.Equal(AreaDireito.Trabalhista, result.Items.First().AreaDireito);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorAdvogadoResponsavel()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        var outroAdvogado = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Outro Advogado",
            Email = "outro@adv.com", UserName = "outro@adv.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(outroAdvogado);
        await ctx.SaveChangesAsync();

        ctx.Processos.AddRange(
            new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0001",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, AdvogadoResponsavelId = usuario.Id, CriadoEm = DateTime.UtcNow },
            new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0002",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, AdvogadoResponsavelId = outroAdvogado.Id, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var result = await service.GetAllAsync(new ProcessoFiltroDto(null, null, null, usuario.Id, null));

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorContatoId()
    {
        var (ctx, tenant, usuario, contato) = await SeedAsync();
        var outroContato = new Contato
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Outro Cliente",
            Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Contatos.Add(outroContato);

        var processo1 = new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow };
        var processo2 = new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0002",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Processos.AddRange(processo1, processo2);

        ctx.ProcessoPartes.AddRange(
            new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = processo1.Id, ContatoId = contato.Id, TipoParte = TipoParteProcesso.Autor },
            new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = processo2.Id, ContatoId = outroContato.Id, TipoParte = TipoParteProcesso.Reu }
        );
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var result = await service.GetAllAsync(new ProcessoFiltroDto(null, null, null, null, contato.Id));

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetAllAsync_DeveSuportarCombinacaoDeFiltros()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        ctx.Processos.AddRange(
            new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0001",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow },
            new Processo { Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = "0002",
                AreaDireito = AreaDireito.Trabalhista, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Encerrado, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var result = await service.GetAllAsync(new ProcessoFiltroDto(null, StatusProcesso.Encerrado, AreaDireito.Trabalhista, null, null));

        Assert.Equal(1, result.Total);
        Assert.Equal(AreaDireito.Trabalhista, result.Items.First().AreaDireito);
    }

    [Fact]
    public async Task GetAllAsync_DevePaginarCorretamente()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        for (int i = 0; i < 25; i++)
        {
            ctx.Processos.Add(new Processo
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = $"000{i:D2}",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var page1 = await service.GetAllAsync(new ProcessoFiltroDto(null, null, null, null, null, 1, 10));
        var page3 = await service.GetAllAsync(new ProcessoFiltroDto(null, null, null, null, null, 3, 10));

        Assert.Equal(10, page1.Items.Count());
        Assert.Equal(5, page3.Items.Count());
        Assert.Equal(25, page1.Total);
    }

    [Fact]
    public async Task CreateAsync_DeveAtribuirTenantIdCorretamente()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateProcessoDto("0000001-00.2024.8.26.0001", null, null, null,
            AreaDireito.Civil, null, FaseProcessual.Conhecimento, null, null);

        var result = await service.CreateAsync(dto);

        var saved = await ctx.Processos.FindAsync(result.Id);
        Assert.NotNull(saved);
        Assert.Equal(tenant.Id, saved.TenantId);
    }

    [Fact]
    public async Task CreateAsync_DeveLancarExcecao_QuandoNumeroCNJDuplicadoNoMesmoTenant()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto1 = new CreateProcessoDto("0000001-00.2024.8.26.0001", null, null, null,
            AreaDireito.Civil, null, FaseProcessual.Conhecimento, null, null);
        await service.CreateAsync(dto1);

        var dto2 = new CreateProcessoDto("0000001-00.2024.8.26.0001", null, null, null,
            AreaDireito.Trabalhista, null, FaseProcessual.Conhecimento, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(dto2));
    }

    [Fact]
    public async Task UpdateAsync_DeveLancarKeyNotFoundException_QuandoProcessoNaoExiste()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new UpdateProcessoDto("0000001-00.2024.8.26.0001", null, null, null,
            AreaDireito.Civil, null, FaseProcessual.Conhecimento, StatusProcesso.Ativo, 10000m, null, null, null, null, null, null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task DeleteAsync_DeveLancarKeyNotFoundException_QuandoProcessoNaoExiste()
    {
        var (ctx, tenant, usuario, _) = await SeedAsync();
        var service = new ProcessoService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
    }
}
