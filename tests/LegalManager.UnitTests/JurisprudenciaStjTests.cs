using System.Net;
using System.Text;
using System.Text.Json;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Jurisprudencia;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LegalManager.UnitTests;

/// <summary>Leitura dos dados abertos do STJ (fixtures com registros reais) e o job que grava a base.</summary>
public class JurisprudenciaStjTests
{
    private static byte[] Fixture(string nome) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Stj", nome));

    private static readonly DateTime Agora = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    // ── Leitura ─────────────────────────────────────────────────────────────

    [Fact]
    public void LerTemas_TemasEControversias_ComSituacaoECancelamento()
    {
        var lista = StjDadosAbertos.LerTemas(Fixture("temas-amostra.csv"), Agora);

        Assert.Equal(3, lista.Count);
        var tema1 = Assert.Single(lista, p => p.Tipo == TipoPrecedente.TemaRepetitivo && p.Numero == "1");
        Assert.Equal("STJ", tema1.Tribunal);
        Assert.Equal("Trânsito em Julgado", tema1.Situacao);
        Assert.False(tema1.Cancelado);
        Assert.Equal("263", tema1.ExternoId);
        Assert.Contains("cod_tema_inicial=1&", tema1.UrlFonte);
        Assert.False(string.IsNullOrWhiteSpace(tema1.Texto));
        Assert.DoesNotContain("\n", tema1.Texto);

        var cancelado = Assert.Single(lista, p => p.Numero == "56");
        Assert.True(cancelado.Cancelado);

        var controversia = Assert.Single(lista, p => p.Tipo == TipoPrecedente.OutroQualificado);
        Assert.Equal("Controvérsia", controversia.Classe);
        Assert.All(lista, p => Assert.Equal(StjDadosAbertos.FonteTemas, p.Fonte));
    }

    [Fact]
    public void LerEspelhos_MapeiaClasseNumeroRelatorDatasELink()
    {
        var lista = StjDadosAbertos.LerEspelhos(Fixture("espelhos-amostra.json"), "stj-espelhos-quarta-turma", Agora);

        Assert.Equal(4, lista.Count);
        var p = lista[0];
        Assert.Equal(TipoPrecedente.Acordao, p.Tipo);
        Assert.Equal("AgInt no AREsp", p.Classe);
        Assert.Equal("AREsp", p.ClasseBase);
        Assert.Equal("3114647", p.Numero);
        Assert.Equal("MARIA ISABEL GALLOTTI", p.Relator);
        Assert.Equal("QUARTA TURMA", p.OrgaoJulgador);
        Assert.Equal(new DateTime(2026, 9, 29), p.DataJulgamento);
        Assert.Equal(new DateTime(2026, 10, 1), p.DataPublicacao);
        Assert.Equal("https://processo.stj.jus.br/processo/pesquisa/?num_registro=202504601381", p.UrlFonte);
        Assert.Equal("1478843", p.ExternoId);
        Assert.StartsWith("AGRAVO INTERNO", p.Texto);
        Assert.Contains(lista, x => x.Classe == "EDcl no AgInt no REsp" && x.ClasseBase == "REsp");
    }

    [Fact]
    public void LerEspelhos_IgnoraRegistrosIncompletos()
    {
        var json = """[{"id":"1","numeroProcesso":"","siglaClasse":"REsp","ementa":"x"},{"id":"2","numeroProcesso":"123","siglaClasse":"REsp","ementa":""}]""";
        Assert.Empty(StjDadosAbertos.LerEspelhos(Encoding.UTF8.GetBytes(json), "stj-espelhos-x", Agora));
    }

    [Fact]
    public void ArquivosDeEspelhos_SoJsonMensalDesdeAData()
    {
        StjDadosAbertos.Arquivo A(string nome, string formato = "JSON") => new(nome, nome, formato, $"https://x/{nome}", null);
        var arquivos = new[] { A("20220508.zip", "ZIP"), A("dicionario.csv", "CSV"), A("20250930.json"), A("20251031.json"), A("20260131.json") };

        var lista = StjDadosAbertos.ArquivosDeEspelhos(arquivos, new DateTime(2025, 10, 1));

        Assert.Equal(["20251031.json", "20260131.json"], lista.Select(x => x.Arquivo.Nome));
    }

