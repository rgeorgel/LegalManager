using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.UnitTests;

public class AndamentoDePublicacaoTests
{
    private const string Cnj = "1009210-06.2023.8.26.0664";
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _tenantId = Guid.NewGuid();

    private Processo Processo(Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId ?? _tenantId, NumeroCNJ = Cnj, AreaDireito = AreaDireito.Civil,
        Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
    };

    private Publicacao Pub(string uuid, Guid? processoId, string texto, DateTime data, TipoPublicacao tipo = TipoPublicacao.Despacho) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProcessoId = processoId, NumeroCNJ = Cnj, Diario = "DJSP",
        DataPublicacao = data, Conteudo = texto, Tipo = tipo, FonteCaptura = FonteCaptura.Escavador,
        UuidExterno = uuid, CapturaEm = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task CriaAsFaltantes_UmaVezSo()
    {
        var processo = Processo();
        _db.Processos.Add(processo);
        var daOab = Pub("oab-1", null, "Vista ao MP.", new DateTime(2026, 11, 11));
        var daBusca = Pub("busca-7", processo.Id, "VISTOS. Fls.", new DateTime(2026, 11, 10));
        // A do monitoramento do processo já criou a sua movimentação no webhook.
        var doProcesso = Pub("v2-mov-1", processo.Id, "Juntada de petição", new DateTime(2026, 11, 9));
        _db.Publicacoes.AddRange(daOab, daBusca, doProcesso);
        await _db.SaveChangesAsync();

        Assert.Equal(2, await AndamentoDePublicacao.CriarFaltantesAsync(_db));

        Assert.Equal(processo.Id, daOab.ProcessoId);
        var andamentos = await _db.Andamentos.OrderBy(a => a.Data).ToListAsync();
        Assert.Equal(["VISTOS. Fls.", "Vista ao MP."], andamentos.Select(a => a.Descricao));
        Assert.All(andamentos, a => Assert.Equal(processo.Id, a.ProcessoId));
        Assert.Equal(daOab.CapturaEm, andamentos[1].CriadoEm);
        Assert.Equal(new DateTime(2026, 11, 11), processo.UltimoAndamentoEm);

        Assert.Equal(0, await AndamentoDePublicacao.CriarFaltantesAsync(_db));
        Assert.Equal(2, await _db.Andamentos.CountAsync());
    }

    [Fact]
    public async Task SemCnj_PreencheDoTextoEVincula_UmaVezSo()
    {
        var processo = Processo();
        _db.Processos.Add(processo);
        var semCnj = Pub("v2-mov-9", null, $"ATOrd {Cnj} AUTOR: FULANO INTIMAÇÃO", new DateTime(2026, 11, 18));
        semCnj.NumeroCNJ = null;
        var outroNumero = Pub("v2-mov-10", null, "Processo 0000001-11.2020.8.26.0100. Intimem-se.", new DateTime(2026, 11, 18));
        outroNumero.NumeroCNJ = null;
        var semNumeroNoTexto = Pub("v2-mov-11", null, "Intimem-se as partes.", new DateTime(2026, 11, 18));
        semNumeroNoTexto.NumeroCNJ = null;
        _db.Publicacoes.AddRange(semCnj, outroNumero, semNumeroNoTexto);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await AndamentoDePublicacao.CriarFaltantesAsync(_db));

        Assert.Equal(Cnj, semCnj.NumeroCNJ);
        Assert.Equal(processo.Id, semCnj.ProcessoId);
        Assert.Equal(processo.Id, (await _db.Andamentos.SingleAsync()).ProcessoId);
        // Sem processo no escritório: ganha o número (o card mostra), sem vínculo.
        Assert.Equal("0000001-11.2020.8.26.0100", outroNumero.NumeroCNJ);
        Assert.Null(outroNumero.ProcessoId);
        Assert.Null(semNumeroNoTexto.NumeroCNJ);

        Assert.Equal(0, await AndamentoDePublicacao.CriarFaltantesAsync(_db));
        Assert.Equal(1, await _db.Andamentos.CountAsync());
    }

    [Fact]
    public void CnjNoTexto_AchaOsNumerosComMascaraSemRepetir()
    {
        Assert.Equal(["0010336-32.2026.5.15.0027", Cnj],
            CnjNoTexto.Todos($"ATOrd 0010336-32.2026.5.15.0027 e {Cnj}; de novo 0010336-32.2026.5.15.0027"));
        Assert.Null(CnjNoTexto.Primeiro("10010336-32.2026.5.15.0027 tem dígito a mais"));
        Assert.Empty(CnjNoTexto.Todos(null));
    }

    [Fact]
    public async Task ProcessoDeOutroEscritorio_NaoVincula()
    {
        _db.Processos.Add(Processo(Guid.NewGuid()));
        _db.Publicacoes.Add(Pub("oab-1", null, "Vista ao MP.", new DateTime(2026, 11, 11)));
        await _db.SaveChangesAsync();

        Assert.Equal(0, await AndamentoDePublicacao.CriarFaltantesAsync(_db));
        Assert.Empty(_db.Andamentos);
    }

    [Theory]
    [InlineData(TipoPublicacao.Decisao, TipoAndamento.Decisao)]
    [InlineData(TipoPublicacao.Despacho, TipoAndamento.Despacho)]
    [InlineData(TipoPublicacao.Intimacao, TipoAndamento.Intimacao)]
    [InlineData(TipoPublicacao.Audiencia, TipoAndamento.Audiencia)]
    [InlineData(TipoPublicacao.Prazo, TipoAndamento.Publicacao)]
    [InlineData(TipoPublicacao.Outro, TipoAndamento.Publicacao)]
    public void Tipo(TipoPublicacao publicacao, TipoAndamento andamento) =>
        Assert.Equal(andamento, AndamentoDePublicacao.Tipo(publicacao));

    [Fact]
    public void Descricao_SemConteudo_UsaTrechoOuPadrao()
    {
        Assert.Equal("trecho", AndamentoDePublicacao.Descricao(new Publicacao { Conteudo = "", Snippet = "trecho" }));
        Assert.Equal("Publicação no diário oficial", AndamentoDePublicacao.Descricao(new Publicacao { Conteudo = " " }));
    }
}
