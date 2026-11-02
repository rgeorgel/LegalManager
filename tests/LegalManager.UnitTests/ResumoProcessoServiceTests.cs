using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class ResumoProcessoServiceTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IIAService> _ia = new();
    private readonly Mock<ICreditoService> _creditos = new();
    private readonly Mock<IBackgroundJobClient> _jobs = new();
    private readonly ResumoProcessoService _service;
    private string? _contextoEnviado;

    public ResumoProcessoServiceTests()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(_tenantId);
        _creditos.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.TraducaoAndamento, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _ia.Setup(i => i.GerarResumoProcessoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((c, _) => _contextoEnviado = c)
            .ReturnsAsync("Resumo gerado.");
        _db.Users.Add(new Usuario { Id = _userId, TenantId = _tenantId, Nome = "Ana", UserName = "ana@x.com", Email = "ana@x.com" });
        _db.SaveChanges();
        _service = new ResumoProcessoService(_db, _ia.Object, _creditos.Object, tenant.Object, _jobs.Object,
            NullLogger<ResumoProcessoService>.Instance);
    }

    private Processo SeedProcesso(bool completo)
    {
        var p = new Processo
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, NumeroCNJ = "0001234-56.2024.8.26.0100", CriadoEm = DateTime.UtcNow,
            AreaDireito = AreaDireito.Civil, Status = StatusProcesso.Ativo
        };
        if (completo)
        {
            p.Tribunal = "TJSP"; p.Vara = "1ª Vara"; p.Comarca = "São Paulo"; p.TipoAcao = "Cobrança";
            p.ValorCausa = 12345.67m; p.Classe = "Procedimento Comum"; p.Assuntos = "Inadimplemento"; p.Observacoes = "Urgente";
            var contato = new Contato { Id = Guid.NewGuid(), TenantId = _tenantId, Nome = "Maria Cliente" };
            _db.Contatos.Add(contato);
            p.Partes.Add(new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = p.Id, ContatoId = contato.Id, TipoParte = TipoParteProcesso.Autor });
            _db.Andamentos.Add(new Andamento { Id = Guid.NewGuid(), TenantId = _tenantId, ProcessoId = p.Id, Data = new DateTime(2026, 3, 1), Tipo = TipoAndamento.Despacho, Descricao = "Cite-se." });
            _db.Documentos.Add(new Documento { Id = Guid.NewGuid(), TenantId = _tenantId, ProcessoId = p.Id, Nome = "inicial.pdf", Tipo = TipoDocumento.Peticao });
            _db.Tarefas.AddRange(
                new Tarefa { Id = Guid.NewGuid(), TenantId = _tenantId, ProcessoId = p.Id, Titulo = "Réplica", Status = StatusTarefa.Pendente, Prazo = new DateTime(2026, 4, 1), CriadoPorId = _userId },
                new Tarefa { Id = Guid.NewGuid(), TenantId = _tenantId, ProcessoId = p.Id, Titulo = "Sem prazo", Status = StatusTarefa.EmAndamento, CriadoPorId = _userId },
                new Tarefa { Id = Guid.NewGuid(), TenantId = _tenantId, ProcessoId = p.Id, Titulo = "Feita", Status = StatusTarefa.Concluida, CriadoPorId = _userId });
        }
        _db.Processos.Add(p);
        _db.SaveChanges();
        return p;
    }

    /// <summary>Pede o resumo e roda o que o job faria.</summary>
    private async Task<ResumoProcessoResponseDto> GerarAsync(Guid processoId, Guid usuarioId)
    {
        var pedido = await _service.SolicitarAsync(new GerarResumoDto(processoId), usuarioId);
        await _service.ProcessarAsync(pedido.Id);
        _db.ChangeTracker.Clear();
        return (await _service.ObterAsync(pedido.Id))!;
    }

    private void VerificarJobsEnfileirados(int vezes) =>
        _jobs.Verify(j => j.Create(It.Is<Job>(job => job.Type == typeof(LegalManager.Infrastructure.Jobs.ResumoProcessoJob)),
            It.IsAny<EnqueuedState>()), Times.Exactly(vezes));

    [Fact]
    public async Task Solicitar_CriaResumoGerandoEEnfileiraOJob_SemChamarAIA()
    {
        var p = SeedProcesso(completo: false);

        var r = await _service.SolicitarAsync(new GerarResumoDto(p.Id), _userId);

        Assert.Equal("Gerando", r.Status);
        Assert.Equal("ana@x.com", r.GeradoPorNome);
        VerificarJobsEnfileirados(1);
        _ia.Verify(i => i.GerarResumoProcessoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Solicitar_ComResumoAindaGerando_DevolveOMesmoSemEnfileirarOutro()
    {
        var p = SeedProcesso(completo: false);

        var primeiro = await _service.SolicitarAsync(new GerarResumoDto(p.Id), _userId);
        var segundo = await _service.SolicitarAsync(new GerarResumoDto(p.Id), _userId);

        Assert.Equal(primeiro.Id, segundo.Id);
        Assert.Single(_db.ResumosProcesso);
        VerificarJobsEnfileirados(1);
    }

    [Fact]
    public async Task Processar_MontaContextoCompleto_ConsomeCreditoGravaENotifica()
    {
        var p = SeedProcesso(completo: true);

        var r = await GerarAsync(p.Id, _userId);

        Assert.Equal("Concluido", r.Status);
        Assert.Equal("Resumo gerado.", r.Conteudo);
        Assert.Equal("ana@x.com", r.GeradoPorNome);
        Assert.Contains("Tribunal: TJSP", _contextoEnviado);
        Assert.Contains("Autor: Maria Cliente", _contextoEnviado);
        Assert.Contains("Despacho: Cite-se.", _contextoEnviado);
        Assert.Contains("inicial.pdf (Peticao)", _contextoEnviado);
        Assert.Contains("Réplica (Pendente, prazo: 01/04/2026)", _contextoEnviado);
        Assert.DoesNotContain("Feita", _contextoEnviado);
        _creditos.Verify(c => c.ConsumirCreditoAsync(TipoCreditoAI.TraducaoAndamento, 1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_db.ResumosProcesso);
        var notificacao = Assert.Single(_db.Notificacoes);
        Assert.Equal(_userId, notificacao.UsuarioId);
        Assert.Equal($"/pages/processo-detalhe.html?id={p.Id}&resumo={r.Id}", notificacao.Url);
    }

    [Fact]
    public async Task Processar_ProcessoVazio_UsaTextosPadrao()
    {
        var p = SeedProcesso(completo: false);

        await GerarAsync(p.Id, _userId);

        Assert.Contains("Nenhuma parte cadastrada.", _contextoEnviado);
        Assert.Contains("Nenhum andamento registrado.", _contextoEnviado);
        Assert.Contains("Nenhum documento anexado.", _contextoEnviado);
        Assert.Contains("Nenhuma tarefa em aberto.", _contextoEnviado);
        Assert.Contains("Tribunal: —", _contextoEnviado);
    }

    [Fact]
    public async Task Processar_IAFalhaUmaVez_TentaDeNovo()
    {
        var p = SeedProcesso(completo: false);
        _ia.SetupSequence(i => i.GerarResumoProcessoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("demorou"))
            .ReturnsAsync("Resumo na segunda.");

        var r = await GerarAsync(p.Id, _userId);

        Assert.Equal("Concluido", r.Status);
        Assert.Equal("Resumo na segunda.", r.Conteudo);
    }

    [Fact]
    public async Task Processar_IAFalhaNasDuasTentativas_GravaErroComMensagemENotifica()
    {
        var p = SeedProcesso(completo: false);
        _ia.Setup(i => i.GerarResumoProcessoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IAIndisponivelException("A IA não conseguiu concluir a resposta."));

        var r = await GerarAsync(p.Id, _userId);

        Assert.Equal("Erro", r.Status);
        Assert.Equal("A IA não conseguiu concluir a resposta.", r.MensagemErro);
        _ia.Verify(i => i.GerarResumoProcessoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _creditos.Verify(c => c.ConsumirCreditoAsync(It.IsAny<TipoCreditoAI>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("Falha", Assert.Single(_db.Notificacoes).Titulo);
    }

    [Fact]
    public async Task Processar_ErroInesperado_GravaMensagemGenerica()
    {
        var p = SeedProcesso(completo: false);
        _ia.Setup(i => i.GerarResumoProcessoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("detalhe interno"));

        var r = await GerarAsync(p.Id, _userId);

        Assert.Equal("Erro", r.Status);
        Assert.Equal("Não foi possível gerar o resumo. Tente novamente.", r.MensagemErro);
    }

    [Fact]
    public async Task Processar_ResumoJaConcluido_NaoFazNada()
    {
        var p = SeedProcesso(completo: false);
        var r = await GerarAsync(p.Id, _userId);

        await _service.ProcessarAsync(r.Id);

        _ia.Verify(i => i.GerarResumoProcessoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Listar_GerandoHaMuitoTempo_ApareceComoInterrompido()
    {
        var p = SeedProcesso(completo: false);
        _db.ResumosProcesso.Add(new ResumoProcesso
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProcessoId = p.Id, GeradoPorId = _userId,
            Status = StatusResumoProcesso.Gerando, GeradoEm = DateTime.UtcNow.AddHours(-1)
        });
        _db.SaveChanges();

        var r = Assert.Single(await _service.ListarAsync(p.Id));

        Assert.Equal("Erro", r.Status);
        Assert.Contains("interrompida", r.MensagemErro);
    }

    [Fact]
    public async Task Solicitar_SemCredito_OuProcessoDeOutroTenant_Falha()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SolicitarAsync(new GerarResumoDto(Guid.NewGuid()), _userId));

        _creditos.Setup(c => c.TemCreditoDisponivelAsync(It.IsAny<TipoCreditoAI>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SolicitarAsync(new GerarResumoDto(SeedProcesso(false).Id), _userId));
        Assert.Contains("Créditos", ex.Message);
        _ia.Verify(i => i.GerarResumoProcessoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Listar_ESetVisivelCliente()
    {
        var p = SeedProcesso(false);
        var r = await GerarAsync(p.Id, _userId);
        var gerando = await _service.SolicitarAsync(new GerarResumoDto(p.Id), _userId);
        _db.ResumosProcesso.Add(new ResumoProcesso { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProcessoId = p.Id, GeradoPorId = _userId, Conteudo = "alheio" });
        _db.SaveChanges();

        var lista = (await _service.ListarAsync(p.Id)).ToList();
        Assert.Equal([gerando.Id, r.Id], lista.Select(x => x.Id));

        var visivel = await _service.SetVisivelClienteAsync(r.Id, true);
        Assert.True(visivel.VisivelCliente);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetVisivelClienteAsync(Guid.NewGuid(), true));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetVisivelClienteAsync(gerando.Id, true));
        Assert.Contains("concluído", ex.Message);
    }
}
