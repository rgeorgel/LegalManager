using System.Globalization;
using System.Text;

namespace LegalManager.Infrastructure.Agenda;

/// <summary>
/// Um item do calendário. Com <see cref="DiaInteiro"/>, <see cref="Inicio"/> e <see cref="Fim"/>
/// são só datas (fim exclusivo, como no iCal); sem, são instantes em UTC real.
/// </summary>
public sealed record IcsEvento(
    string Uid,
    string Titulo,
    DateTime Inicio,
    DateTime Fim,
    bool DiaInteiro,
    DateTime ModificadoEmUtc,
    string? Descricao = null,
    string? Local = null,
    string? Url = null,
    string? Categoria = null,
    TimeSpan? AlarmeAntes = null);

/// <summary>
/// Gera um VCALENDAR (RFC 5545) para assinatura por URL: escape de texto, dobra de linhas em
/// 75 octetos e CRLF. Os horários vão em UTC ("Z"), então não é preciso bloco VTIMEZONE.
/// </summary>
public sealed class IcsBuilder
{
    private const int MaxOctetos = 75;
    private readonly StringBuilder _sb = new();
    private bool _concluido;

    public IcsBuilder(string nomeCalendario, string? fusoIana, TimeSpan? intervaloAtualizacao = null)
    {
        Linha("BEGIN:VCALENDAR");
        Linha("VERSION:2.0");
        Linha("PRODID:-//Causify//Agenda//PT-BR");
        Linha("CALSCALE:GREGORIAN");
        Linha("METHOD:PUBLISH");
        Linha("X-WR-CALNAME:" + Escapar(nomeCalendario));
        if (!string.IsNullOrWhiteSpace(fusoIana)) Linha("X-WR-TIMEZONE:" + Escapar(fusoIana));
        if (intervaloAtualizacao is { } intervalo)
        {
            // Apple e Outlook respeitam; o Google ignora e busca no ritmo dele.
            Linha("REFRESH-INTERVAL;VALUE=DURATION:" + Duracao(intervalo));
            Linha("X-PUBLISHED-TTL:" + Duracao(intervalo));
        }
    }

    public IcsBuilder Adicionar(IcsEvento e)
    {
        if (_concluido) throw new InvalidOperationException("Calendário já concluído.");

        Linha("BEGIN:VEVENT");
        Linha("UID:" + Escapar(e.Uid));
        // DTSTAMP = última modificação (e não "agora"): o arquivo só muda quando algo muda, e o
        // ETag do feed pode responder 304.
        Linha("DTSTAMP:" + Utc(e.ModificadoEmUtc));
        Linha("LAST-MODIFIED:" + Utc(e.ModificadoEmUtc));
        if (e.DiaInteiro)
        {
            Linha("DTSTART;VALUE=DATE:" + Data(e.Inicio));
            Linha("DTEND;VALUE=DATE:" + Data(e.Fim));
            Linha("TRANSP:TRANSPARENT"); // prazo de dia inteiro não bloqueia a agenda
        }
        else
        {
            Linha("DTSTART:" + Utc(e.Inicio));
            Linha("DTEND:" + Utc(e.Fim));
        }
        Linha("SUMMARY:" + Escapar(e.Titulo));
        if (!string.IsNullOrWhiteSpace(e.Descricao)) Linha("DESCRIPTION:" + Escapar(e.Descricao));
        if (!string.IsNullOrWhiteSpace(e.Local)) Linha("LOCATION:" + Escapar(e.Local));
        if (!string.IsNullOrWhiteSpace(e.Url)) Linha("URL:" + e.Url);
        if (!string.IsNullOrWhiteSpace(e.Categoria)) Linha("CATEGORIES:" + Escapar(e.Categoria));
        Linha("STATUS:CONFIRMED");
        if (e.AlarmeAntes is { } antes)
        {
            Linha("BEGIN:VALARM");
            Linha("ACTION:DISPLAY");
            Linha("DESCRIPTION:" + Escapar(e.Titulo));
            Linha("TRIGGER:-" + Duracao(antes));
            Linha("END:VALARM");
        }
        Linha("END:VEVENT");
        return this;
    }

    public string Concluir()
    {
        if (!_concluido)
        {
            Linha("END:VCALENDAR");
            _concluido = true;
        }
        return _sb.ToString();
    }

    /// <summary>Escape de TEXT (RFC 5545 §3.3.11); quebras de linha viram "\n" e outros controles somem.</summary>
    public static string Escapar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case ';': sb.Append(@"\;"); break;
                case ',': sb.Append(@"\,"); break;
                case '\r':
                    if (i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                    sb.Append(@"\n");
                    break;
                case '\n': sb.Append(@"\n"); break;
                default:
                    if (c == '\t') sb.Append(' ');
                    else if (!char.IsControl(c)) sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Duração iCal positiva (ex.: P1D, PT15H, PT1H30M).</summary>
    public static string Duracao(TimeSpan duracao)
    {
        duracao = duracao.Duration();
        var sb = new StringBuilder("P");
        if (duracao.Days > 0) sb.Append(duracao.Days).Append('D');
        if (duracao.Hours > 0 || duracao.Minutes > 0 || duracao.Days == 0)
        {
            sb.Append('T');
            if (duracao.Hours > 0) sb.Append(duracao.Hours).Append('H');
            if (duracao.Minutes > 0 || duracao.Hours == 0) sb.Append(duracao.Minutes).Append('M');
        }
        return sb.ToString();
    }

    private static string Utc(DateTime utc) =>
        utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Data(DateTime data) =>
        data.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Escreve a linha dobrada: no máximo 75 octetos UTF-8 por linha, sem partir caracteres.</summary>
    private void Linha(string conteudo)
    {
        var octetos = 0;
        var limite = MaxOctetos;
        for (var i = 0; i < conteudo.Length; i++)
        {
            var tamanho = char.IsHighSurrogate(conteudo[i]) && i + 1 < conteudo.Length ? 2 : 1;
            var bytes = Encoding.UTF8.GetByteCount(conteudo.AsSpan(i, tamanho));
            if (octetos + bytes > limite)
            {
                _sb.Append("\r\n ");
                octetos = 0;
                limite = MaxOctetos - 1; // o espaço da continuação conta
            }
            _sb.Append(conteudo, i, tamanho);
            octetos += bytes;
            i += tamanho - 1;
        }
        _sb.Append("\r\n");
    }
}
