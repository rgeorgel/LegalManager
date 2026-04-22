using LegalManager.Application.DTOs.Atividades;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class TarefaServiceTests
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

    private async Task<(AppDbContext ctx, Tenant tenant, Usuario usuario)> SeedAsync()
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
        await ctx.SaveChangesAsync();
        return (ctx, tenant, usuario);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateTarefaWithPendingStatus()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Revisar contrato", null, null, null, PrioridadeTarefa.Alta, null, null, null);
        var result = await svc.CreateAsync(dto);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Revisar contrato", result.Titulo);
        Assert.Equal(StatusTarefa.Pendente, result.Status);
        Assert.Equal(PrioridadeTarefa.Alta, result.Prioridade);
        Assert.False(result.Atrasada);
    }

    [Fact]
    public async Task CreateAsync_WithTags_ShouldPersistDistinctTags()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa com tags", null, null, null, PrioridadeTarefa.Media, null, null,
            new List<string> { "urgente", "urgente", "revisão" });
        var result = await svc.CreateAsync(dto);

        Assert.Equal(2, result.Tags.Count);
        Assert.Contains("urgente", result.Tags);
        Assert.Contains("revisão", result.Tags);
    }

    [Fact]
    public async Task GetByIdAsync_WithWrongTenant_ShouldReturnNull()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa secreta", null, null, null, PrioridadeTarefa.Baixa, null, null, null);
        var created = await svc.CreateAsync(dto);

        var svcOtherTenant = new TarefaService(ctx, CreateTenantContext(Guid.NewGuid(), usuario.Id));
        var result = await svcOtherTenant.GetByIdAsync(created.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConcluirAsync_ShouldSetStatusAndConcluidaEm()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa a concluir", null, null, null, PrioridadeTarefa.Media, null, null, null);
        var created = await svc.CreateAsync(dto);

        await svc.ConcluirAsync(created.Id);

        var tarefa = await ctx.Tarefas.FindAsync(created.Id);
        Assert.Equal(StatusTarefa.Concluida, tarefa!.Status);
        Assert.NotNull(tarefa.ConcluidaEm);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveTarefa()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa para deletar", null, null, null, PrioridadeTarefa.Baixa, null, null, null);
        var created = await svc.CreateAsync(dto);

        await svc.DeleteAsync(created.Id);

        Assert.Null(await ctx.Tarefas.FindAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_WithWrongTenant_ShouldThrow()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa", null, null, null, PrioridadeTarefa.Media, null, null, null);
        var created = await svc.CreateAsync(dto);

        var svcOther = new TarefaService(ctx, CreateTenantContext(Guid.NewGuid(), usuario.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svcOther.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task GetAllAsync_ShouldFilterByStatus()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        await svc.CreateAsync(new CreateTarefaDto("T1", null, null, null, PrioridadeTarefa.Baixa, null, null, null));
        var t2 = await svc.CreateAsync(new CreateTarefaDto("T2", null, null, null, PrioridadeTarefa.Alta, null, null, null));
        await svc.ConcluirAsync(t2.Id);

        var filtro = new TarefaFiltroDto(null, StatusTarefa.Concluida, null, null, null, null, null);
        var result = await svc.GetAllAsync(filtro);

        Assert.Equal(1, result.Total);
        Assert.Equal("T2", result.Items.First().Titulo);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateFieldsAndTags()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var created = await svc.CreateAsync(new CreateTarefaDto("Original", null, null, null, PrioridadeTarefa.Baixa, null, null,
            new List<string> { "tag1" }));

        var updateDto = new UpdateTarefaDto("Atualizada", "Descrição nova", null, null,
            PrioridadeTarefa.Alta, StatusTarefa.EmAndamento, null, null, new List<string> { "tag2", "tag3" });

        var updated = await svc.UpdateAsync(created.Id, updateDto);

        Assert.Equal("Atualizada", updated.Titulo);
        Assert.Equal(PrioridadeTarefa.Alta, updated.Prioridade);
        Assert.Equal(StatusTarefa.EmAndamento, updated.Status);
        Assert.Equal(2, updated.Tags.Count);
        Assert.DoesNotContain("tag1", updated.Tags);
    }
}
