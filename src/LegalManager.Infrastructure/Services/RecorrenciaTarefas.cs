using System.Security.Cryptography;
using System.Text;
using LegalManager.Application.DTOs.Atividades;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Regras de repetição de tarefas (<see cref="RecorrenciaTarefa"/>): cálculo das datas, criação
/// das ocorrências e texto em pt-BR. Datas no referencial "de parede", como <see cref="Tarefa.Prazo"/>.
/// </summary>
public static class RecorrenciaTarefas
{
    public const int IntervaloMaximo = 99;

    // Job parado por muito tempo: ocorrências de dias já passados são puladas (não viram uma
    // enxurrada de tarefas atrasadas); este teto só protege o laço.
    private const int MaxIteracoes = 5000;

    /// <summary>Aplica a regra do DTO na série, validando e completando com o dia do prazo.</summary>
    public static void AplicarRegra(RecorrenciaTarefa serie, RecorrenciaDto dto, DateTime ancora)
    {
        if (dto.Intervalo < 1 || dto.Intervalo > IntervaloMaximo)
            throw new ArgumentException($"O intervalo da repetição deve ser entre 1 e {IntervaloMaximo}.");
        if (dto.DiaDoMes is < 1 or > 31)
            throw new ArgumentException("O dia do mês da repetição deve ser entre 1 e 31.");
        if (dto.TerminaEm.HasValue && dto.TerminaEm.Value.Date < ancora.Date)
            throw new ArgumentException("A data de término da repetição não pode ser anterior ao prazo.");

        serie.Frequencia = dto.Frequencia;
        serie.Intervalo = dto.Intervalo;
        serie.Ancora = ancora;
        serie.TerminaEm = dto.TerminaEm?.Date;
        serie.DiasSemana = 0;
        serie.DiaDoMes = null;

        if (dto.Frequencia == FrequenciaRecorrencia.Semanal)
        {
            var dias = dto.DiasSemana is { Length: > 0 } d ? d : new[] { ancora.DayOfWeek };
            foreach (var dia in dias) serie.DiasSemana |= 1 << (int)dia;
        }
        else if (dto.Frequencia == FrequenciaRecorrencia.Mensal)
        {
            serie.DiaDoMes = dto.DiaDoMes ?? ancora.Day;
        }
    }

    /// <summary>Primeira ocorrência em uma data posterior à de <paramref name="depoisDe"/>; null se a série terminou.</summary>
    public static DateTime? Proxima(RecorrenciaTarefa r, DateTime depoisDe) => OcorrenciaDesde(r, depoisDe.Date.AddDays(1));

    // Primeira ocorrência num dia >= desde, no horário da âncora; null se passou do término.
    private static DateTime? OcorrenciaDesde(RecorrenciaTarefa r, DateTime desde)
    {
        var dia = ProximoDia(r, desde);
        if (dia is null || (r.TerminaEm.HasValue && dia.Value > r.TerminaEm.Value.Date)) return null;
        return dia.Value.Add(r.Ancora.TimeOfDay);
    }

