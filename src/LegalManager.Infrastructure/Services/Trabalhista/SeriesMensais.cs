using LegalManager.Application.DTOs.Pensao;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services.Trabalhista;

/// <summary>Índices mensais (taxas fracionárias: 0,0042 = 0,42%) e vigências do salário mínimo.</summary>
public sealed class SeriesMensais(
    IReadOnlyDictionary<TipoIndice, IReadOnlyDictionary<(int Ano, int Mes), decimal>> indices,
    IReadOnlyList<SalarioMinimoVigenciaDto> salariosMinimos)
{
    public IReadOnlyList<SalarioMinimoVigenciaDto> SalariosMinimos { get; } = salariosMinimos;

    public bool Taxa(TipoIndice tipo, (int Ano, int Mes) mes, out decimal taxa)
    {
        taxa = 0;
        return indices.TryGetValue(tipo, out var serie) && serie.TryGetValue(mes, out taxa);
    }

    public decimal? SalarioMinimoEm(int ano, int mes)
    {
        decimal? valor = null;
        foreach (var s in SalariosMinimos)
        {
            if ((s.Ano, s.Mes).CompareTo((ano, mes)) > 0) break;
            valor = s.Valor;
        }
        return valor ?? (SalariosMinimos.Count > 0 ? SalariosMinimos[0].Valor : null);
    }

    public static SeriesMensais Vazia => new(new Dictionary<TipoIndice, IReadOnlyDictionary<(int, int), decimal>>(), []);

    public static async Task<SeriesMensais> CarregarAsync(AppDbContext db, (int Ano, int Mes) desde,
        IEnumerable<TipoIndice> tipos, bool salariosMinimos, CancellationToken ct)
    {
        var chave = desde.Ano * 100 + desde.Mes;
        var lista = tipos.Distinct().ToList();
        var linhas = await db.IndicesCorrecaoMonetaria.AsNoTracking()
            .Where(i => lista.Contains(i.Tipo) && i.Ano * 100 + i.Mes >= chave)
            .Select(i => new { i.Tipo, i.Ano, i.Mes, i.Valor })
            .ToListAsync(ct);
        var indices = lista.ToDictionary(
            t => t,
            t => (IReadOnlyDictionary<(int, int), decimal>)linhas.Where(l => l.Tipo == t).ToDictionary(l => (l.Ano, l.Mes), l => l.Valor));

        var salarios = new List<SalarioMinimoVigenciaDto>();
        if (salariosMinimos)
        {
            var meses = await db.SalariosMinimos.AsNoTracking()
                .OrderBy(s => s.Ano).ThenBy(s => s.Mes)
                .Select(s => new { s.Ano, s.Mes, s.Valor })
                .ToListAsync(ct);
            foreach (var m in meses)
                if (salarios.Count == 0 || salarios[^1].Valor != m.Valor) salarios.Add(new(m.Ano, m.Mes, m.Valor));
        }
        return new SeriesMensais(indices, salarios);
    }
}
