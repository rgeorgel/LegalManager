using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

/// <summary>
/// Regra de repetição de uma tarefa ("toda sexta", "todo dia 10"). Cada ocorrência é uma
/// <see cref="Tarefa"/> própria (status, kanban e conclusão independentes); a série só guarda a
/// regra e o modelo usado para criar as próximas. Sempre existe a próxima ocorrência a vencer:
/// quando a última gerada passa, o job cria a seguinte — mesmo que as anteriores sigam abertas.
/// </summary>
public class RecorrenciaTarefa
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public FrequenciaRecorrencia Frequencia { get; set; }
    /// <summary>A cada N dias/semanas/meses (1 = todo/toda).</summary>
    public int Intervalo { get; set; } = 1;
    /// <summary>Semanal: dias da semana como bits (1 &lt;&lt; (int)DayOfWeek; domingo = bit 0).</summary>
    public int DiasSemana { get; set; }
    /// <summary>Mensal: dia do mês (1..31; meses mais curtos usam o último dia).</summary>
    public int? DiaDoMes { get; set; }
    /// <summary>
    /// Referência da série, na hora "de parede" (como <see cref="Tarefa.Prazo"/>): o horário das
    /// ocorrências e a semana/mês de partida para intervalos maiores que 1.
    /// </summary>
    public DateTime Ancora { get; set; }
    /// <summary>Última data incluída (hora "de parede"); null = sem fim.</summary>
    public DateTime? TerminaEm { get; set; }
    /// <summary>Prazo da última ocorrência já criada (ou pulada) — a próxima vem depois dela.</summary>
    public DateTime? UltimaGerada { get; set; }
    public bool Ativa { get; set; } = true;

    // Modelo das próximas ocorrências
    public string Titulo { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public PrioridadeTarefa Prioridade { get; set; }
    public TipoTarefa Tipo { get; set; }
    public Guid? ResponsavelId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid? ContatoId { get; set; }
    /// <summary>Tags separadas por vírgula.</summary>
    public string? Tags { get; set; }

    public Guid CriadoPorId { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario? Responsavel { get; set; }
    public Usuario CriadoPor { get; set; } = null!;
    public Processo? Processo { get; set; }
    public Contato? Contato { get; set; }
}
