using LegalManager.API.Controllers;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class TenantOabServiceTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<IEscavadorService> _escavador = new();
    private long _proximoMonitoramento = 100;

    public TenantOabServiceTests()
    {
        _escavador.Setup(e => e.CriarMonitoramentoOabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new EscavadorMonitoramentoDto(++_proximoMonitoramento, "ATIVO"));
        _escavador.Setup(e => e.RemoverMonitoramentoTermoAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private TenantOabService Service(PlanoTipo plano = PlanoTipo.Max)
    {
        var t = new Mock<ITenantContext>();
        t.Setup(x => x.TenantId).Returns(_tenantId);
        t.Setup(x => x.Plano).Returns(plano);
        return new TenantOabService(_db, t.Object, _escavador.Object, FakeConsultaExternaLogService.Instance, NullLogger<TenantOabService>.Instance);
    }

    [Fact]
    public async Task Criar_NormalizaESincroniza()
    {
        var dto = await Service().CriarAsync(new CriarTenantOabRequest(null, "sp", "123.456", "  Ana Advogada "));

        Assert.Equal("SP", dto.Uf);
        Assert.Equal("123456", dto.Numero);
        Assert.Equal("Ana Advogada", dto.Nome);
        Assert.True(dto.Sincronizada);
        Assert.Equal(101, dto.EscavadorMonitoramentoId);

        var lista = await Service().ListarAsync();
        Assert.Single(lista);
    }

    [Theory]
    [InlineData("", "12345")]
    [InlineData("XX", "12345")]
    [InlineData("SP", "12")]
    [InlineData("SP", "12345678")]
    public async Task Criar_OabInvalida_ArgumentException(string uf, string numero)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CriarAsync(new CriarTenantOabRequest(null, uf, numero, "X")));
    }

    [Fact]
    public async Task Criar_PlanoSemCaptura_NaoAutorizado()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Service(PlanoTipo.Free).CriarAsync(new CriarTenantOabRequest(null, "SP", "12345", "X")));
    }

    [Fact]
    public async Task Criar_DuplicadaOuAcimaDoLimite_InvalidOperation()
    {
        var svc = Service(PlanoTipo.Max);
        await svc.CriarAsync(new CriarTenantOabRequest(null, "SP", "11111", "A"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CriarAsync(new CriarTenantOabRequest(null, "SP", "11111", "A")));

        var pro = Service(PlanoTipo.Pro);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pro.CriarAsync(new CriarTenantOabRequest(null, "RJ", "22222", "B")));
        Assert.Contains("Limite", ex.Message);
    }

    [Fact]
    public async Task Criar_FalhaNoEscavador_GuardaErroSemDerrubar()
    {
        _escavador.Setup(e => e.CriarMonitoramentoOabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(new string('e', 400)));

        var dto = await Service().CriarAsync(new CriarTenantOabRequest(null, "SP", "12345", "A"));

        Assert.False(dto.Sincronizada);
        Assert.EndsWith("…", dto.SyncError);
    }

    [Fact]
    public async Task Criar_EscavadorRetornaNull_RegistraErro()
    {
        _escavador.Setup(e => e.CriarMonitoramentoOabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EscavadorMonitoramentoDto?)null);

        var dto = await Service().CriarAsync(new CriarTenantOabRequest(null, "SP", "12345", "A"));
        Assert.Contains("null", dto.SyncError);
    }

    [Fact]
    public async Task Atualizar_MudancaDeNumero_RecriaMonitoramento()
    {
        var svc = Service();
        var criada = await svc.CriarAsync(new CriarTenantOabRequest(null, "SP", "12345", "A"));

        var dto = await svc.AtualizarAsync(criada.Id, new AtualizarTenantOabRequest(null, "SP", "54321", "A", true));

        Assert.Equal("54321", dto.Numero);
        Assert.Equal(102, dto.EscavadorMonitoramentoId);
        _escavador.Verify(e => e.RemoverMonitoramentoTermoAsync(101, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Atualizar_Desativar_LimpaMonitoramento_EReativarRespeitaLimite()
    {
        var svc = Service(PlanoTipo.Pro);
        var a = await svc.CriarAsync(new CriarTenantOabRequest(null, "SP", "12345", "A"));

        var desativada = await svc.AtualizarAsync(a.Id, new AtualizarTenantOabRequest(null, "SP", "12345", "A", false));
        Assert.Null(desativada.EscavadorMonitoramentoId);

        var b = await svc.CriarAsync(new CriarTenantOabRequest(null, "RJ", "99999", "B"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AtualizarAsync(a.Id, new AtualizarTenantOabRequest(null, "SP", "12345", "A", true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AtualizarAsync(b.Id, new AtualizarTenantOabRequest(null, "SP", "12345", "A", true)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.AtualizarAsync(Guid.NewGuid(), new AtualizarTenantOabRequest(null, "SP", "12345", "A", true)));
    }

    [Fact]
    public async Task Atualizar_SemMudancaRemota_NaoChamaEscavador()
    {
        var svc = Service();
        var a = await svc.CriarAsync(new CriarTenantOabRequest(null, "SP", "12345", "A"));
        var userId = Guid.NewGuid();

        var dto = await svc.AtualizarAsync(a.Id, new AtualizarTenantOabRequest(userId, "SP", "12345", "A", true));

        Assert.Equal(userId, dto.UserId);
        _escavador.Verify(e => e.RemoverMonitoramentoTermoAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remover_RemoveRemotoMesmoComFalha()
    {
        var svc = Service();
        var a = await svc.CriarAsync(new CriarTenantOabRequest(null, "SP", "12345", "A"));
        _escavador.Setup(e => e.RemoverMonitoramentoTermoAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("down"));

        await svc.RemoverAsync(a.Id);

        Assert.Empty(_db.TenantOabs);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.RemoverAsync(a.Id));
    }

    [Fact]
    public async Task Sincronizar_AtivaRecriaInativaLimpa()
    {
        var svc = Service();
        var ativa = await svc.CriarAsync(new CriarTenantOabRequest(null, "SP", "12345", "A"));
        var inativa = new TenantOab { Id = Guid.NewGuid(), TenantId = _tenantId, Uf = "RJ", Numero = "54321", Nome = "B", Ativo = false, EscavadorMonitoramentoId = 7 };
        _db.TenantOabs.Add(inativa);
        _db.SaveChanges();

        Assert.Equal(102, (await svc.SincronizarAsync(ativa.Id)).EscavadorMonitoramentoId);
        Assert.Null((await svc.SincronizarAsync(inativa.Id)).EscavadorMonitoramentoId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.SincronizarAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SincronizarTodas_SoAsAtivasSemMonitoramento()
    {
        _db.TenantOabs.AddRange(
            new TenantOab { Id = Guid.NewGuid(), TenantId = _tenantId, Uf = "SP", Numero = "111", Nome = "A", Ativo = true },
            new TenantOab { Id = Guid.NewGuid(), TenantId = _tenantId, Uf = "SP", Numero = "222", Nome = "B", Ativo = true, EscavadorMonitoramentoId = 5 },
            new TenantOab { Id = Guid.NewGuid(), TenantId = _tenantId, Uf = "SP", Numero = "333", Nome = "C", Ativo = false });
        _db.SaveChanges();

        Assert.Equal(1, await Service().SincronizarTodasAsync());
    }

    [Fact]
    public async Task MarcarVerificadas_ListaVazia_NaoFazNada()
    {
        await Service().MarcarVerificadasAsync([], DateTime.UtcNow);
    }

    // ── Controller ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Controller_MapeiaResultados()
    {
        var c = new TenantOabsController(Service(PlanoTipo.Max));

        Assert.IsType<BadRequestObjectResult>(await c.Criar(null!, default));
        var criado = Assert.IsType<CreatedAtActionResult>(await c.Criar(new CriarTenantOabRequest(null, "SP", "12345", "A"), default));
        var id = ((TenantOabDto)criado.Value!).Id;
        Assert.IsType<ConflictObjectResult>(await c.Criar(new CriarTenantOabRequest(null, "SP", "12345", "A"), default));
        Assert.IsType<BadRequestObjectResult>(await c.Criar(new CriarTenantOabRequest(null, "ZZ", "12345", "A"), default));
        Assert.IsType<OkObjectResult>(await c.Listar(default));

        Assert.IsType<BadRequestObjectResult>(await c.Atualizar(id, null!, default));
        Assert.IsType<OkObjectResult>(await c.Atualizar(id, new AtualizarTenantOabRequest(null, "SP", "12345", "A2", true), default));
        Assert.IsType<NotFoundObjectResult>(await c.Atualizar(Guid.NewGuid(), new AtualizarTenantOabRequest(null, "SP", "12345", "A2", true), default));
        Assert.IsType<BadRequestObjectResult>(await c.Atualizar(id, new AtualizarTenantOabRequest(null, "SP", "1", "A2", true), default));

        Assert.IsType<OkObjectResult>(await c.Sincronizar(id, default));
        Assert.IsType<NotFoundObjectResult>(await c.Sincronizar(Guid.NewGuid(), default));
        Assert.IsType<OkObjectResult>(await c.SincronizarTodas(default));

        Assert.IsType<NoContentResult>(await c.Remover(id, default));
        Assert.IsType<NotFoundObjectResult>(await c.Remover(id, default));
    }

    [Fact]
    public async Task Controller_PlanoSemCaptura_402()
    {
        var c = new TenantOabsController(Service(PlanoTipo.Free));
        _db.TenantOabs.Add(new TenantOab { Id = Guid.NewGuid(), TenantId = _tenantId, Uf = "SP", Numero = "111", Nome = "A" });
        _db.SaveChanges();
        var id = _db.TenantOabs.Single().Id;

        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Criar(new CriarTenantOabRequest(null, "SP", "12345", "A"), default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Atualizar(id, new AtualizarTenantOabRequest(null, "SP", "12345", "A", true), default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Sincronizar(id, default)).StatusCode);
    }
}
