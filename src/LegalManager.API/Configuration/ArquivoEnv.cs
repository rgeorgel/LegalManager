using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace LegalManager.API.Configuration;

/// <summary>
/// Lê o <c>.env</c> da raiz do repositório quando a API roda com <c>dotnet run</c> (Development) —
/// o docker-compose já o lê sozinho, mas o ASP.NET não.
///
/// - Nomes do docker-compose (<c>DB_CONNECTION_STRING</c>, <c>HANGFIRE_DESABILITADO</c>) são traduzidos para a chave de
///   configuração (<see cref="Mapeamento"/>); nomes no formato do ASP.NET (<c>Secao__Chave</c>)
///   entram direto. O resto do arquivo é ignorado — <c>ASPNETCORE_ENVIRONMENT=Production</c> no
///   .env não muda o ambiente local.
/// - Não altera as variáveis de ambiente do processo: os valores entram como fonte de configuração
///   logo antes das variáveis de ambiente, então <c>export X=...</c> no shell continua valendo mais.
/// </summary>
public static class ArquivoEnv
{
    public const string NomeArquivo = ".env";

    private static readonly Dictionary<string, string> Mapeamento = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DB_CONNECTION_STRING"] = "ConnectionStrings:DefaultConnection",
        ["HANGFIRE_DESABILITADO"] = "Hangfire:Desabilitado",
    };

    /// <summary>Adiciona o .env encontrado a partir de <paramref name="inicio"/> (subindo até a raiz do repositório).</summary>
    public static string? Adicionar(ConfigurationManager config, string inicio)
    {
        var caminho = Localizar(inicio);
        if (caminho is null) return null;

        var valores = Ler(File.ReadAllLines(caminho));
        if (valores.Count == 0) return caminho;

        var fonte = new MemoryConfigurationSource { InitialData = valores };
        var indiceEnv = config.Sources
            .Select((s, i) => (s, i))
            .Where(x => x.s is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            .Select(x => (int?)x.i)
            .FirstOrDefault();

        if (indiceEnv is int i) config.Sources.Insert(i, fonte);
        else config.Sources.Add(fonte);

        return caminho;
    }

    /// <summary>Procura o .env em <paramref name="inicio"/> e nos diretórios acima, parando na raiz do repositório (.git).</summary>
    public static string? Localizar(string inicio)
    {
        for (var dir = new DirectoryInfo(inicio); dir is not null; dir = dir.Parent)
        {
            var caminho = Path.Combine(dir.FullName, NomeArquivo);
            if (File.Exists(caminho)) return caminho;
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return null;
        }
        return null;
    }

    /// <summary>Interpreta as linhas do .env e devolve só as chaves que a API entende, já como chaves de configuração.</summary>
    public static Dictionary<string, string?> Ler(IEnumerable<string> linhas)
    {
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var bruta in linhas)
        {
            var linha = bruta.Trim();
            if (linha.Length == 0 || linha.StartsWith('#')) continue;
            if (linha.StartsWith("export ", StringComparison.Ordinal)) linha = linha[7..].TrimStart();

            var igual = linha.IndexOf('=');
            if (igual <= 0) continue;

            var nome = linha[..igual].Trim();
            var chave = Mapeamento.TryGetValue(nome, out var mapeada) ? mapeada
                : nome.Contains("__", StringComparison.Ordinal) ? nome.Replace("__", ":", StringComparison.Ordinal)
                : null;
            if (chave is null) continue;

            var valor = Valor(linha[(igual + 1)..].Trim());
            if (valor.Length > 0) valores[chave] = valor;
        }

        return valores;
    }

    // Entre aspas: o conteúdo vale literalmente (inclusive '#'). Sem aspas: " # ..." é comentário.
    private static string Valor(string texto)
    {
        if (texto.Length >= 2 && (texto[0] == '"' || texto[0] == '\'') && texto[^1] == texto[0])
            return texto[1..^1];

        for (var i = 1; i < texto.Length; i++)
            if (texto[i] == '#' && char.IsWhiteSpace(texto[i - 1]))
                return texto[..i].TrimEnd();

        return texto;
    }
}
