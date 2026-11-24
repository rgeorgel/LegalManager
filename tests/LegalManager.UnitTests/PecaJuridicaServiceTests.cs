using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class PecaJuridicaServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private ITenantContext CreateTenantContext(Guid tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Guid tenantId, Guid usuarioId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        var usuarioId = Guid.NewGuid();
        var usuario = new Usuario
        {
            Id = usuarioId, TenantId = tenantId, Nome = "Advogado",
            Email = "adv@test.com", UserName = "adv@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        ctx.CreditosAI.Add(new CreditoAI
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            Tipo = TipoCreditoAI.GeracaoPeca, QuantidadeTotal = 5, QuantidadeUsada = 0,
            Origem = OrigemCreditoAI.Cortesai, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, usuarioId);
    }

    [Fact]
    public async Task GerarPecaAsync_DeveLancarExcecao_QuandoCreditoEsgotado()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var credito = await ctx.CreditosAI.FirstAsync(c => c.TenantId == tenantId);
        credito.QuantidadeUsada = credito.QuantidadeTotal;
        await ctx.SaveChangesAsync();

        var mockIa = new Mock<IIAService>();
        var mockCredito = new Mock<ICreditoService>();
        mockCredito.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId), mockIa.Object, mockCredito.Object);
        var dto = new GerarPecaDto(null, TipoPecaJuridica.PeticaoInicial, "Ação de cobrança");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GerarPecaAsync(dto, usuarioId));
    }

    [Fact]
    public async Task GerarPecaAsync_DeveGerarPeca_QuandoCreditoDisponivel()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var mockIa = new Mock<IIAService>();
        mockIa.Setup(i => i.GerarPecaJuridicaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Petição gerada com sucesso.");
        mockIa.Setup(i => i.BuscarJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Jurisprudência aplicável.");

        var mockCredito = new Mock<ICreditoService>();
        mockCredito.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockCredito.Setup(c => c.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId), mockIa.Object, mockCredito.Object);
        var dto = new GerarPecaDto(null, TipoPecaJuridica.Contestacao, "Ação trabalhista");

        var result = await service.GerarPecaAsync(dto, usuarioId);

        Assert.NotNull(result);
        Assert.Equal(TipoPecaJuridica.Contestacao, result.Tipo);
        Assert.Equal("Petição gerada com sucesso.", result.ConteudoGerado);
        mockCredito.Verify(c => c.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GerarPecaAsync_GeraSemPrecedentes_QuandoBuscaNaBaseFalha()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var tenant = await ctx.Tenants.FirstAsync(t => t.Id == tenantId);
        tenant.RecursosLiberados = [LegalManager.Domain.RecursosEmTeste.JurisprudenciaVerificada];
        await ctx.SaveChangesAsync();

        var mockIa = new Mock<IIAService>();
        mockIa.Setup(i => i.GerarPecaJuridicaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Manifestação gerada.");
        mockIa.Setup(i => i.BuscarJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Entendimentos.");
        var mockCredito = new Mock<ICreditoService>();
        mockCredito.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockCredito.Setup(c => c.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // Busca full-text que estoura o tempo do comando (como em produção, "prazo perdido... sentença").
        var mockJuris = new Mock<IJurisprudenciaService>();
        mockJuris.Setup(j => j.BuscarPrecedentesPorSignificadoAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "An exception has been raised that is likely due to a transient failure.", new TimeoutException()));
        mockJuris.Setup(j => j.VerificarTextoAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificacaoCitacoesDto([], 0, 0, 0, ""));

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId), mockIa.Object, mockCredito.Object, mockJuris.Object);
        var result = await service.GerarPecaAsync(new GerarPecaDto(null, TipoPecaJuridica.Manifestacao, "prazo perdido, para cumprimento de sentenca"), usuarioId);

        Assert.Equal("Manifestação gerada.", result.ConteudoGerado);
        mockCredito.Verify(c => c.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObterPecaAsync_DeveRetornarPeca_QuandoExistir()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var pecaId = Guid.NewGuid();
        ctx.PecasGeradas.Add(new PecaGerada
        {
            Id = pecaId, TenantId = tenantId, GeradoPorId = usuarioId,
            Tipo = TipoPecaJuridica.Recurso, DescricaoSolicitacao = "Recurso de apelação",
            ConteudoGerado = "Conteúdo do recurso", CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ObterPecaAsync(pecaId);

        Assert.NotNull(result);
        Assert.Equal(pecaId, result.Id);
    }

    [Fact]
    public async Task ObterPecaAsync_DeveRetornarNull_QuandoNaoExistir()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ObterPecaAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ObterPecaAsync_DeveRetornarNull_QuandoDeOutroTenant()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var outroTenantId = Guid.NewGuid();
        var pecaId = Guid.NewGuid();
        ctx.PecasGeradas.Add(new PecaGerada
        {
            Id = pecaId, TenantId = outroTenantId, GeradoPorId = Guid.NewGuid(),
            Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = "Teste",
            ConteudoGerado = "Teste", CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ObterPecaAsync(pecaId);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListarAsync_DeveRetornarPecasDoTenant()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        ctx.PecasGeradas.AddRange(
            new PecaGerada { Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId, Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = "Peca 1", ConteudoGerado = "C1", CriadoEm = DateTime.UtcNow.AddMinutes(-1) },
            new PecaGerada { Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId, Tipo = TipoPecaJuridica.Contestacao, DescricaoSolicitacao = "Peca 2", ConteudoGerado = "C2", CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ListarAsync(new ListPecasGeradasDto(1, 10, null, null));

        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task ListarAsync_DeveFiltrarPorProcessoId()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var processoId = Guid.NewGuid();
        ctx.PecasGeradas.AddRange(
            new PecaGerada { Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId, ProcessoId = processoId, Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = "Peca 1", ConteudoGerado = "C1", CriadoEm = DateTime.UtcNow },
            new PecaGerada { Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId, Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = "Peca 2", ConteudoGerado = "C2", CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ListarAsync(new ListPecasGeradasDto(1, 10, processoId, null));

        Assert.Single(result);
    }

    [Fact]
    public async Task ListarAsync_DevePaginarCorretamente()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        for (int i = 0; i < 15; i++)
        {
            ctx.PecasGeradas.Add(new PecaGerada
            {
                Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId,
                Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = $"Peca {i}",
                ConteudoGerado = $"C{i}", CriadoEm = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var page1 = await service.ListarAsync(new ListPecasGeradasDto(1, 10, null, null));
        var page2 = await service.ListarAsync(new ListPecasGeradasDto(2, 10, null, null));

        Assert.Equal(10, page1.Count());
        Assert.Equal(5, page2.Count());
    }

    // ── Jurisprudência verificada ────────────────────────────────────────────

    private static Mock<ICreditoService> CreditoLiberado()
    {
        var mock = new Mock<ICreditoService>();
        mock.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mock.Setup(c => c.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return mock;
    }

    private static PrecedenteDto PrecedenteExemplo() => new(
        Guid.NewGuid(), "Acordao", "STJ", "REsp", "1234567", "REsp 1.234.567/STJ", "Nancy Andrighi", "Terceira Turma",
        new DateTime(2026, 5, 12), "Busca e apreensão. Alienação fiduciária.", null, "https://processo.stj.jus.br/x");

    [Fact]
    public async Task GerarPecaAsync_SemRecurso_MarcaCitacoesComoNaoVerificadas()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var ia = new Mock<IIAService>();
        ia.Setup(i => i.GerarPecaJuridicaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Incide a Súmula 7/STJ e o REsp 1.234.567/SP.");
        ia.Setup(i => i.BuscarJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("Entendimentos.");
        var jurisprudencia = new Mock<IJurisprudenciaService>(MockBehavior.Strict);
        jurisprudencia.Setup(j => j.VerificarTextoAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string t, bool _, CancellationToken _) => new VerificacaoCitacoesDto(
                [new CitacaoVerificadaDto(TipoCitacao.Sumula, "STJ", null, "7", "Súmula 7/STJ", 10, 22, StatusCitacao.NaoVerificada, "Confira.")],
                0, 1, 0, "não habilitada"));

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId), ia.Object, CreditoLiberado().Object, jurisprudencia.Object);
        var result = await service.GerarPecaAsync(new GerarPecaDto(null, TipoPecaJuridica.Contestacao, "Ação de cobrança"), usuarioId);

        Assert.Equal(StatusCitacao.NaoVerificada, Assert.Single(result.Verificacao!.Citacoes).Status);
        Assert.Null(result.Precedentes);
        Assert.Equal("Entendimentos.", result.JurisprudenciaCitada);
        ia.Verify(i => i.GerarPecaComPrecedentesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        // Lida de novo do banco: a conferência ficou gravada.
        var salva = await service.ObterPecaAsync(result.Id);
        Assert.Equal("Súmula 7/STJ", Assert.Single(salva!.Verificacao!.Citacoes).Trecho);
    }

    [Fact]
    public async Task GerarPecaAsync_ComRecurso_UsaPrecedentesDaBase()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        tenant!.RecursosLiberados.Add(LegalManager.Domain.RecursosEmTeste.JurisprudenciaVerificada);
        await ctx.SaveChangesAsync();

        string? listaEnviada = null;
        var ia = new Mock<IIAService>(MockBehavior.Strict);
        ia.Setup(i => i.GerarPecaComPrecedentesAsync(It.IsAny<string>(), "Contestacao", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, string lista, CancellationToken _) => listaEnviada = lista)
            .ReturnsAsync("Nos termos do REsp 1.234.567/STJ, Rel. Min. Nancy Andrighi.");
        var precedente = PrecedenteExemplo();
        var jurisprudencia = new Mock<IJurisprudenciaService>(MockBehavior.Strict);
        jurisprudencia.Setup(j => j.BuscarPrecedentesPorSignificadoAsync(It.Is<string>(s => s.Contains("busca e apreensão")), PecaJuridicaService.PrecedentesPorPeca, It.IsAny<CancellationToken>()))
            .ReturnsAsync([precedente]);
        jurisprudencia.Setup(j => j.VerificarTextoAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificacaoCitacoesDto(
                [new CitacaoVerificadaDto(TipoCitacao.Acordao, "STJ", "REsp", "1234567", "REsp 1.234.567/STJ", 13, 31, StatusCitacao.Verificada, "Encontrado.", precedente)],
                1, 0, 0, "Conferido na base oficial"));

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId), ia.Object, CreditoLiberado().Object, jurisprudencia.Object);
        var result = await service.GerarPecaAsync(new GerarPecaDto(null, TipoPecaJuridica.Contestacao, "Contestação em busca e apreensão de veículo"), usuarioId);

        Assert.NotNull(listaEnviada);
        Assert.Contains("[1] REsp 1.234.567/STJ — Terceira Turma, Rel. Min. Nancy Andrighi, julgado em 12/05/2026", listaEnviada);
        Assert.Equal(precedente.Id, Assert.Single(result.Precedentes!).Id);
        Assert.Equal(1, result.Verificacao!.Verificadas);
        Assert.Null(result.JurisprudenciaCitada);
    }

    [Fact]
    public async Task GerarPecaAsync_ComRecursoSemPrecedentesAchados_GeraSemLista()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        (await ctx.Tenants.FindAsync(tenantId))!.RecursosLiberados.Add(LegalManager.Domain.RecursosEmTeste.JurisprudenciaVerificada);
        await ctx.SaveChangesAsync();
        var ia = new Mock<IIAService>();
        ia.Setup(i => i.GerarPecaJuridicaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("Texto.");
        ia.Setup(i => i.BuscarJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("Entendimentos.");
        var jurisprudencia = new Mock<IJurisprudenciaService>();
        jurisprudencia.Setup(j => j.BuscarPrecedentesPorSignificadoAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        jurisprudencia.Setup(j => j.VerificarTextoAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificacaoCitacoesDto([], 0, 0, 0, ""));

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId), ia.Object, CreditoLiberado().Object, jurisprudencia.Object);
        var result = await service.GerarPecaAsync(new GerarPecaDto(null, TipoPecaJuridica.Pedido, "Pedido simples"), usuarioId);

        Assert.Equal("Texto.", result.ConteudoGerado);
        Assert.Null(result.Precedentes);
    }

    [Fact]
    public async Task GerarPecaAsync_SemServicoDeJurisprudencia_AindaMarcaAsCitacoes()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var ia = new Mock<IIAService>();
        ia.Setup(i => i.GerarPecaJuridicaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Conforme o Tema 1.076/STJ.");
        ia.Setup(i => i.BuscarJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("");

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId), ia.Object, CreditoLiberado().Object);
        var result = await service.GerarPecaAsync(new GerarPecaDto(null, TipoPecaJuridica.Pedido, "Pedido"), usuarioId);

        var c = Assert.Single(result.Verificacao!.Citacoes);
        Assert.Equal(StatusCitacao.NaoVerificada, c.Status);
        Assert.Equal("Tema 1.076/STJ", c.Trecho);
    }

    [Fact]
    public async Task ObterPecaAsync_JsonInvalido_NaoQuebra()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var id = Guid.NewGuid();
        ctx.PecasGeradas.Add(new PecaGerada
        {
            Id = id, TenantId = tenantId, GeradoPorId = usuarioId, Tipo = TipoPecaJuridica.Pedido,
            DescricaoSolicitacao = "x", ConteudoGerado = "y", CitacoesJson = "{quebrado", CriadoEm = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var result = await new PecaJuridicaService(ctx, CreateTenantContext(tenantId), Mock.Of<IIAService>(), Mock.Of<ICreditoService>())
            .ObterPecaAsync(id);

        Assert.Null(result!.Verificacao);
    }
}
