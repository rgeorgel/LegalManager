using System.Text;
using LegalManager.Application.DTOs.Processos;

namespace LegalManager.API.Reports;

/// <summary>
/// CSV do relatório de processos no formato que o Excel em português abre direto: separador ";",
/// UTF-8 com BOM (acentos), datas dd/MM/yyyy e valores 1.234,56.
/// </summary>
public static class ProcessosRelatorioCsv
{
    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Cabecalho =
    [
        "Número do processo", "Autor(es)", "Réu(s)", "Tribunal", "Vara", "Comarca", "Área", "Tipo de ação",
        "Fase", "Status", "Responsável", "Valor da causa", "Data de distribuição",
        "Última movimentação", "Descrição da última movimentação"
    ];

    public static byte[] Gerar(ProcessoRelatorioDto dados)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(';', Cabecalho)).Append("\r\n");
        foreach (var p in dados.Itens)
        {
            string[] campos =
            [
                Texto(p.NumeroCNJ), Texto(p.Autores), Texto(p.Reus), Texto(p.Tribunal), Texto(p.Vara),
                Texto(p.Comarca), ProcessoRotulos.Area(p.AreaDireito), Texto(p.TipoAcao),
                ProcessoRotulos.Fase(p.Fase), ProcessoRotulos.Status(p.Status), Texto(p.AdvogadoResponsavel),
                p.ValorCausa?.ToString("N2", PtBr) ?? "",
                p.DataDistribuicao?.ToString("dd/MM/yyyy", PtBr) ?? "",
                p.UltimoAndamentoData?.ToString("dd/MM/yyyy", PtBr) ?? "",
                Texto(p.UltimoAndamentoDescricao)
            ];
            sb.Append(string.Join(';', campos)).Append("\r\n");
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Texto(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return "";
        // Texto vindo de fora (andamentos, nomes) começando com = + - @ seria executado como
        // fórmula pelo Excel (CSV injection) — o apóstrofo força texto.
        if (valor[0] is '=' or '+' or '-' or '@' or '\t' or '\r') valor = "'" + valor;
        return valor.IndexOfAny([';', '"', '\n', '\r']) >= 0
            ? "\"" + valor.Replace("\"", "\"\"") + "\""
            : valor;
    }
}
