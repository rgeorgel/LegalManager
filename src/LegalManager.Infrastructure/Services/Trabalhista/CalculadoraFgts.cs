using System.Globalization;
using LegalManager.Application.DTOs.Trabalhista;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services.Trabalhista;

/// <summary>FGTS devido (depósitos não recolhidos + multa rescisória) e revisão TR × índice (ADI 5090).</summary>
public static class CalculadoraFgts
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // ── Competências ────────────────────────────────────────────────────

    /// <summary>
    /// Competências do contrato: salário proporcional aos dias no 1º e no último mês (base 30), 13º por avos
    /// (dezembro, ou o mês da saída no último ano) e o aviso prévio indenizado (Súmula 305 do TST).
    /// </summary>
    public static List<CompetenciaFgtsDto> GerarCompetencias(GerarCompetenciasFgtsDto dto)
    {
        var lista = new List<CompetenciaFgtsDto>();
        if (dto.Saida < dto.Admissao) return lista;
        foreach (var (ano, mes) in AtualizacaoTrabalhista.Meses((dto.Admissao.Year, dto.Admissao.Month), Proximo((dto.Saida.Year, dto.Saida.Month))))
        {
            var dias = ContratoTrabalho.DiasTrabalhadosNoMes(ano, mes, dto.Admissao, dto.Saida);
            var mesInteiro = dias == DateTime.DaysInMonth(ano, mes);
            var remuneracao = mesInteiro ? dto.Salario : Arredondar(dto.Salario / 30m * Math.Min(30, dias));
            lista.Add(new CompetenciaFgtsDto(ano, mes, remuneracao));

            var fimDoAno = mes == 12 || (ano, mes) == (dto.Saida.Year, dto.Saida.Month);
            if (dto.Incluir13 && fimDoAno)
            {
                var avos = ContratoTrabalho.Avos13(ano, dto.Admissao, dto.Saida);
                if (avos > 0) lista.Add(new CompetenciaFgtsDto(ano, mes, Arredondar(dto.Salario * avos / 12m), 0, "13"));
            }
        }
        if (dto.DiasAvisoIndenizado > 0)
            lista.Add(new CompetenciaFgtsDto(dto.Saida.Year, dto.Saida.Month,
                Arredondar(dto.Salario / 30m * dto.DiasAvisoIndenizado), 0, "aviso"));
        return lista;
    }

    // ── FGTS devido ─────────────────────────────────────────────────────

    public static string? ValidarDevido(CalcularFgtsDevidoDto dto)
    {
        if (dto.Competencias is not { Count: > 0 }) return "Inclua ao menos uma competência.";
        var calc = (dto.DataCalculo.Year, dto.DataCalculo.Month);
        var futura = dto.Competencias.FirstOrDefault(c => (c.Ano, c.Mes).CompareTo(calc) > 0);
        if (futura is not null) return $"A competência {futura.Mes:00}/{futura.Ano} é posterior à data do cálculo.";
        if (dto.DataAjuizamento is { } aj && aj > dto.DataCalculo) return "O ajuizamento não pode ser depois da data do cálculo.";
        return null;
    }

    public static ResultadoFgtsDevidoDto CalcularDevido(CalcularFgtsDevidoDto dto, SeriesMensais series)
    {
        var atualizacao = new AtualizacaoTrabalhista(dto.Atualizacao, dto.DataCalculo, dto.DataAjuizamento, series);
        var avisos = new List<string>();
        var referencia = dto.DataAjuizamento ?? dto.DataCalculo;
        var limitePrescricao = referencia.AddYears(-5);

        var linhas = new List<LinhaFgtsDto>();
        var ordenadas = dto.Competencias
            .OrderBy(c => c.Ano).ThenBy(c => c.Mes).ThenBy(c => OrdemTipo(c.Tipo)).ToList();
        foreach (var c in ordenadas)
        {
            var devido = Arredondar(c.Remuneracao * dto.Aliquota / 100m);
            var diferenca = Math.Max(0, devido - c.Depositado);
            // Depósito até o dia 7 (hoje, dia 20 no FGTS Digital) do mês seguinte à competência.
            var vencimento = new DateOnly(c.Ano, c.Mes, 1).AddMonths(1);
            var prescrita = dto.LimitarPrescricao && vencimento < limitePrescricao;
            var atualizado = prescrita ? new ValorAtualizado(0, 0, 0, 1, 0) : atualizacao.Atualizar(diferenca, vencimento);
            linhas.Add(new LinhaFgtsDto(
                $"{c.Mes:00}/{c.Ano}", Descricao(c), vencimento, c.Remuneracao, devido, c.Depositado,
                prescrita ? 0 : diferenca, atualizado.FatorCorrecao, atualizado.JurosPercentual, atualizado.Juros,
                atualizado.Total, prescrita));
        }

        var validas = linhas.Where(l => !l.Prescrita).ToList();
        var totalDevido = validas.Sum(l => l.Devido);
        var diferencas = validas.Sum(l => l.Diferenca);
        var baseMulta = dto.SaldoParaFinsRescisorios is { } saldo
            ? saldo + diferencas
            : linhas.Sum(l => l.Devido); // a multa incide sobre todos os depósitos do contrato (inclusive os prescritos)
        var multa = Math.Max(0, Arredondar(baseMulta * dto.MultaPercentual / 100m) - dto.MultaJaPaga);
        var rescisao = dto.DataRescisao ?? UltimoDia(ordenadas[^1].Ano, ordenadas[^1].Mes);
        var multaAtualizada = atualizacao.Atualizar(multa, rescisao.AddDays(10)).Total;

        var prescritas = linhas.Count(l => l.Prescrita);
        if (prescritas > 0)
            avisos.Add($"{prescritas} {(prescritas == 1 ? "competência anterior" : "competências anteriores")} a {limitePrescricao.ToString("dd/MM/yyyy", PtBr)} fora do cálculo pela prescrição quinquenal (STF, ARE 709.212).");
        if (dto.SaldoParaFinsRescisorios is null && dto.MultaPercentual > 0)
            avisos.Add("Sem o saldo para fins rescisórios do extrato, a multa usa a soma dos depósitos devidos, sem a remuneração da conta (JAM).");
        avisos.AddRange(atualizacao.Avisos());

        return new ResultadoFgtsDevidoDto(linhas, totalDevido, validas.Sum(l => l.Depositado), diferencas,
            validas.Sum(l => l.Total), baseMulta, multa, multaAtualizada,
            validas.Sum(l => l.Total) + multaAtualizada,
            AtualizacaoTrabalhista.Descricao(dto.Atualizacao, dto.DataAjuizamento), avisos);
    }

    private static int OrdemTipo(string tipo) => tipo switch { "13" => 1, "aviso" => 2, _ => 0 };

    private static string Descricao(CompetenciaFgtsDto c) => c.Tipo switch
    {
        "13" => $"13º salário {c.Ano}",
        "aviso" => "Aviso prévio indenizado",
        _ => "Remuneração"
    };

    // ── Revisão (ADI 5090) ──────────────────────────────────────────────

    public static string NomeIndice(IndiceRevisaoFgts indice) => indice switch
    {
        IndiceRevisaoFgts.IPCAE => "IPCA-E (IBGE)",
        IndiceRevisaoFgts.IPCA => "IPCA (IBGE)",
        _ => "INPC (IBGE)"
    };

    public static TipoIndice TipoDoIndice(IndiceRevisaoFgts indice) => indice switch
    {
        IndiceRevisaoFgts.IPCAE => TipoIndice.TJSP,
        IndiceRevisaoFgts.IPCA => TipoIndice.IPCA,
        _ => TipoIndice.INPC
    };

    public static string? ValidarRevisao(CalcularRevisaoFgtsDto dto)
    {
        if (dto.Movimentos is not { Count: > 0 }) return "Inclua ao menos um mês.";
        var calc = (dto.DataCalculo.Year, dto.DataCalculo.Month);
        if (dto.Movimentos.Any(m => (m.Ano, m.Mes).CompareTo(calc) >= 0))
            return "Os meses do extrato precisam ser anteriores ao mês da data do cálculo.";
        return null;
    }

    /// <summary>
    /// Refaz o saldo mês a mês com TR + juros (como a Caixa) e com o índice escolhido + juros: em cada mês o saldo
    /// anterior rende (1 + taxa)·(1 + juros mensais) e depois entram o depósito e o saque do mês.
    /// A diferença no fim do período é atualizada até a data do cálculo pelo mesmo índice + juros da conta.
    /// </summary>
    public static ResultadoRevisaoFgtsDto CalcularRevisao(CalcularRevisaoFgtsDto dto, SeriesMensais series)
    {
        var tipo = TipoDoIndice(dto.Indice);
        var nome = NomeIndice(dto.Indice);
        var jurosMensais = (decimal)(Math.Pow(1 + (double)dto.JurosAnuais / 100, 1.0 / 12) - 1);
        var faltaTr = new SortedSet<(int, int)>();
        var faltaIndice = new SortedSet<(int, int)>();

        var movimentos = dto.Movimentos.GroupBy(m => (m.Ano, m.Mes))
            .ToDictionary(g => g.Key, g => (Deposito: g.Sum(x => x.Deposito), Saque: g.Sum(x => x.Saque)));
        var inicio = movimentos.Keys.Min();
        var fim = movimentos.Keys.Max();

        decimal saldoTr = dto.SaldoInicial, saldoRev = dto.SaldoInicial;
        var linhas = new List<LinhaRevisaoFgtsDto>();
        foreach (var m in AtualizacaoTrabalhista.Meses(inicio, Proximo(fim)))
        {
            if (!series.Taxa(TipoIndice.TR, m, out var tr)) faltaTr.Add(m);
            if (!series.Taxa(tipo, m, out var idx)) faltaIndice.Add(m);
            movimentos.TryGetValue(m, out var mov);

            saldoTr = Math.Max(0, saldoTr * (1 + tr) * (1 + jurosMensais) + mov.Deposito - mov.Saque);
            saldoRev = Math.Max(0, saldoRev * (1 + idx) * (1 + jurosMensais) + mov.Deposito - mov.Saque);
            linhas.Add(new LinhaRevisaoFgtsDto($"{m.Mes:00}/{m.Ano}", mov.Deposito, mov.Saque,
                Math.Round(tr * 100, 4), Math.Round(idx * 100, 4),
                Arredondar(saldoTr), Arredondar(saldoRev), Arredondar(saldoRev - saldoTr)));
        }

        var diferenca = Arredondar(saldoRev - saldoTr);
        var fator = 1m;
        foreach (var m in AtualizacaoTrabalhista.Meses(Proximo(fim), (dto.DataCalculo.Year, dto.DataCalculo.Month)))
        {
            if (!series.Taxa(tipo, m, out var idx)) faltaIndice.Add(m);
            fator *= (1 + idx) * (1 + jurosMensais);
        }
        fator = Math.Round(fator, 6, MidpointRounding.AwayFromZero);

        var avisos = new List<string>
        {
            "O STF julgou a ADI 5090 em 12/06/2024: a remuneração do FGTS (TR + 3% a.a. + distribuição de lucros) deve garantir ao menos o IPCA, " +
            "com efeitos apenas a partir da decisão — sem pagamento de diferenças passadas. Use este cálculo como estudo ou para casos específicos."
        };
        if (faltaTr.Count > 0) avisos.Add($"TR sem dado para {Lista(faltaTr)} — considerada 0%.");
        if (faltaIndice.Count > 0) avisos.Add($"{nome} ainda não publicado para {Lista(faltaIndice)} — considerado 0%.");

        return new ResultadoRevisaoFgtsDto(linhas, nome, Arredondar(saldoTr), Arredondar(saldoRev), diferenca,
            fator, Arredondar(diferenca * fator), avisos);
    }

    // ── util ────────────────────────────────────────────────────────────

    private static (int Ano, int Mes) Proximo((int Ano, int Mes) m) => m.Mes == 12 ? (m.Ano + 1, 1) : (m.Ano, m.Mes + 1);
    private static DateOnly UltimoDia(int ano, int mes) => new DateOnly(ano, mes, 1).AddMonths(1).AddDays(-1);
    private static string Lista(IEnumerable<(int Ano, int Mes)> meses) => string.Join(", ", meses.Select(m => $"{m.Mes:00}/{m.Ano}"));
    private static decimal Arredondar(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
