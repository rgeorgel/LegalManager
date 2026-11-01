using LegalManager.API.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace LegalManager.UnitTests;

/// <summary>.env lido pelo dotnet run: nomes do docker-compose mapeados, aspas/comentários, precedência das variáveis de ambiente.</summary>
public class ArquivoEnvTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "arquivoenv-" + Guid.NewGuid().ToString("N"));

    public ArquivoEnvTests() => Directory.CreateDirectory(_raiz);

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    [Fact]
    public void Ler_MapeiaDbConnectionString_EIgnoraNomesDesconhecidos()
    {
        var valores = ArquivoEnv.Ler(
        [
            "# comentário",
            "",
            "ASPNETCORE_ENVIRONMENT=Production",
            "POSTGRES_PASSWORD=x",
            "DB_CONNECTION_STRING=Host=ep-x.neon.tech;Database=causify;Username=u;Password=p;SSL Mode=Require",
        ]);

        Assert.Single(valores);
        Assert.Equal("Host=ep-x.neon.tech;Database=causify;Username=u;Password=p;SSL Mode=Require",
            valores["ConnectionStrings:DefaultConnection"]);
    }

    [Fact]
    public void Ler_MapeiaHangfireDesabilitado()
    {
        Assert.Equal("true", ArquivoEnv.Ler(["HANGFIRE_DESABILITADO=true"])["Hangfire:Desabilitado"]);
    }

    [Fact]
    public void Ler_AceitaChavesNoFormatoAspNet_EExport()
    {
        var valores = ArquivoEnv.Ler(["export Escavador__UseMock=true", "Jwt__Key = abc "]);

        Assert.Equal("true", valores["Escavador:UseMock"]);
        Assert.Equal("abc", valores["Jwt:Key"]);
    }

    [Theory]
    [InlineData("Jwt__Key=abc   # comentário", "abc")]
    [InlineData("Jwt__Key=a#b", "a#b")]
    [InlineData("Jwt__Key=\"a #b\"", "a #b")]
    [InlineData("Jwt__Key='x;y # z'", "x;y # z")]
    public void Ler_TrataAspasEComentarioNaLinha(string linha, string esperado)
    {
        Assert.Equal(esperado, ArquivoEnv.Ler([linha])["Jwt:Key"]);
    }

    [Theory]
    [InlineData("DB_CONNECTION_STRING=")]
    [InlineData("=valor")]
    [InlineData("sem igual")]
    public void Ler_IgnoraLinhasVaziasOuInvalidas(string linha)
    {
        Assert.Empty(ArquivoEnv.Ler([linha]));
    }

    [Fact]
    public void Localizar_SobeAteEncontrar_EParaNaRaizDoRepositorio()
    {
        var projeto = Directory.CreateDirectory(Path.Combine(_raiz, "repo", "src", "Api")).FullName;
        Directory.CreateDirectory(Path.Combine(_raiz, "repo", ".git"));

        Assert.Null(ArquivoEnv.Localizar(projeto));

        // .env acima da raiz do repositório não é usado
        File.WriteAllText(Path.Combine(_raiz, ".env"), "DB_CONNECTION_STRING=fora");
        Assert.Null(ArquivoEnv.Localizar(projeto));

        var env = Path.Combine(_raiz, "repo", ".env");
        File.WriteAllText(env, "DB_CONNECTION_STRING=dentro");
        Assert.Equal(env, ArquivoEnv.Localizar(projeto));
    }

    [Fact]
    public void Adicionar_SobrepoeAppSettings_MasFicaAntesDasVariaveisDeAmbiente()
    {
        File.WriteAllText(Path.Combine(_raiz, ".env"), "DB_CONNECTION_STRING=Host=neon");
        Directory.CreateDirectory(Path.Combine(_raiz, ".git"));
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost",
        });
        config.AddEnvironmentVariables();

        var caminho = ArquivoEnv.Adicionar(config, _raiz);

        Assert.Equal(Path.Combine(_raiz, ".env"), caminho);
        Assert.Equal("Host=neon", config.GetConnectionString("DefaultConnection"));
        Assert.IsType<EnvironmentVariablesConfigurationSource>(config.Sources[^1]);
    }

    [Fact]
    public void Adicionar_SemArquivo_NaoMudaAConfiguracao()
    {
        Directory.CreateDirectory(Path.Combine(_raiz, ".git"));
        var config = new ConfigurationManager();
        var fontes = config.Sources.Count;

        Assert.Null(ArquivoEnv.Adicionar(config, _raiz));
        Assert.Equal(fontes, config.Sources.Count);
    }

    [Fact]
    public void Adicionar_SemFonteDeVariaveisDeAmbiente_AdicionaNoFim()
    {
        File.WriteAllText(Path.Combine(_raiz, ".env"), "Jwt__Key=abc\nPOSTGRES_DB=x");
        Directory.CreateDirectory(Path.Combine(_raiz, ".git"));
        var config = new ConfigurationManager();

        ArquivoEnv.Adicionar(config, _raiz);

        Assert.Equal("abc", config["Jwt:Key"]);
    }
}
