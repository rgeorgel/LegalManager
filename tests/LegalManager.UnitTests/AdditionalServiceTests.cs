using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.DTOs.Modelos;
using LegalManager.Application.DTOs.Notificacoes;
using LegalManager.Application.DTOs.Prazos;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.API.Controllers;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

// ─── Helpers ─────────────────────────────────────────────────────────────────

file static class SvcTestHelpers
{
    public static AppDbContext CreateCtx() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public static Mock<ITenantContext> TenantMock(Guid tenantId, Guid userId, PlanoTipo plano = PlanoTipo.Pro)
    {
        var m = new Mock<ITenantContext>();
        m.Setup(t => t.TenantId).Returns(tenantId);
        m.Setup(t => t.UserId).Returns(userId);
        m.Setup(t => t.Plano).Returns(plano);
        return m;
    }
}

// ─── ProcessoService ──────────────────────────────────────────────────────────

public class ProcessoServiceAdditionalTests
{
    private static (ProcessoService Svc, AppDbContext Ctx, Guid TenantId, Guid UserId) Build(PlanoTipo plano = PlanoTipo.Pro)
    {
        var ctx = SvcTestHelpers.CreateCtx();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = plano, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario { Id = userId, TenantId = tenantId, Nome = "Adv", Email = "a@a.com", UserName = "a@a.com" });
        ctx.SaveChanges();
        var svc = new ProcessoService(ctx, SvcTestHelpers.TenantMock(tenantId, userId, plano).Object, new Mock<IEscavadorService>().Object);
        return (svc, ctx, tenantId, userId);
    }

    private static CreateProcessoDto MakeCreateDto(string cnj = "0000001-11.2024.8.26.0001", bool monitorado = false) =>
        new(cnj, "TJSP", "1ª Vara Cível", "São Paulo", AreaDireito.Civil, "Ação de Cobrança",
            FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, null, null, monitorado);

    [Fact]
    public async Task GetByIdAsync_RetornaProcesso()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        var result = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(result);
        Assert.Equal(created.Id, result!.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NaoExiste_RetornaNull()
    {
        var (svc, _, _, _) = Build();
        var result = await svc.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ComFiltroBusca_FiltraCorreto()
    {
        var (svc, _, _, _) = Build();
        await svc.CreateAsync(MakeCreateDto("0000001-11.2024.8.26.0001"));
        var found = await svc.GetAllAsync(new ProcessoFiltroDto("0000001", null, null, null, null));
        Assert.Equal(1, found.Total);
        var notFound = await svc.GetAllAsync(new ProcessoFiltroDto("XYZXYZ", null, null, null, null));
        Assert.Equal(0, notFound.Total);
    }

    [Fact]
    public async Task GetAllAsync_ComFiltroAreaDireito_FiltraCorreto()
    {
        var (svc, _, _, _) = Build();
        await svc.CreateAsync(MakeCreateDto());
        var found = await svc.GetAllAsync(new ProcessoFiltroDto(null, null, AreaDireito.Civil, null, null));
        Assert.Equal(1, found.Total);
        var notFound = await svc.GetAllAsync(new ProcessoFiltroDto(null, null, AreaDireito.Trabalhista, null, null));
        Assert.Equal(0, notFound.Total);
    }

    [Fact]
    public async Task UpdateAsync_AtualizaProcesso()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        var dto = new UpdateProcessoDto("0000001-11.2024.8.26.0001", "TJRJ", null, "Rio",
            AreaDireito.Trabalhista, null, FaseProcessual.Recursal, StatusProcesso.Ativo,
            null, null, null, null, null, null, null);
        var result = await svc.UpdateAsync(created.Id, dto);
        Assert.Equal("TJRJ", result.Tribunal);
        Assert.Equal(AreaDireito.Trabalhista, result.AreaDireito);
    }

    [Fact]
    public async Task UpdateAsync_EncerrarProcesso_DefineDateEncerramento()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        var dto = new UpdateProcessoDto("0000001-11.2024.8.26.0001", null, null, null, AreaDireito.Civil,
            null, FaseProcessual.Conhecimento, StatusProcesso.Encerrado,
            null, null, null, "Acordo", "Favorável", null, null);
        var result = await svc.UpdateAsync(created.Id, dto);
        Assert.Equal(StatusProcesso.Encerrado, result.Status);
        Assert.NotNull(result.EncerradoEm);
    }

    [Fact]
    public async Task UpdateAsync_CNJDuplicado_Throws()
    {
        var (svc, _, _, _) = Build();
        await svc.CreateAsync(MakeCreateDto("0000001-11.2024.8.26.0001"));
        var c2 = await svc.CreateAsync(MakeCreateDto("0000002-11.2024.8.26.0001"));
        var dto = new UpdateProcessoDto("0000001-11.2024.8.26.0001", null, null, null, AreaDireito.Civil,
            null, FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, null, null, null, null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync(c2.Id, dto));
    }

    [Fact]
    public async Task DeleteAsync_ArquivaProcesso()
    {
        var (svc, ctx, _, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        await svc.DeleteAsync(created.Id);
        var processo = await ctx.Processos.FindAsync(created.Id);
        Assert.Equal(StatusProcesso.Arquivado, processo!.Status);
    }

    [Fact]
    public async Task GetAndamentosAsync_RetornaAndamentos()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        await svc.AddAndamentoAsync(created.Id, new CreateAndamentoDto(DateTime.UtcNow, TipoAndamento.Decisao, "D1"));
        await svc.AddAndamentoAsync(created.Id, new CreateAndamentoDto(DateTime.UtcNow, TipoAndamento.Despacho, "D2"));
        var andamentos = (await svc.GetAndamentosAsync(created.Id)).ToList();
        Assert.Equal(2, andamentos.Count);
    }

    [Fact]
    public async Task GetAndamentosAsync_ProcessoNaoExiste_Throws()
    {
        var (svc, _, _, _) = Build();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.GetAndamentosAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAndamentoAsync_RemoveAndamento()
    {
        var (svc, ctx, _, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        var andamento = await svc.AddAndamentoAsync(created.Id, new CreateAndamentoDto(DateTime.UtcNow, TipoAndamento.Despacho, "D"));
        await svc.DeleteAndamentoAsync(created.Id, andamento.Id);
        Assert.Equal(0, ctx.Andamentos.Count(a => a.ProcessoId == created.Id));
    }

    [Fact]
    public async Task AdicionarParteAsync_AdicionaParte()
    {
        var (svc, ctx, tenantId, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "João", Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();
        await svc.AdicionarParteAsync(created.Id, contato.Id, "Autor");
        Assert.Equal(1, ctx.ProcessoPartes.Count(p => p.ProcessoId == created.Id));
    }

    [Fact]
    public async Task AdicionarParteAsync_JaExiste_Throws()
    {
        var (svc, ctx, tenantId, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "João", Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();
        await svc.AdicionarParteAsync(created.Id, contato.Id, "Autor");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AdicionarParteAsync(created.Id, contato.Id, "Reu"));
    }

    [Fact]
    public async Task RemoverParteAsync_RemoveParte()
    {
        var (svc, ctx, tenantId, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "João", Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();
        await svc.AdicionarParteAsync(created.Id, contato.Id, "Autor");
        await svc.RemoverParteAsync(created.Id, contato.Id);
        Assert.Equal(0, ctx.ProcessoPartes.Count(p => p.ProcessoId == created.Id));
    }

    [Fact]
    public async Task RemoverParteAsync_NaoExiste_Throws()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(MakeCreateDto());
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.RemoverParteAsync(created.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_LimiteMonitorados_Throws()
    {
        var (svc, ctx, tenantId, _) = Build(PlanoTipo.Free);
        for (var i = 0; i < 40; i++)
        {
            ctx.Processos.Add(new Processo
            {
                Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = $"{i:D7}-11.2024.8.26.0001",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, Monitorado = true, CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(MakeCreateDto("9999999-11.2024.8.26.0001", monitorado: true)));
    }
}

// ─── ContatoService ───────────────────────────────────────────────────────────

public class ContatoServiceAdditionalTests
{
    private static (ContatoService Svc, AppDbContext Ctx, Guid TenantId, Guid UserId) Build()
    {
        var ctx = SvcTestHelpers.CreateCtx();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Pro, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario { Id = userId, TenantId = tenantId, Nome = "Adv", Email = "a@a.com", UserName = "a@a.com" });
        ctx.SaveChanges();
        var svc = new ContatoService(ctx, SvcTestHelpers.TenantMock(tenantId, userId).Object);
        return (svc, ctx, tenantId, userId);
    }

    private static CreateContatoDto MakeDto(string nome = "João Silva") =>
        new(TipoPessoa.PF, TipoContato.Cliente, nome, null, null, null, null, null, null, null, null, null, null, true, ["vip"]);

    [Fact]
    public async Task GetByIdAsync_RetornaContato()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(MakeDto());
        var result = await svc.GetByIdAsync(created.Id);
        Assert.NotNull(result);
        Assert.Equal("João Silva", result!.Nome);
    }

    [Fact]
    public async Task GetByIdAsync_NaoExiste_RetornaNull()
    {
        var (svc, _, _, _) = Build();
        Assert.Null(await svc.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_AtualizaContato()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(MakeDto());
        var dto = new UpdateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Maria Souza",
            null, null, "maria@test.com", null, null, null, null, null, null, null, true, null);
        var result = await svc.UpdateAsync(created.Id, dto);
        Assert.Equal("Maria Souza", result.Nome);
        Assert.Equal("maria@test.com", result.Email);
    }

    [Fact]
    public async Task UpdateAsync_NaoEncontrado_Throws()
    {
        var (svc, _, _, _) = Build();
        var dto = new UpdateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "X",
            null, null, null, null, null, null, null, null, null, null, true, null);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.UpdateAsync(Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task AddAtendimentoAsync_AdicionaAtendimento()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(MakeDto());
        var dto = new CreateAtendimentoDto("Reunião inicial", DateTime.UtcNow);
        var result = await svc.AddAtendimentoAsync(created.Id, dto);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Reunião inicial", result.Descricao);
    }

    [Fact]
    public async Task AddAtendimentoAsync_ContatoNaoExiste_Throws()
    {
        var (svc, _, _, _) = Build();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.AddAtendimentoAsync(Guid.NewGuid(), new CreateAtendimentoDto("X", DateTime.UtcNow)));
    }

    [Fact]
    public async Task GetAtendimentosAsync_RetornaAtendimentos()
    {
        var (svc, ctx, tenantId, userId) = Build();
        var created = await svc.CreateAsync(MakeDto());
        ctx.Atendimentos.AddRange(
            new Atendimento { Id = Guid.NewGuid(), TenantId = tenantId, ContatoId = created.Id, UsuarioId = userId, Descricao = "A1", Data = DateTime.UtcNow, CriadoEm = DateTime.UtcNow },
            new Atendimento { Id = Guid.NewGuid(), TenantId = tenantId, ContatoId = created.Id, UsuarioId = userId, Descricao = "A2", Data = DateTime.UtcNow, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();
        var result = (await svc.GetAtendimentosAsync(created.Id)).ToList();
        Assert.Equal(2, result.Count);
    }
}

// ─── ModeloDocumentoService ───────────────────────────────────────────────────

public class ModeloDocumentoServiceTests
{
    private static ModeloDocumentoService Build(
        out AppDbContext ctx, out Guid tenantId, out Guid userId,
        Mock<IIAService>? ia = null, Mock<ICreditoService>? credito = null)
    {
        ctx = SvcTestHelpers.CreateCtx();
        tenantId = Guid.NewGuid();
        userId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Pro, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario { Id = userId, TenantId = tenantId, Nome = "U", Email = "u@u.com", UserName = "u@u.com" });
        ctx.SaveChanges();
        ia ??= new Mock<IIAService>();
        credito ??= new Mock<ICreditoService>();
        return new ModeloDocumentoService(ctx, SvcTestHelpers.TenantMock(tenantId, userId).Object, ia.Object, credito.Object);
    }

    [Fact]
    public async Task GetAllAsync_RetornaModelosDoTenant()
    {
        var svc = Build(out var ctx, out var tenantId, out var userId);
        // Use service to create first modelo (ensures correct FKs)
        await svc.CreateAsync(new CreateModeloDocumentoDto { Nome = "M1", Conteudo = "C1", Variaveis = [] });
        // Add a second modelo for a different tenant directly (with valid FK)
        ctx.ModelosDocumento.Add(new ModeloDocumento
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), CriadoPorId = userId,
            Nome = "M2", Conteudo = "C2", Variaveis = "", CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var result = (await svc.GetAllAsync()).ToList();
        Assert.Single(result);
        Assert.Equal("M1", result[0].Nome);
    }

    [Fact]
    public async Task GetByIdAsync_RetornaModelo()
    {
        var svc = Build(out var ctx, out var tenantId, out var userId);
        var id = Guid.NewGuid();
        ctx.ModelosDocumento.Add(new ModeloDocumento
        {
            Id = id, TenantId = tenantId, CriadoPorId = userId,
            Nome = "M1", Conteudo = "C", Variaveis = "VAR1,VAR2", CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var result = await svc.GetByIdAsync(id);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Variaveis.Count);
    }

    [Fact]
    public async Task GetByIdAsync_NaoExiste_RetornaNull()
    {
        var svc = Build(out _, out _, out _);
        Assert.Null(await svc.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_CriaModelo()
    {
        var svc = Build(out _, out _, out _);
        var dto = new CreateModeloDocumentoDto { Nome = "Contrato", Descricao = "Desc", Conteudo = "Texto {{NOME}}", Variaveis = ["NOME"] };
        var result = await svc.CreateAsync(dto);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Contains("NOME", result.Variaveis);
    }

    [Fact]
    public async Task CreateAsync_TenantVazio_Throws()
    {
        var ctx = SvcTestHelpers.CreateCtx();
        var tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.TenantId).Returns(Guid.Empty);
        tenantMock.Setup(t => t.UserId).Returns(Guid.NewGuid());
        var svc = new ModeloDocumentoService(ctx, tenantMock.Object, new Mock<IIAService>().Object, new Mock<ICreditoService>().Object);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.CreateAsync(new CreateModeloDocumentoDto { Nome = "X", Conteudo = "Y", Variaveis = [] }));
    }

    [Fact]
    public async Task UpdateAsync_AtualizaModelo()
    {
        var svc = Build(out _, out _, out _);
        var created = await svc.CreateAsync(new CreateModeloDocumentoDto { Nome = "Old", Conteudo = "C", Variaveis = [] });
        var dto = new UpdateModeloDocumentoDto { Nome = "New", Conteudo = "Updated {{PART}}", Variaveis = ["PART"] };
        var result = await svc.UpdateAsync(created.Id, dto);
        Assert.Equal("New", result.Nome);
        Assert.Contains("PART", result.Variaveis);
    }

    [Fact]
    public async Task UpdateAsync_NaoEncontrado_Throws()
    {
        var svc = Build(out _, out _, out _);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.UpdateAsync(Guid.NewGuid(), new UpdateModeloDocumentoDto { Nome = "X" }));
    }

    [Fact]
    public async Task DeleteAsync_RemoveModelo()
    {
        var svc = Build(out var ctx, out _, out _);
        var created = await svc.CreateAsync(new CreateModeloDocumentoDto { Nome = "M", Conteudo = "C", Variaveis = [] });
        await svc.DeleteAsync(created.Id);
        Assert.Null(await ctx.ModelosDocumento.FindAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_NaoEncontrado_Throws()
    {
        var svc = Build(out _, out _, out _);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AplicarVariaveisAsync_SubstituiVariaveis()
    {
        var svc = Build(out _, out _, out _);
        var created = await svc.CreateAsync(new CreateModeloDocumentoDto { Nome = "M", Conteudo = "Olá {{NOME}}, CPF {{CPF}}.", Variaveis = ["NOME", "CPF"] });
        var result = await svc.AplicarVariaveisAsync(created.Id, new Dictionary<string, string> { ["NOME"] = "Maria", ["CPF"] = "123" });
        Assert.Equal("Olá Maria, CPF 123.", result);
    }

    [Fact]
    public async Task AplicarVariaveisAsync_NaoEncontrado_Throws()
    {
        var svc = Build(out _, out _, out _);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.AplicarVariaveisAsync(Guid.NewGuid(), new Dictionary<string, string>()));
    }

    [Fact]
    public async Task GerarComIAAsync_RetornaConteudo()
    {
        var ia = new Mock<IIAService>();
        ia.Setup(s => s.GerarModeloDocumentoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Modelo com {{NOME}} e {{DATA}}.");
        var credito = new Mock<ICreditoService>();
        credito.Setup(s => s.TemCreditoDisponivelAsync(It.IsAny<TipoCreditoAI>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        credito.Setup(s => s.ConsumirCreditoAsync(It.IsAny<TipoCreditoAI>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var svc = Build(out _, out _, out _, ia, credito);
        var result = await svc.GerarComIAAsync("contrato de prestação");
        Assert.Contains("NOME", result.Variaveis);
        Assert.Contains("DATA", result.Variaveis);
    }

    [Fact]
    public async Task GerarComIAAsync_SemCredito_Throws()
    {
        var credito = new Mock<ICreditoService>();
        credito.Setup(s => s.TemCreditoDisponivelAsync(It.IsAny<TipoCreditoAI>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var svc = Build(out _, out _, out _, credito: credito);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GerarComIAAsync("x"));
    }
}

// ─── EventoService – Create & Update ─────────────────────────────────────────

public class EventoServiceAdditionalTests
{
    private static (EventoService Svc, AppDbContext Ctx) Build()
    {
        var ctx = SvcTestHelpers.CreateCtx();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Pro, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.SaveChanges();
        return (new EventoService(ctx, SvcTestHelpers.TenantMock(tenantId, Guid.NewGuid()).Object), ctx);
    }

    [Fact]
    public async Task CreateAsync_CriaEvento()
    {
        var (svc, _) = Build();
        var dto = new CreateEventoDto("Audiência", TipoEvento.Audiencia, DateTime.UtcNow.AddDays(7), null, "Sala 1", null, null, null);
        var result = await svc.CreateAsync(dto);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Audiência", result.Titulo);
    }

    [Fact]
    public async Task UpdateAsync_AtualizaEvento()
    {
        var (svc, _) = Build();
        var created = await svc.CreateAsync(new CreateEventoDto("Old", TipoEvento.Audiencia, DateTime.UtcNow.AddDays(1), null, null, null, null, null));
        var dto = new UpdateEventoDto("New Title", TipoEvento.Reuniao, DateTime.UtcNow.AddDays(2), null, "Sala 3", null, null, "Obs");
        var result = await svc.UpdateAsync(created.Id, dto);
        Assert.Equal("New Title", result.Titulo);
        Assert.Equal(TipoEvento.Reuniao, result.Tipo);
    }

    [Fact]
    public async Task UpdateAsync_NaoEncontrado_Throws()
    {
        var (svc, _) = Build();
        var dto = new UpdateEventoDto("X", TipoEvento.Audiencia, DateTime.UtcNow, null, null, null, null, null);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.UpdateAsync(Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task GetAllAsync_ComFiltroTipo_FiltraCorreto()
    {
        var (svc, _) = Build();
        await svc.CreateAsync(new CreateEventoDto("A1", TipoEvento.Audiencia, DateTime.UtcNow.AddDays(3), null, null, null, null, null));
        await svc.CreateAsync(new CreateEventoDto("R1", TipoEvento.Reuniao, DateTime.UtcNow.AddDays(5), null, null, null, null, null));
        var filtro = new EventoFiltroDto(null, null, TipoEvento.Audiencia, null, null);
        var result = await svc.GetAllAsync(filtro);
        Assert.Equal(1, result.Total);
    }
}

// ─── TarefaService.MoverKanban ────────────────────────────────────────────────

public class TarefaServiceMoverKanbanTests
{
    private static (TarefaService Svc, AppDbContext Ctx, Guid TenantId) Build()
    {
        var ctx = SvcTestHelpers.CreateCtx();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Pro, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.SaveChanges();
        return (new TarefaService(ctx, SvcTestHelpers.TenantMock(tenantId, Guid.NewGuid()).Object), ctx, tenantId);
    }

    [Fact]
    public async Task MoverKanbanAsync_AtualizaStatus()
    {
        var (svc, ctx, tenantId) = Build();
        var tarefaId = Guid.NewGuid();
        ctx.Tarefas.Add(new Tarefa
        {
            Id = tarefaId, TenantId = tenantId, Titulo = "T1",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Media,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        await svc.MoverKanbanAsync(tarefaId, tenantId, StatusTarefa.EmAndamento);
        var tarefa = await ctx.Tarefas.FindAsync(tarefaId);
        Assert.Equal(StatusTarefa.EmAndamento, tarefa!.Status);
    }

    [Fact]
    public async Task MoverKanbanAsync_ParaConcluida_DefineDateConclusao()
    {
        var (svc, ctx, tenantId) = Build();
        var tarefaId = Guid.NewGuid();
        ctx.Tarefas.Add(new Tarefa
        {
            Id = tarefaId, TenantId = tenantId, Titulo = "T2",
            Status = StatusTarefa.EmAndamento, Prioridade = PrioridadeTarefa.Alta,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        await svc.MoverKanbanAsync(tarefaId, tenantId, StatusTarefa.Concluida);
        var tarefa = await ctx.Tarefas.FindAsync(tarefaId);
        Assert.Equal(StatusTarefa.Concluida, tarefa!.Status);
        Assert.NotNull(tarefa.ConcluidaEm);
    }

    [Fact]
    public async Task MoverKanbanAsync_NaoEncontrada_Throws()
    {
        var (svc, _, tenantId) = Build();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.MoverKanbanAsync(Guid.NewGuid(), tenantId, StatusTarefa.EmAndamento));
    }
}

// ─── DocumentoHelpers ─────────────────────────────────────────────────────────

public class DocumentoHelpersTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void FormatFileSize_RetornaFormatoCorreto(long bytes, string expected)
        => Assert.Equal(expected, DocumentoHelpers.FormatFileSize(bytes));

    [Fact]
    public void CotaArmazenamentoDto_CalcPorcentagem()
    {
        var dto = new CotaArmazenamentoDto { UsadoBytes = 512 * 1024 * 1024L, CotaBytes = 1024 * 1024 * 1024L };
        Assert.Equal(50.0, dto.PorcentagemUsada);
        Assert.Equal("512 MB", dto.UsadoFormatado);
        Assert.Equal("1 GB", dto.CotaFormatado);
    }

    [Fact]
    public void CotaArmazenamentoDto_CotaZero_PorcentagemZero()
    {
        var dto = new CotaArmazenamentoDto { UsadoBytes = 100, CotaBytes = 0 };
        Assert.Equal(0, dto.PorcentagemUsada);
    }

    [Fact]
    public void DocumentoDto_TamanhoFormatado_UsaHelper()
    {
        var dto = new DocumentoDto { TamanhoBytes = 2048 };
        Assert.Equal("2 KB", dto.TamanhoFormatado);
    }
}

// ─── EventosController – Create & Update ─────────────────────────────────────

public class EventosControllerCreateUpdateTests
{
    private static EventosController CreateController(Mock<IEventoService>? svc = null)
    {
        svc ??= new Mock<IEventoService>();
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.UserId).Returns(Guid.NewGuid());
        var ctrl = new EventosController(svc.Object, audit.Object, tenant.Object);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return ctrl;
    }

    [Fact]
    public async Task Create_RetornsCreated()
    {
        var dto = new CreateEventoDto("Audiência", TipoEvento.Audiencia, DateTime.UtcNow.AddDays(1), null, null, null, null, null);
        var response = new EventoResponseDto(Guid.NewGuid(), "Audiência", TipoEvento.Audiencia, DateTime.UtcNow.AddDays(1), null, null, null, null, null, null, null, DateTime.UtcNow);
        var svc = new Mock<IEventoService>();
        svc.Setup(s => s.CreateAsync(dto, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        var ctrl = CreateController(svc);
        var result = await ctrl.Create(dto, default);
        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Update_RetornsOk()
    {
        var id = Guid.NewGuid();
        var dto = new UpdateEventoDto("X", TipoEvento.Reuniao, DateTime.UtcNow, null, null, null, null, null);
        var response = new EventoResponseDto(id, "X", TipoEvento.Reuniao, DateTime.UtcNow, null, null, null, null, null, null, null, DateTime.UtcNow);
        var svc = new Mock<IEventoService>();
        svc.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        svc.Setup(s => s.UpdateAsync(id, dto, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        var ctrl = CreateController(svc);
        var result = await ctrl.Update(id, dto, default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_NaoEncontrado_RetornsNotFound()
    {
        var svc = new Mock<IEventoService>();
        svc.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((EventoResponseDto?)null);
        svc.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateEventoDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var ctrl = CreateController(svc);
        var result = await ctrl.Update(Guid.NewGuid(), new UpdateEventoDto("X", TipoEvento.Audiencia, DateTime.UtcNow, null, null, null, null, null), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }
}

// ─── NotificacoesController – missing methods ────────────────────────────────

public class NotificacoesControllerAdditionalTests
{
    private static NotificacoesController CreateController(
        Mock<INotificacaoService>? notifSvc = null,
        Mock<IPreferenciasNotificacaoService>? prefsSvc = null)
    {
        notifSvc ??= new Mock<INotificacaoService>();
        prefsSvc ??= new Mock<IPreferenciasNotificacaoService>();
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.UserId).Returns(Guid.NewGuid());
        return new NotificacoesController(notifSvc.Object, prefsSvc.Object, tenant.Object);
    }

    [Fact]
    public async Task GetHistorico_RetornsOk()
    {
        var svc = new Mock<INotificacaoService>();
        svc.Setup(s => s.GetHistoricoAsync(1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Application.DTOs.Notificacoes.NotificacaoDto>(), 0));
        var ctrl = CreateController(notifSvc: svc);
        var result = await ctrl.GetHistorico(1, 20);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetPreferencias_RetornsOk()
    {
        var prefs = new PreferenciasNotificacaoDto(true, true, true, true, true, true, true, true, true, true, true, true);
        var prefsSvc = new Mock<IPreferenciasNotificacaoService>();
        prefsSvc.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefs);
        var ctrl = CreateController(prefsSvc: prefsSvc);
        var result = await ctrl.GetPreferencias(default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<PreferenciasNotificacaoDto>(ok.Value);
    }

    [Fact]
    public async Task AtualizarPreferencias_RetornsOk()
    {
        var dto = new AtualizarPreferenciasDto(true, false, true, false, true, false, true, false, true, false, true, false);
        var prefs = new PreferenciasNotificacaoDto(true, false, true, false, true, false, true, false, true, false, true, false);
        var prefsSvc = new Mock<IPreferenciasNotificacaoService>();
        prefsSvc.Setup(s => s.AtualizarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefs);
        var ctrl = CreateController(prefsSvc: prefsSvc);
        var result = await ctrl.AtualizarPreferencias(dto, default);
        Assert.IsType<OkObjectResult>(result.Result);
    }
}

// ─── PrazosController – Calcular ─────────────────────────────────────────────

public class PrazosControllerAdditionalTests
{
    private static PrazosController CreateController(PlanoTipo plano = PlanoTipo.Pro)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.Plano).Returns(plano);
        return new PrazosController(tenant.Object);
    }

    [Fact]
    public void Calcular_DiasCorridos_ReturnsCorrectDataFinal()
    {
        var ctrl = CreateController();
        var inicio = new DateTime(2025, 1, 6); // segunda-feira
        var dto = new CalcularPrazoDto(inicio, 10, TipoCalculo.DiasCorridos);
        var result = ctrl.Calcular(dto);
        var ok = Assert.IsType<OkObjectResult>(result);
        var res = Assert.IsType<CalcularPrazoResultDto>(ok.Value);
        Assert.Equal(inicio.AddDays(10), res.DataFinal);
    }

    [Fact]
    public void Calcular_DiasUteis_SkipsWeekends()
    {
        var ctrl = CreateController();
        var sexta = new DateTime(2025, 1, 3); // sexta-feira
        var dto = new CalcularPrazoDto(sexta, 1, TipoCalculo.DiasUteis);
        var result = ctrl.Calcular(dto);
        var ok = Assert.IsType<OkObjectResult>(result);
        var res = Assert.IsType<CalcularPrazoResultDto>(ok.Value);
        Assert.Equal(DayOfWeek.Monday, res.DataFinal.DayOfWeek);
    }
}
