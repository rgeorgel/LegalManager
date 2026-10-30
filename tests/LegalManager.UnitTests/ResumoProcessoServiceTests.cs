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

public class ResumoProcessoServiceTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IIAService> _ia = new();
    private readonly Mock<ICreditoService> _creditos = new();
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
        _service = new ResumoProcessoService(_db, _ia.Object, _creditos.Object, tenant.Object);
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

    [Fact]
    public async Task Gerar_MontaContextoCompleto_ConsomeCreditoEGrava()
    {
        var p = SeedProcesso(completo: true);

        var r = await _service.GerarAsync(new GerarResumoDto(p.Id), _userId);

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
    }

    [Fact]
    public async Task Gerar_ProcessoVazio_UsaTextosPadrao()
    {
        var p = SeedProcesso(completo: false);

        await _service.GerarAsync(new GerarResumoDto(p.Id), Guid.NewGuid());

        Assert.Contains("Nenhuma parte cadastrada.", _contextoEnviado);
        Assert.Contains("Nenhum andamento registrado.", _contextoEnviado);
        Assert.Contains("Nenhum documento anexado.", _contextoEnviado);
        Assert.Contains("Nenhuma tarefa em aberto.", _contextoEnviado);
        Assert.Contains("Tribunal: —", _contextoEnviado);
    }

    [Fact]
    public async Task Gerar_SemCredito_OuProcessoDeOutroTenant_Falha()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GerarAsync(new GerarResumoDto(Guid.NewGuid()), _userId));

        _creditos.Setup(c => c.TemCreditoDisponivelAsync(It.IsAny<TipoCreditoAI>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GerarAsync(new GerarResumoDto(SeedProcesso(false).Id), _userId));
        Assert.Contains("Créditos", ex.Message);
        _ia.Verify(i => i.GerarResumoProcessoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Listar_ESetVisivelCliente()
    {
        var p = SeedProcesso(false);
        var r = await _service.GerarAsync(new GerarResumoDto(p.Id), _userId);
        _db.ResumosProcesso.Add(new ResumoProcesso { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProcessoId = p.Id, GeradoPorId = _userId, Conteudo = "alheio" });
        _db.SaveChanges();

        var lista = (await _service.ListarAsync(p.Id)).ToList();
        Assert.Equal(r.Id, Assert.Single(lista).Id);

        var visivel = await _service.SetVisivelClienteAsync(r.Id, true);
        Assert.True(visivel.VisivelCliente);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetVisivelClienteAsync(Guid.NewGuid(), true));
    }
}