    [Fact]
    public void ArquivoDeTemas_OMaisRecente()
    {
        var antigo = new StjDadosAbertos.Arquivo("a", "Temas.csv", "CSV", "https://x/a", new DateTime(2026, 11, 13));
        var novo = new StjDadosAbertos.Arquivo("b", "Temas.csv", "CSV", "https://x/b", new DateTime(2026, 11, 23));
        var dicionario = new StjDadosAbertos.Arquivo("c", "dicionario-temas.csv", "CSV", "https://x/c", new DateTime(2026, 12, 1));

        Assert.Equal("b", StjDadosAbertos.ArquivoDeTemas([antigo, dicionario, novo])!.Id);
    }

    [Fact]
    public void Csv_AspasVirgulasEQuebrasDeLinha()
    {
        var linhas = Csv.Ler("a,b,c\r\n1,\"x, \"\"y\"\"\nz\",3\n\n4,5,6");
        Assert.Equal(3, linhas.Count);
        Assert.Equal(["1", "x, \"y\"\nz", "3"], linhas[1]);
        Assert.Equal(["4", "5", "6"], linhas[2]);
    }

    [Fact]
    public void FonteDoPacote() =>
        Assert.Equal("stj-espelhos-quarta-turma", StjDadosAbertos.FonteDoPacote("espelhos-de-acordaos-quarta-turma"));

    // ── Job ─────────────────────────────────────────────────────────────────

