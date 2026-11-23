using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Busca;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class BuscaControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static BuscaController Controller(AppDbContext ctx, PlanoTipo plano = PlanoTipo.Pro) =>
        new(ctx, Mock.Of<ITenantContext>(t =>
            t.TenantId == TenantId && t.UserId == Guid.NewGuid() && t.Plano == plano));

    private static AppDbContext NovoContexto() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Contato Contato(string nome, Guid? tenant = null, string? cpf = null, bool ativo = true) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant ?? TenantId, Nome = nome, CpfCnpj = cpf, Ativo = ativo,
        TipoContato = TipoContato.Cliente, CriadoEm = DateTime.UtcNow,
    };

    private static Processo Processo(string cnj, Guid? tenant = null, StatusProcesso status = StatusProcesso.Ativo) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant ?? TenantId, NumeroCNJ = cnj, Status = status, CriadoEm = DateTime.UtcNow,
    };

    private static async Task<BuscaGlobalResultadoDto> Buscar(BuscaController c, string? q, int? limite = null)
    {
        var ok = Assert.IsType<OkObjectResult>((await c.Buscar(q, limite, CancellationToken.None)).Result);
        return Assert.IsType<BuscaGlobalResultadoDto>(ok.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" a ")]
    public async Task TermoCurto_NaoBusca(string? q)
    {
        using var ctx = NovoContexto();
        ctx.Contatos.Add(Contato("Ana"));
        await ctx.SaveChangesAsync();

        Assert.Empty((await Buscar(Controller(ctx), q)).Itens);
    }

    [Fact]
    public async Task AchaProcessoPeloCnjSemPontuacaoEPeloNomeDaParte()
    {
        using var ctx = NovoContexto();
        var maria = Contato("Maria Souza");
        var processo = Processo("1234567-89.2024.8.26.0100");
        processo.TipoAcao = "Ação de cobrança";
        processo.Vara = "2ª Vara Cível";
        ctx.AddRange(maria, processo, new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = processo.Id, ContatoId = maria.Id });
        await ctx.SaveChangesAsync();
        var c = Controller(ctx);

        var porDigitos = (await Buscar(c, "12345678920248")).Itens.Single(i => i.Tipo == "processo");
        Assert.Equal(processo.Id, porDigitos.Id);
        Assert.Equal($"/pages/processo-detalhe.html?id={processo.Id}", porDigitos.Url);
        Assert.Equal("Maria Souza · Ação de cobrança · 2ª Vara Cível", porDigitos.Subtitulo);

        var porParte = await Buscar(c, "souza");
        Assert.Contains(porParte.Itens, i => i.Tipo == "processo" && i.Id == processo.Id);
        Assert.Contains(porParte.Itens, i => i.Tipo == "contato" && i.Id == maria.Id);
    }

    [Fact]
    public async Task NaoMostraDadosDeOutroEscritorio()
    {
        using var ctx = NovoContexto();
        var outro = Guid.NewGuid();
        ctx.AddRange(Contato("Carlos Outro", outro), Processo("7777777-00.2024.8.26.0001", outro));
        ctx.Tarefas.Add(new Tarefa { Id = Guid.NewGuid(), TenantId = outro, Titulo = "Carlos tarefa" });
        await ctx.SaveChangesAsync();

        Assert.Empty((await Buscar(Controller(ctx), "carlos")).Itens);
        Assert.Empty((await Buscar(Controller(ctx), "7777777")).Itens);
    }

    [Fact]
    public async Task ContatoPorCpfSemPontuacao_InativoPorUltimo()
    {
        using var ctx = NovoContexto();
        var inativo = Contato("Bruno Antigo", ativo: false);
        var ativo = Contato("Bruno Novo", cpf: "123.456.789-09");
        ctx.AddRange(inativo, ativo);
        await ctx.SaveChangesAsync();
        var c = Controller(ctx);

        var porNome = (await Buscar(c, "bruno")).Itens;
        Assert.Equal(new[] { ativo.Id, inativo.Id }, porNome.Select(i => i.Id));
        Assert.True(porNome[1].Inativo);
        Assert.Equal("Cliente · Inativo", porNome[1].Subtitulo);

        Assert.Equal(ativo.Id, (await Buscar(c, "12345678909")).Itens.Single().Id);
    }

    [Fact]
    public async Task TarefasAbertasPrimeiroEUrlAbreOModal()
    {
        using var ctx = NovoContexto();
        var concluida = new Tarefa { Id = Guid.NewGuid(), TenantId = TenantId, Titulo = "Protocolar recurso", Status = StatusTarefa.Concluida, Prazo = new DateTime(2026, 1, 1) };
        var aberta = new Tarefa { Id = Guid.NewGuid(), TenantId = TenantId, Titulo = "Protocolar petição", Status = StatusTarefa.Pendente, Tipo = TipoTarefa.Prazo, Prazo = new DateTime(2026, 12, 1, 18, 0, 0) };
        ctx.Tarefas.AddRange(concluida, aberta);
        await ctx.SaveChangesAsync();

        var itens = (await Buscar(Controller(ctx), "protocolar")).Itens;

        Assert.Equal(new[] { aberta.Id, concluida.Id }, itens.Select(i => i.Id));
        Assert.Equal($"/pages/tarefas.html?abrirId={aberta.Id}", itens[0].Url);
        Assert.Equal(new DateTime(2026, 12, 1, 18, 0, 0), itens[0].Data);
        Assert.Equal("Prazo · Pendente", itens[0].Subtitulo);
        Assert.True(itens[1].Inativo);
    }

    [Fact]
    public async Task EventoAbreODiaNaAgenda_DocumentoAbreNaPasta()
    {
        using var ctx = NovoContexto();
        var evento = new Evento { Id = Guid.NewGuid(), TenantId = TenantId, Titulo = "Audiência Silva", Tipo = TipoEvento.Audiencia, DataHora = new DateTime(2026, 12, 3, 14, 30, 0), Local = "Fórum" };
        var pasta = Guid.NewGuid();
        var doc = new Documento { Id = Guid.NewGuid(), TenantId = TenantId, Nome = "Procuração Silva.pdf", Tipo = TipoDocumento.Outro, PastaId = pasta, CriadoEm = DateTime.UtcNow };
        var docSemPasta = new Documento { Id = Guid.NewGuid(), TenantId = TenantId, Nome = "x.pdf", Observacoes = "Cópia do Silva", Tipo = TipoDocumento.Prova, CriadoEm = DateTime.UtcNow.AddDays(-1) };
        ctx.AddRange(evento, doc, docSemPasta);
        await ctx.SaveChangesAsync();

        var itens = (await Buscar(Controller(ctx), "silva")).Itens;

        var ev = itens.Single(i => i.Tipo == "evento");
        Assert.Equal($"/pages/agenda.html?view=dia&date=2026-12-03&abrirId={evento.Id}", ev.Url);
        Assert.Equal("Audiência · Fórum", ev.Subtitulo);
        var docs = itens.Where(i => i.Tipo == "documento").ToList();
        Assert.Equal($"/pages/documentos.html?doc={doc.Id}&pasta={pasta}", docs[0].Url);
        Assert.Equal($"/pages/documentos.html?doc={docSemPasta.Id}", docs[1].Url);
        Assert.Equal("Prova", docs[1].Subtitulo);
    }

    [Fact]
    public async Task ContratosSoNosPlanosComHonorarios()
    {
        using var ctx = NovoContexto();
        var cliente = Contato("Empresa Alfa");
        var contrato = new ContratoHonorario
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ContatoId = cliente.Id, NumeroContrato = "2026-001",
            Objeto = "Defesa trabalhista", DataInicio = new DateTime(2026, 1, 10),
        };
        ctx.AddRange(cliente, contrato);
        await ctx.SaveChangesAsync();

        var pro = (await Buscar(Controller(ctx, PlanoTipo.Pro), "alfa")).Itens.Single(i => i.Tipo == "contrato");
        Assert.Equal("Defesa trabalhista", pro.Titulo);
        Assert.Equal("Empresa Alfa · nº 2026-001", pro.Subtitulo);
        Assert.Equal($"/pages/honorarios-contrato-detalhe.html?id={contrato.Id}", pro.Url);

        Assert.DoesNotContain((await Buscar(Controller(ctx, PlanoTipo.Free), "alfa")).Itens, i => i.Tipo == "contrato");
    }

    [Fact]
    public async Task RespeitaOLimitePorTipo()
    {
        using var ctx = NovoContexto();
        for (var i = 0; i < 15; i++) ctx.Contatos.Add(Contato($"Cliente {i:00}"));
        await ctx.SaveChangesAsync();
        var c = Controller(ctx);

        Assert.Equal(5, (await Buscar(c, "cliente")).Itens.Count);
        Assert.Equal(2, (await Buscar(c, "cliente", 2)).Itens.Count);
        Assert.Equal(10, (await Buscar(c, "cliente", 50)).Itens.Count);
    }
}
