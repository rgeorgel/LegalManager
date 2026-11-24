using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LegalManager.Domain.Entities;

namespace LegalManager.Infrastructure.Jurisprudencia;

/// <summary>
/// Portal de Dados Abertos do STJ (CKAN, dadosabertos.web.stj.jus.br) — uso livre, inclusive comercial.
/// Lê os temas de recursos repetitivos e demais precedentes qualificados (CSV, lista completa) e os
/// espelhos de acórdãos de cada órgão julgador (um JSON por mês). Só leitura e tradução para
/// <see cref="Precedente"/>; quem grava é o <see cref="Jobs.JurisprudenciaStjJob"/>.
/// </summary>
public class StjDadosAbertos(HttpClient http)
{
    public const string BaseUrl = "https://dadosabertos.web.stj.jus.br/";
    public const string FonteTemas = "stj-temas";
    public const string PrefixoFonteEspelhos = "stj-espelhos-";
    public const string PacoteTemas = "precedentes-qualificados";

    /// <summary>Um pacote de espelhos por órgão julgador.</summary>
    public static readonly IReadOnlyList<string> PacotesEspelhos =
    [
        "espelhos-de-acordaos-corte-especial",
        "espelhos-de-acordaos-primeira-secao",
        "espelhos-de-acordaos-segunda-secao",
        "espelhos-de-acordaos-terceira-secao",
        "espelhos-de-acordaos-primeira-turma",
        "espelhos-de-acordaos-segunda-turma",
        "espelhos-de-acordaos-terceira-turma",
        "espelhos-de-acordaos-quarta-turma",
        "espelhos-de-acordaos-quinta-turma",
        "espelhos-de-acordaos-sexta-turma",
    ];

    public sealed record Arquivo(string Id, string Nome, string Formato, string Url, DateTime? Modificado)
    {
        /// <summary>Chave de controle: muda se o STJ republicar o arquivo.</summary>
        public string Chave => $"{Id}|{Modificado:yyyyMMddHHmmss}";
    }

    private const int TamanhoMaximoTexto = 8000;