    private sealed class PortalFalso : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, byte[] Corpo)> Rotas { get; } = new();
        public List<string> Pedidos { get; } = [];

        public void Pacote(string pacote, params object[] recursos) =>
            Rotas[$"package_show?id={pacote}"] = (HttpStatusCode.OK,
                JsonSerializer.SerializeToUtf8Bytes(new { success = true, result = new { resources = recursos } }));

        public void Arquivo(string url, byte[] corpo) => Rotas[url] = (HttpStatusCode.OK, corpo);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Pedidos.Add(url);
            var rota = Rotas.FirstOrDefault(r => url.EndsWith(r.Key, StringComparison.Ordinal));
            return Task.FromResult(rota.Key is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(rota.Value.Status) { Content = new ByteArrayContent(rota.Value.Corpo) });
        }
    }

    private static object Recurso(string id, string nome, string formato, string url, string modificado = "2026-11-20T10:00:00") =>
        new { id, name = nome, format = formato, url, last_modified = modificado };

    private static (AppDbContext db, JurisprudenciaStjJob job, PortalFalso portal) Montar(int meses = 120)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var portal = new PortalFalso();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jurisprudencia:MesesAcordaos"] = meses.ToString(),
        }).Build();
        var job = new JurisprudenciaStjJob(db, new StjDadosAbertos(new HttpClient(portal)), config, NullLogger<JurisprudenciaStjJob>.Instance);
        return (db, job, portal);
    }

    private static void PortalComDados(PortalFalso portal, string mesEspelho = "20261031")
    {
        portal.Pacote(StjDadosAbertos.PacoteTemas, Recurso("t1", "Temas.csv", "CSV", "https://stj/temas.csv"));
        portal.Arquivo("https://stj/temas.csv", Fixture("temas-amostra.csv"));
        portal.Pacote("espelhos-de-acordaos-quarta-turma",
            Recurso("e1", $"{mesEspelho}.json", "JSON", "https://stj/4t.json"),
            Recurso("z", "20220508.zip", "ZIP", "https://stj/historico.zip"));
        portal.Arquivo("https://stj/4t.json", Fixture("espelhos-amostra.json"));
        // Os demais órgãos ainda sem arquivos.
        foreach (var p in StjDadosAbertos.PacotesEspelhos.Where(p => p != "espelhos-de-acordaos-quarta-turma"))
            portal.Pacote(p);
    }

    [Fact]
    public async Task Executar_GravaTemasEAcordaos_MarcaListaCompleta()
    {
        var (db, job, portal) = Montar();
        PortalComDados(portal, DateTime.UtcNow.AddMonths(-1).ToString("yyyyMM") + "28");

        await job.ExecutarAsync();

        Assert.Equal(3, await db.Precedentes.CountAsync(p => p.Fonte == StjDadosAbertos.FonteTemas));
        Assert.Equal(4, await db.Precedentes.CountAsync(p => p.Fonte == "stj-espelhos-quarta-turma"));
        var temas = await db.FontesPrecedentes.FindAsync(StjDadosAbertos.FonteTemas);
        Assert.True(temas!.ListaCompleta);
        Assert.Equal(3, temas.Total);
        var quarta = await db.FontesPrecedentes.FindAsync("stj-espelhos-quarta-turma");
        Assert.Equal(4, quarta!.Total);
        Assert.False(quarta.ListaCompleta);
        Assert.DoesNotContain(portal.Pedidos, u => u.EndsWith(".zip"));
        Assert.False(await JurisprudenciaStjJob.PrecisaCargaInicialAsync(db));
    }

    [Fact]
    public async Task ExecutarDuasVezes_NaoDuplicaNemBaixaDeNovoOMesmoArquivo()
    {
        var (db, job, portal) = Montar();
        PortalComDados(portal, DateTime.UtcNow.AddMonths(-1).ToString("yyyyMM") + "28");

        await job.ExecutarAsync();
        await job.ExecutarAsync();

        Assert.Equal(7, await db.Precedentes.CountAsync());
        Assert.Equal(1, portal.Pedidos.Count(u => u == "https://stj/4t.json"));
        // Temas são recarregados sempre (lista completa e pode mudar de situação).
        Assert.Equal(2, portal.Pedidos.Count(u => u == "https://stj/temas.csv"));
    }

    [Fact]
    public async Task ArquivoRepublicado_EhLidoDeNovoEAtualizado()
    {
        var (db, job, portal) = Montar();
        var mes = DateTime.UtcNow.AddMonths(-1).ToString("yyyyMM") + "28";
        PortalComDados(portal, mes);
        await job.ExecutarAsync();

        var p = await db.Precedentes.FirstAsync(x => x.ExternoId == "1478843");
        p.Texto = "alterado";
        await db.SaveChangesAsync();
        portal.Pacote("espelhos-de-acordaos-quarta-turma", Recurso("e1", $"{mes}.json", "JSON", "https://stj/4t.json", "2026-11-22T10:00:00"));

        await job.ImportarEspelhosAsync();

        db.ChangeTracker.Clear();
        Assert.StartsWith("AGRAVO INTERNO", (await db.Precedentes.FirstAsync(x => x.ExternoId == "1478843")).Texto);
        Assert.Equal(4, await db.Precedentes.CountAsync(x => x.Fonte == "stj-espelhos-quarta-turma"));
    }

    [Fact]
    public async Task MesesAntigos_NaoSaoImportados()
    {
        var (db, job, portal) = Montar(meses: 3);
        PortalComDados(portal, "20200131");

        await job.ImportarEspelhosAsync();

        Assert.Equal(0, await db.Precedentes.CountAsync());
        Assert.DoesNotContain(portal.Pedidos, u => u == "https://stj/4t.json");
    }

    [Fact]
    public async Task FalhaNoPortal_RegistraErroESegue()
    {
        var (db, job, portal) = Montar();
        // Nada cadastrado: package_show responde 404 para tudo.

        await job.ExecutarAsync();

        var temas = await db.FontesPrecedentes.FindAsync(StjDadosAbertos.FonteTemas);
        Assert.False(string.IsNullOrEmpty(temas!.UltimoErro));
        Assert.Equal(StjDadosAbertos.PacotesEspelhos.Count + 1, await db.FontesPrecedentes.CountAsync(f => f.UltimoErro != null));
        Assert.True(await JurisprudenciaStjJob.PrecisaCargaInicialAsync(db));
    }

    [Fact]
    public void Habilitado_SoComAConfiguracaoLigada()
    {
        Assert.False(JurisprudenciaStjJob.Habilitado(new ConfigurationBuilder().Build()));
        Assert.True(JurisprudenciaStjJob.Habilitado(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [JurisprudenciaStjJob.ChaveConfigHabilitado] = "true" }).Build()));
    }
}
