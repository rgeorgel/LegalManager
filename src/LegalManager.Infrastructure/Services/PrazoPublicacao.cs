using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Vencimento de um prazo pedido numa publicação: data da publicação no diário eletrônico, os
/// feriados da UF/comarca do processo e as regras de prazo processual do <see cref="FeriadosService"/>.
/// Devolve também a memória de cálculo, que o advogado vê antes de criar o prazo.
/// </summary>
public static class PrazoPublicacao
{
    public sealed record Local(string? Uf, string? Municipio, IReadOnlyList<DateOnly> Feriados, bool TemFeriadoMunicipal);

    public sealed record Resultado(DateTime DataPublicacao, DateTime Vencimento, string Memoria);

    /// <summary>Feriados estaduais da UF e municipais da comarca (globais e do escritório), ativos.</summary>
    public static async Task<Local> LocalAsync(AppDbContext db, Guid tenantId, string? uf, string? municipio, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uf)) return new Local(null, null, [], false);
        uf = uf.Trim().ToUpperInvariant();
        var feriados = await db.Feriados.AsNoTracking()
            .Where(f => f.Ativo && f.Uf == uf && (f.TenantId == null || f.TenantId == tenantId)
                        && (f.Tipo == "estadual" || f.Tipo == "municipal"))
            .Select(f => new { f.Data, f.Tipo, f.Municipio })
            .ToListAsync(ct);

        var mun = municipio?.Trim();
        var municipais = feriados
            .Where(f => f.Tipo == "municipal" && !string.IsNullOrEmpty(mun)
                        && string.Equals(f.Municipio?.Trim(), mun, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Data).ToList();
        var datas = feriados.Where(f => f.Tipo == "estadual").Select(f => f.Data).Concat(municipais).Distinct().ToList();
        return new Local(uf, mun, datas, municipais.Count > 0);
    }

    /// <param name="dataDiario">Data da publicação capturada (a da edição do diário, ou seja, a disponibilização).</param>
    /// <param name="viaDiario">Publicação de diário eletrônico: a publicação é o 1º dia útil seguinte.</param>
    public static Resultado Calcular(
        DateTime dataDiario, bool viaDiario, int dias, TipoCalculo tipo, bool criminal, Local local)
    {
        var dataDiarioDia = DateTime.SpecifyKind(dataDiario.Date, DateTimeKind.Utc);
        var dataPublicacao = viaDiario
            ? FeriadosService.DataPublicacaoDiario(dataDiarioDia, local.Feriados)
            : dataDiarioDia;
        var (vencimento, feriados) = FeriadosService.CalcularPrazo(dataPublicacao, dias, tipo, local.Feriados, prazoProcessual: true);

        static string D(DateTime d) => d.ToString("dd/MM/yyyy", BrasiliaTime.PtBr);
        var partes = new List<string>();
        partes.Add(viaDiario
            ? $"Disponibilizada em {D(dataDiarioDia)}; publicação em {D(dataPublicacao)} (1º dia útil seguinte, Lei 11.419/2006, art. 4º)."
            : $"Publicada em {D(dataPublicacao)}.");
        partes.Add($"{dias} dias {(tipo == TipoCalculo.DiasUteis ? "úteis" : "corridos")} contados a partir do dia útil seguinte: vence em {D(vencimento)}.");
        partes.Add(local.Uf is null
            ? "Só feriados nacionais considerados (UF do processo não identificada): confira os feriados locais."
            : $"Feriados considerados: nacionais e de {local.Uf}{(local.TemFeriadoMunicipal ? $" e de {local.Municipio}" : "")}.");
        if (feriados.Any(f => f.StartsWith("recesso", StringComparison.Ordinal)))
        {
            partes.Add("O recesso forense (20/12 a 20/01) não foi contado.");
            if (criminal)
                partes.Add("Processo criminal: o recesso não suspende prazos com réu preso, da Lei Maria da Penha ou de medidas urgentes (CPP, art. 798-A); confira.");
        }

        return new Resultado(dataPublicacao, DateTime.SpecifyKind(vencimento, DateTimeKind.Utc), string.Join(" ", partes));
    }
}