    // Primeiro dia >= desde que pertence à série.
    private static DateTime? ProximoDia(RecorrenciaTarefa r, DateTime desde)
    {
        var ancora = r.Ancora.Date;
        var intervalo = Math.Max(1, r.Intervalo);
        switch (r.Frequencia)
        {
            case FrequenciaRecorrencia.Diaria:
            {
                if (desde <= ancora) return ancora;
                var passos = (int)Math.Ceiling((desde - ancora).TotalDays / intervalo);
                return ancora.AddDays((double)passos * intervalo);
            }
            case FrequenciaRecorrencia.Semanal:
            {
                if (r.DiasSemana == 0) return null;
                var semanaAncora = InicioSemana(ancora);
                // Um ciclo inteiro (intervalo semanas) mais uma semana cobre qualquer combinação.
                for (var d = desde; d < desde.AddDays(7 * intervalo + 7); d = d.AddDays(1))
                {
                    if ((r.DiasSemana & (1 << (int)d.DayOfWeek)) == 0) continue;
                    var semanas = (int)Math.Round((InicioSemana(d) - semanaAncora).TotalDays / 7);
                    if ((semanas % intervalo + intervalo) % intervalo == 0) return d;
                }
                return null;
            }
            case FrequenciaRecorrencia.Mensal:
            {
                var diaDoMes = r.DiaDoMes ?? ancora.Day;
                var mesAncora = ancora.Year * 12 + ancora.Month - 1;
                var mes = desde.Year * 12 + desde.Month - 1;
                var resto = ((mes - mesAncora) % intervalo + intervalo) % intervalo;
                if (resto != 0) mes += intervalo - resto;
                for (var i = 0; i < 3; i++, mes += intervalo)
                {
                    var ano = mes / 12;
                    var m = mes % 12 + 1;
                    var d = new DateTime(ano, m, Math.Min(diaDoMes, DateTime.DaysInMonth(ano, m)), 0, 0, 0, r.Ancora.Kind);
                    if (d >= desde) return d;
                }
                return null;
            }
            default:
                return null;
        }
    }

    private static DateTime InicioSemana(DateTime d) => d.Date.AddDays(-(int)d.DayOfWeek);

    /// <summary>
    /// Cria (no contexto, sem salvar) as ocorrências necessárias para que a série tenha sempre a
    /// próxima a vencer: enquanto a última gerada já passou de <paramref name="agora"/>, cria a
    /// seguinte — independe do status das anteriores. Ocorrências de dias já passados (job parado)
    /// são puladas. Desativa a série quando ela termina.
    /// </summary>
    public static List<Tarefa> GerarOcorrencias(AppDbContext context, RecorrenciaTarefa serie, DateTime agora)
    {
        var criadas = new List<Tarefa>();
        var hoje = agora.Date;
        for (var i = 0; serie.Ativa && (serie.UltimaGerada is null || serie.UltimaGerada < agora) && i < MaxIteracoes; i++)
        {
            var proxima = serie.UltimaGerada is { } ultima ? Proxima(serie, ultima) : OcorrenciaDesde(serie, serie.Ancora.Date);
            if (proxima is null) { serie.Ativa = false; break; }
            serie.UltimaGerada = proxima;
            if (proxima.Value.Date < hoje) continue;

            var tarefa = NovaOcorrencia(serie, proxima.Value);
            context.Tarefas.Add(tarefa);
            criadas.Add(tarefa);
        }
        return criadas;
    }

    /// <summary>Datas previstas (ainda não criadas) entre <paramref name="de"/> e <paramref name="ate"/>, para a agenda.</summary>
    public static IEnumerable<DateTime> Previstas(RecorrenciaTarefa serie, DateTime de, DateTime ate, int limite = 200)
    {
        if (!serie.Ativa) yield break;
        var atual = serie.UltimaGerada is { } ultima ? Proxima(serie, ultima) : OcorrenciaDesde(serie, serie.Ancora.Date);
        for (var n = 0; atual is { } data && data <= ate && n < MaxIteracoes; n++)
        {
            if (data >= de)
            {
                yield return data;
                if (--limite <= 0) yield break;
            }
            atual = Proxima(serie, data);
        }
    }

    public static Tarefa NovaOcorrencia(RecorrenciaTarefa serie, DateTime prazo)
    {
        var tarefa = new Tarefa
        {
            Id = Guid.NewGuid(),
            TenantId = serie.TenantId,
            Titulo = serie.Titulo,
            Descricao = serie.Descricao,
            ResponsavelId = serie.ResponsavelId,
            CriadoPorId = serie.CriadoPorId,
            Prazo = prazo,
            Prioridade = serie.Prioridade,
            Status = StatusTarefa.Pendente,
            Tipo = serie.Tipo,
            ProcessoId = serie.ProcessoId,
            ContatoId = serie.ContatoId,
            RecorrenciaId = serie.Id,
            CriadoEm = DateTime.UtcNow
        };
        tarefa.Tags = SepararTags(serie.Tags)
            .Select(tag => new TarefaTag { Id = Guid.NewGuid(), TarefaId = tarefa.Id, Tag = tag })
            .ToList();
        return tarefa;
    }