    public async Task<IReadOnlyList<Arquivo>> ListarArquivosAsync(string pacote, CancellationToken ct = default)
    {
        var resposta = await http.GetFromJsonAsync<CkanResposta>(
            $"{BaseUrl}api/3/action/package_show?id={Uri.EscapeDataString(pacote)}", ct);
        return resposta?.Result?.Resources?
            .Where(r => !string.IsNullOrEmpty(r.Url))
            .Select(r => new Arquivo(r.Id ?? "", r.Name ?? "", (r.Format ?? "").ToUpperInvariant(), r.Url!,
                DateTime.TryParse(r.LastModified ?? r.Created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null))
            .ToList() ?? [];
    }

    public async Task<byte[]> BaixarAsync(string url, CancellationToken ct = default) =>
        await http.GetByteArrayAsync(url, ct);

    /// <summary>O CSV de temas mais recente do pacote (o portal guarda versões antigas também).</summary>
    public static Arquivo? ArquivoDeTemas(IEnumerable<Arquivo> arquivos) =>
        arquivos
            .Where(a => a.Formato == "CSV" && a.Nome.StartsWith("temas", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Modificado)
            .FirstOrDefault();

    /// <summary>Os JSON mensais de espelhos ("20261031.json") a partir de <paramref name="desde"/>.</summary>
    public static IReadOnlyList<(Arquivo Arquivo, DateTime Mes)> ArquivosDeEspelhos(IEnumerable<Arquivo> arquivos, DateTime desde) =>
        arquivos
            .Where(a => a.Formato == "JSON")
            .Select(a => (a, m: Regex.Match(a.Nome, @"^(\d{8})\.json$", RegexOptions.IgnoreCase)))
            .Where(x => x.m.Success)
            .Select(x => (x.a, data: DateTime.ParseExact(x.m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture)))
            .Where(x => x.data >= desde)
            .OrderBy(x => x.data)
            .Select(x => (x.a, x.data))
            .ToList();

    // ── Temas e precedentes qualificados (CSV) ──────────────────────────────

    public static List<Precedente> LerTemas(byte[] csv, DateTime agora)
    {
        var linhas = Csv.Ler(Encoding.UTF8.GetString(csv).TrimStart('﻿'));
        if (linhas.Count < 2) return [];
        var cab = linhas[0].Select((h, i) => (h.Trim(), i)).ToDictionary(x => x.Item1, x => x.i, StringComparer.OrdinalIgnoreCase);
        string Campo(List<string> l, string nome) => cab.TryGetValue(nome, out var i) && i < l.Count ? l[i].Trim() : "";

        var lista = new List<Precedente>();
        foreach (var l in linhas.Skip(1))
        {
            var seq = Campo(l, "sequencialPrecedente");
            var numero = ExtratorCitacoes.SoDigitos(Campo(l, "numeroPrecedente"));
            if (seq.Length == 0 || numero.Length == 0) continue;

            var tipo = Campo(l, "tipoPrecedente");
            var ehTema = tipo.Length == 0 || tipo.Equals("Tema", StringComparison.OrdinalIgnoreCase);
            var situacao = Limpar(Campo(l, "situacao"));
            var tese = Limpar(Campo(l, "teseFirmada"));
            var questao = Limpar(Campo(l, "questaoSubmetidaAJulgamento"));
            var texto = tese.Length > 0 ? tese : questao.Length > 0 ? $"Questão submetida a julgamento: {questao}" : "";
            if (texto.Length == 0) continue;

            lista.Add(new Precedente
            {
                Id = Guid.NewGuid(),
                Tipo = ehTema ? TipoPrecedente.TemaRepetitivo : TipoPrecedente.OutroQualificado,
                Tribunal = "STJ",
                Classe = ehTema ? null : tipo,
                Numero = numero,
                OrgaoJulgador = OrgaoPorSigla(Campo(l, "orgaoJulgador")),
                DataJulgamento = Data(Campo(l, "dataJulgamento")),
                DataPublicacao = Data(Campo(l, "dataPublicacaoAcordao")),
                Texto = Cortar(texto),
                Situacao = situacao.Length == 0 ? null : situacao,
                Cancelado = situacao.Contains("cancelad", StringComparison.OrdinalIgnoreCase),
                UrlFonte = ehTema
                    ? $"https://processo.stj.jus.br/repetitivos/temas_repetitivos/pesquisa.jsp?novaConsulta=true&tipo_pesquisa=T&cod_tema_inicial={numero}&cod_tema_final={numero}"
                    : $"{BaseUrl}dataset/{PacoteTemas}",
                Fonte = FonteTemas,
                ExternoId = seq,
                ImportadoEm = agora,
            });
        }
        return lista;
    }

    // ── Espelhos de acórdãos (JSON) ─────────────────────────────────────────

    public static List<Precedente> LerEspelhos(byte[] json, string fonte, DateTime agora)
    {
        var itens = JsonSerializer.Deserialize<List<Espelho>>(json) ?? [];
        var lista = new List<Precedente>(itens.Count);
        foreach (var e in itens)
        {
            var numero = ExtratorCitacoes.SoDigitos(e.NumeroProcesso ?? "");
            var ementa = Limpar(e.Ementa ?? "");
            if (string.IsNullOrWhiteSpace(e.Id) || numero.Length == 0 || ementa.Length == 0 || string.IsNullOrWhiteSpace(e.SiglaClasse))
                continue;
            var classe = ExtratorCitacoes.NormalizarCadeia(e.SiglaClasse);
            lista.Add(new Precedente
            {
                Id = Guid.NewGuid(),
                Tipo = TipoPrecedente.Acordao,
                Tribunal = "STJ",
                Classe = Cortar(classe, 100),
                ClasseBase = ExtratorCitacoes.ClasseBase(classe) ?? Cortar(classe.Split(' ')[^1], 20),
                Numero = Cortar(numero, 20),
                Relator = Cortar(e.MinistroRelator?.Trim(), 200),
                OrgaoJulgador = Cortar(e.NomeOrgaoJulgador?.Trim(), 100),
                DataJulgamento = DateTime.TryParseExact(e.DataDecisao, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                DataPublicacao = DataPublicacao(e.DataPublicacao),
                Texto = Cortar(ementa),
                UrlFonte = string.IsNullOrWhiteSpace(e.NumeroRegistro)
                    ? $"{BaseUrl}dataset/{fonte[PrefixoFonteEspelhos.Length..]}"
                    : $"https://processo.stj.jus.br/processo/pesquisa/?num_registro={Uri.EscapeDataString(e.NumeroRegistro.Trim())}",
                Fonte = fonte,
                ExternoId = e.Id.Trim(),
                ImportadoEm = agora,
            });
        }
        return lista;
    }

    /// <summary>"espelhos-de-acordaos-quarta-turma" → "stj-espelhos-quarta-turma".</summary>
    public static string FonteDoPacote(string pacote) => PrefixoFonteEspelhos + pacote.Replace("espelhos-de-acordaos-", "");

    private static string OrgaoPorSigla(string sigla) => sigla.ToUpperInvariant() switch
    {
        "S1" => "PRIMEIRA SEÇÃO", "S2" => "SEGUNDA SEÇÃO", "S3" => "TERCEIRA SEÇÃO", "CE" => "CORTE ESPECIAL",
        _ => sigla,
    };

    private static DateTime? Data(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static DateTime? DataPublicacao(string? s)
    {
        var m = Regex.Match(s ?? "", @"(\d{2}/\d{2}/\d{4})");
        return m.Success && DateTime.TryParseExact(m.Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>Junta as quebras de linha da fonte e tira espaços duplos e não separáveis.</summary>
    private static string Limpar(string s) => Regex.Replace(s.Replace(' ', ' '), @"\s+", " ").Trim();

    private static string Cortar(string s) => s.Length <= TamanhoMaximoTexto ? s : s[..TamanhoMaximoTexto];

    private static string? Cortar(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    private sealed class CkanResposta
    {
        [JsonPropertyName("result")] public CkanPacote? Result { get; set; }
    }

    private sealed class CkanPacote
    {
        [JsonPropertyName("resources")] public List<CkanRecurso>? Resources { get; set; }
    }

    private sealed class CkanRecurso
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("format")] public string? Format { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("last_modified")] public string? LastModified { get; set; }
        [JsonPropertyName("created")] public string? Created { get; set; }
    }

    private sealed class Espelho
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("numeroProcesso")] public string? NumeroProcesso { get; set; }
        [JsonPropertyName("numeroRegistro")] public string? NumeroRegistro { get; set; }
        [JsonPropertyName("siglaClasse")] public string? SiglaClasse { get; set; }
        [JsonPropertyName("nomeOrgaoJulgador")] public string? NomeOrgaoJulgador { get; set; }
        [JsonPropertyName("ministroRelator")] public string? MinistroRelator { get; set; }
        [JsonPropertyName("dataPublicacao")] public string? DataPublicacao { get; set; }
        [JsonPropertyName("ementa")] public string? Ementa { get; set; }
        [JsonPropertyName("dataDecisao")] public string? DataDecisao { get; set; }
    }
}

/// <summary>Leitor de CSV (RFC 4180): vírgula, aspas e quebras de linha dentro de aspas.</summary>
public static class Csv
{
    public static List<List<string>> Ler(string texto, char separador = ',')
    {
        var linhas = new List<List<string>>();
        var linha = new List<string>();
        var campo = new StringBuilder();
        var aspas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (aspas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                    else aspas = false;
                }
                else campo.Append(c);
            }
            else if (c == '"') aspas = true;
            else if (c == separador) { linha.Add(campo.ToString()); campo.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                linha.Add(campo.ToString()); campo.Clear();
                if (linha.Count > 1 || linha[0].Length > 0) linhas.Add(linha);
                linha = [];
            }
            else campo.Append(c);
        }
        if (campo.Length > 0 || linha.Count > 0)
        {
            linha.Add(campo.ToString());
            linhas.Add(linha);
        }
        return linhas;
    }
}
