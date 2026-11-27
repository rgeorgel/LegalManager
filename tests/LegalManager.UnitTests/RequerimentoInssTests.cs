using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

// Fase 2 do previdenciário administrativo: dados do requerimento no INSS, prazos sugeridos,
// "Ajuizar ação" (processo de origem) e NIT do segurado.
public class RequerimentoInssTests
{
    // ── Regras de prazo (puras) ──

    private static RequerimentoInss Req(SituacaoRequerimentoInss situacao, DateTime? ciencia = null,
        int? especie = null, DateTime? dcb = null, DateTime? pericia = null) => new()
    {
        Situacao = situacao, DataCiencia = ciencia, Especie = especie, Dcb = dcb, PericiaEm = pericia,
        PericiaLocal = pericia is null ? null : "APS Centro"
    };

    private static readonly DateTime Hoje = new(2026, 3, 1);

    [Fact]
    public void Exigencia_TrintaDiasCorridosSemContarACiencia()
    {
        var s = Assert.Single(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.EmExigencia, new DateTime(2026, 3, 2)), Hoje));

        Assert.Equal(PrazosInss.ChaveExigencia, s.Chave);
        Assert.Equal("prazo", s.Tipo);
        Assert.Equal(new DateTime(2026, 4, 1, 12, 0, 0), s.Data);
        Assert.Contains("01/04/2026", s.Memoria);
    }

    [Theory]
    [InlineData(6)] // 30º dia = domingo de Páscoa (05/04)
    [InlineData(4)] // 30º dia = Sexta-feira Santa (03/04), depois sábado e domingo
    public void Indeferimento_VencimentoSemExpedientePassaParaODiaUtil(int diaCiencia)
    {
        var s = Assert.Single(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.Indeferido, new DateTime(2026, 3, diaCiencia)), Hoje));

        Assert.Equal(PrazosInss.ChaveRecurso, s.Chave);
        Assert.Equal(new DateTime(2026, 4, 6, 12, 0, 0), s.Data);
        Assert.Contains("não tem expediente", s.Memoria);
        Assert.Contains("Decreto 3.048", s.Memoria);
    }

    [Fact]
    public void Prorrogacao_SoParaAuxilioTemporarioConcedido()
    {
        var dcb = new DateTime(2026, 5, 20);

        var s = Assert.Single(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.Concedido, especie: 31, dcb: dcb), Hoje));
        Assert.Equal(PrazosInss.ChaveProrrogacao, s.Chave);
        Assert.Equal(new DateTime(2026, 5, 19, 12, 0, 0), s.Data);
        Assert.Contains("05/05/2026", s.Memoria); // janela de 15 dias

        Assert.Empty(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.Concedido, especie: 41, dcb: dcb), Hoje));
        Assert.Empty(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.EmAnalise, especie: 31, dcb: dcb), Hoje));
    }

    [Fact]
    public void Pericia_ViraEventoNaHoraMarcada()
    {
        var quando = new DateTime(2026, 3, 20, 9, 30, 0);

        var s = Assert.Single(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.EmAnalise, pericia: quando), Hoje));

        Assert.Equal("evento", s.Tipo);
        Assert.Equal(quando, s.Data);
        Assert.Contains("APS Centro", s.Memoria);
    }

    [Fact]
    public void DataLidaComoLocal_UsaODiaGravado()
    {
        // Npgsql legado devolve timestamptz como Local no fuso do servidor: 25/11 00:00 UTC
        // vira 24/11 à noite num servidor a oeste de Greenwich. O prazo conta do dia gravado.
        var gravado = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

        var s = Assert.Single(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.EmExigencia, gravado.ToLocalTime()), Hoje));

        Assert.Equal(new DateTime(2026, 4, 1, 12, 0, 0), s.Data);
        Assert.Equal(DateTimeKind.Utc, PrazosInss.Parede(gravado.ToLocalTime()).Kind);
        Assert.Equal(gravado, PrazosInss.Parede(gravado.ToLocalTime()));
    }

    [Fact]
    public void SemCienciaOuJaVencido_NaoSugere()
    {
        Assert.Empty(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.EmExigencia), Hoje));
        Assert.Empty(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.EmAnalise, new DateTime(2026, 3, 2)), Hoje));
        Assert.Empty(PrazosInss.Sugerir(Req(SituacaoRequerimentoInss.Indeferido, new DateTime(2026, 1, 5)), Hoje));
    }

    // ── Serviço ──

    private sealed record Cenario(AppDbContext Db, RequerimentoInssService Inss, ProcessoService Processos, Guid TenantId, Guid UserId);

    private static async Task<Cenario> CriarAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Free,
            Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow
        };
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Advogada", Email = "adv@teste.com",
            UserName = "adv@teste.com", Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        db.Tenants.Add(tenant);
        db.Users.Add(usuario);
        await db.SaveChangesAsync();

        var ctx = new Mock<ITenantContext>();
        ctx.Setup(t => t.TenantId).Returns(tenant.Id);
        ctx.Setup(t => t.UserId).Returns(usuario.Id);
        var inss = new RequerimentoInssService(db, ctx.Object, new TarefaService(db, ctx.Object), new EventoService(db, ctx.Object));
        var processos = new ProcessoService(db, ctx.Object, new Mock<IEscavadorService>().Object, FakeConsultaExternaLogService.Instance);
        return new Cenario(db, inss, processos, tenant.Id, usuario.Id);
    }

    private static CreateProcessoDto Administrativo(string numero = "123456789", RequerimentoInssDto? inss = null) => new(
        numero, null, null, null, AreaDireito.Previdenciario, "Aposentadoria por idade", FaseProcessual.Outro,
        StatusProcesso.Ativo, null, null, DataAjuizamento: new DateTime(2026, 2, 10),
        Tipo: TipoProcesso.Administrativo, Orgao: "INSS", Inss: inss);

    // Datas relativas a hoje: as sugestões já vencidas somem.
    private static DateTime DiasAtras(int dias) => DateTime.UtcNow.Date.AddDays(-dias);

    [Fact]
    public async Task Salvar_ProcessoJudicial_Recusa()
    {
        var c = await CriarAsync();
        var judicial = await c.Processos.CreateAsync(Administrativo("0000001-00.2024.8.26.0001") with { Tipo = TipoProcesso.Judicial, Orgao = null });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Inss.SalvarAsync(judicial.Id, new RequerimentoInssDto(Especie: 41)));
    }

    [Fact]
    public async Task Salvar_ValidaEspecieEDatas()
    {
        var c = await CriarAsync();
        var p = await c.Processos.CreateAsync(Administrativo());

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Inss.SalvarAsync(p.Id, new RequerimentoInssDto(Especie: 999)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Inss.SalvarAsync(p.Id,
            new RequerimentoInssDto(Dib: new DateTime(2026, 5, 1), Dcb: new DateTime(2026, 4, 1))));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Inss.SalvarAsync(Guid.NewGuid(), new RequerimentoInssDto()));
    }

    [Fact]
    public async Task Salvar_DevolveDadosDerESugestao_ECriarViraPrazoUmaVezSo()
    {
        var c = await CriarAsync();
        var p = await c.Processos.CreateAsync(Administrativo());

        var r = await c.Inss.SalvarAsync(p.Id, new RequerimentoInssDto(
            Especie: 41, NumeroBeneficio: " 1234567890 ", Situacao: SituacaoRequerimentoInss.Indeferido,
            DataCiencia: DiasAtras(2)));

        Assert.Equal("B41 — Aposentadoria por idade", r.EspecieNome);
        Assert.Equal("1234567890", r.NumeroBeneficio);
        Assert.Equal(new DateTime(2026, 2, 10), r.Der);
        var sugestao = Assert.Single(r.Sugestoes);

        var criado = await c.Inss.CriarSugestaoAsync(p.Id, sugestao.Chave);

        Assert.Equal("prazo", criado.Tipo);
        var tarefa = await c.Db.Tarefas.SingleAsync();
        Assert.Equal(p.Id, tarefa.ProcessoId);
        Assert.Equal(TipoTarefa.Prazo, tarefa.Tipo);
        Assert.Equal(sugestao.Data, tarefa.Prazo);
        Assert.Equal(c.UserId, tarefa.ResponsavelId);
        Assert.Empty((await c.Inss.ObterAsync(p.Id))!.Sugestoes);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Inss.CriarSugestaoAsync(p.Id, sugestao.Chave));
    }

    [Fact]
    public async Task Pericia_CriaEventoNaAgenda()
    {
        var c = await CriarAsync();
        var p = await c.Processos.CreateAsync(Administrativo());
        var quando = DateTime.UtcNow.Date.AddDays(10).AddHours(9);
        await c.Inss.SalvarAsync(p.Id, new RequerimentoInssDto(Especie: 31, PericiaEm: quando, PericiaLocal: "APS Centro"));

        var criado = await c.Inss.CriarSugestaoAsync(p.Id, PrazosInss.ChavePericia);

        var evento = await c.Db.Eventos.SingleAsync();
        Assert.Equal(criado.Id, evento.Id);
        Assert.Equal(TipoEvento.Pericia, evento.Tipo);
        Assert.Equal(quando, evento.DataHora);
        Assert.Equal("APS Centro", evento.Local);
        Assert.Empty((await c.Inss.ObterAsync(p.Id))!.Sugestoes);
    }

    [Fact]
    public async Task Obter_SemDados_DevolveNull()
    {
        var c = await CriarAsync();
        var p = await c.Processos.CreateAsync(Administrativo());

        Assert.Null(await c.Inss.ObterAsync(p.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Inss.ObterAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CriarProcesso_ComDadosDoInss_GravaELista()
    {
        var c = await CriarAsync();

        var p = await c.Processos.CreateAsync(Administrativo(inss: new RequerimentoInssDto(Especie: 88, Situacao: SituacaoRequerimentoInss.EmExigencia)));

        Assert.Equal(SituacaoRequerimentoInss.EmExigencia, p.SituacaoInss);
        Assert.Equal(88, (await c.Inss.ObterAsync(p.Id))!.Especie);
        var item = Assert.Single((await c.Processos.GetAllAsync(new ProcessoFiltroDto(null, null, null, null, null))).Items);
        Assert.Equal(SituacaoRequerimentoInss.EmExigencia, item.SituacaoInss);
    }

    [Fact]
    public async Task CriarProcesso_DadosDoInssEmJudicial_Recusa()
    {
        var c = await CriarAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Processos.CreateAsync(
            Administrativo("0000001-00.2024.8.26.0001", new RequerimentoInssDto()) with { Tipo = TipoProcesso.Judicial }));
    }

    [Fact]
    public async Task AjuizarAcao_LigaOrigemEDerivado()
    {
        var c = await CriarAsync();
        var adm = await c.Processos.CreateAsync(Administrativo());

        var acao = await c.Processos.CreateAsync(Administrativo("0000001-00.2026.4.03.6100") with
        {
            Tipo = TipoProcesso.Judicial, Orgao = null, ProcessoOrigemId = adm.Id
        });

        Assert.Equal(adm.Id, acao.Origem!.Id);
        Assert.Equal("123456789", acao.Origem.Numero);
        var derivado = Assert.Single((await c.Processos.GetByIdAsync(adm.Id))!.Derivados!);
        Assert.Equal(acao.Id, derivado.Id);
        Assert.Equal(TipoProcesso.Judicial, derivado.Tipo);
    }

    [Fact]
    public async Task AjuizarAcao_OrigemInexistente_Recusa()
    {
        var c = await CriarAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Processos.CreateAsync(
            Administrativo("0000001-00.2026.4.03.6100") with { Tipo = TipoProcesso.Judicial, Orgao = null, ProcessoOrigemId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Contato_Nit_GravaMantemEApaga()
    {
        var c = await CriarAsync();
        var ctx = new Mock<ITenantContext>();
        ctx.Setup(t => t.TenantId).Returns(c.TenantId);
        ctx.Setup(t => t.UserId).Returns(c.UserId);
        var svc = new ContatoService(c.Db, ctx.Object, new HonorarioService(c.Db, Mock.Of<IAuditService>()));

        var criado = await svc.CreateAsync(new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Segurada",
            null, null, null, null, null, null, null, null, null, null, false, null, Nit: " 123.45678.90-1 "));
        Assert.Equal("123.45678.90-1", criado.Nit);

        UpdateContatoDto Atualizacao(string? nit) => new(TipoPessoa.PF, TipoContato.Cliente, "Segurada",
            null, null, null, null, null, null, null, null, null, null, false, null, nit);
        Assert.Equal("123.45678.90-1", (await svc.UpdateAsync(criado.Id, Atualizacao(null))).Nit);
        Assert.Null((await svc.UpdateAsync(criado.Id, Atualizacao(""))).Nit);
    }
}