    /// <summary>Copia os dados da tarefa para o modelo das próximas ocorrências.</summary>
    public static void CopiarModelo(RecorrenciaTarefa serie, Tarefa tarefa, IEnumerable<string>? tags)
    {
        serie.Titulo = tarefa.Titulo;
        serie.Descricao = tarefa.Descricao;
        serie.Prioridade = tarefa.Prioridade;
        serie.Tipo = tarefa.Tipo;
        serie.ResponsavelId = tarefa.ResponsavelId;
        serie.ProcessoId = tarefa.ProcessoId;
        serie.ContatoId = tarefa.ContatoId;
        var lista = (tags ?? Enumerable.Empty<string>())
            .Select(t => t.Trim().Replace(",", " "))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        serie.Tags = lista.Count > 0 ? string.Join(",", lista) : null;
    }

    private static IEnumerable<string> SepararTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? Enumerable.Empty<string>()
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);

    public static DayOfWeek[] DiasDaSemana(int bits) =>
        Enum.GetValues<DayOfWeek>().Where(d => (bits & (1 << (int)d)) != 0).ToArray();

    public static RecorrenciaResponseDto ParaDto(RecorrenciaTarefa r) => new(
        r.Id, r.Frequencia, r.Intervalo, DiasDaSemana(r.DiasSemana), r.DiaDoMes, r.TerminaEm, r.Ativa, Descrever(r));

    private static readonly string[] NomesDias =
        { "domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado" };

    /// <summary>Ex.: "Toda sexta-feira", "A cada 2 semanas (segunda e quarta)", "Todo dia 10", "Diariamente até 31/12/2026".</summary>
    public static string Descrever(RecorrenciaTarefa r)
    {
        var n = Math.Max(1, r.Intervalo);
        string texto;
        switch (r.Frequencia)
        {
            case FrequenciaRecorrencia.Diaria:
                texto = n == 1 ? "Diariamente" : $"A cada {n} dias";
                break;
            case FrequenciaRecorrencia.Semanal:
            {
                var dias = DiasDaSemana(r.DiasSemana);
                var nomes = Juntar(dias.Select(d => NomesDias[(int)d]).ToList());
                var fimDeSemana = dias.Length == 1 && dias[0] is DayOfWeek.Saturday or DayOfWeek.Sunday;
                if (n == 1 && dias.Length == 1)
                    texto = fimDeSemana ? $"Todo {NomesDias[(int)dias[0]]}" : $"Toda {NomesDias[(int)dias[0]]}-feira";
                else if (n == 1)
                    texto = $"Toda semana ({nomes})";
                else
                    texto = $"A cada {n} semanas ({nomes})";
                break;
            }
            case FrequenciaRecorrencia.Mensal:
            {
                var dia = r.DiaDoMes ?? r.Ancora.Day;
                var diaTxt = dia >= 29 ? $"dia {dia} (ou o último dia do mês)" : $"dia {dia}";
                texto = n == 1 ? $"Todo {diaTxt}" : $"A cada {n} meses, no {diaTxt}";
                break;
            }
            default:
                texto = "Recorrente";
                break;
        }
        if (r.TerminaEm.HasValue)
            texto += $" até {r.TerminaEm.Value.ToString("dd/MM/yyyy", BrasiliaTime.PtBr)}";
        return texto;
    }

    private static string Juntar(List<string> itens) =>
        itens.Count <= 1 ? string.Concat(itens) : string.Join(", ", itens.Take(itens.Count - 1)) + " e " + itens[^1];

    /// <summary>Id estável de uma ocorrência prevista na agenda (não existe tarefa com esse id).</summary>
    public static Guid IdPrevista(Guid serieId, DateTime data) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"{serieId:N}:{data.Ticks}")));
}
