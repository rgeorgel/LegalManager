using LegalManager.Application.DTOs.Processos;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Prazos e eventos que os dados de um requerimento no INSS pedem. São sugestões: o advogado
/// confere a memória de cálculo e confirma (nada é criado sozinho).
/// Prazos do processo administrativo em dias corridos, sem contar o dia da ciência e, se o
/// vencimento cair em dia sem expediente, passando para o dia útil seguinte (Lei 9.784/1999,
/// art. 66). Só feriados nacionais.
/// </summary>
public static class PrazosInss
{
    public const int DiasExigencia = 30;
    public const int DiasRecurso = 30;
    // O pedido de prorrogação cabe nos 15 dias que antecedem a DCB.
    public const int JanelaProrrogacao = 15;

    public const string ChaveExigencia = "exigencia";
    public const string ChaveRecurso = "recurso-ordinario";
    public const string ChaveProrrogacao = "prorrogacao";
    public const string ChavePericia = "pericia";

    /// <param name="hoje">Hoje "de parede" no fuso do escritório: prazos já vencidos não são sugeridos.</param>
    public static IReadOnlyList<SugestaoPrazoInssDto> Sugerir(RequerimentoInss r, DateTime hoje)
    {
        var lista = new List<SugestaoPrazoInssDto>();
        hoje = hoje.Date;

        if (r.DataCiencia is { } cienciaLida)
        {
            var ciencia = Parede(cienciaLida);
            if (r.Situacao == SituacaoRequerimentoInss.EmExigencia)
            {
                var venc = Vencimento(ciencia, DiasExigencia);
                lista.Add(Prazo(ChaveExigencia, "Cumprir exigência do INSS", venc,
                    $"Ciência da exigência em {D(ciencia)}; {DiasExigencia} dias corridos sem contar o dia da ciência: vence em {D(venc)}" +
                    $"{Prorrogado(ciencia, DiasExigencia, venc)}. Confira o prazo indicado na carta de exigência."));
            }
            else if (r.Situacao == SituacaoRequerimentoInss.Indeferido)
            {
                var venc = Vencimento(ciencia, DiasRecurso);
                lista.Add(Prazo(ChaveRecurso, "Recurso ordinário à Junta de Recursos (CRPS)", venc,
                    $"Ciência do indeferimento em {D(ciencia)}; {DiasRecurso} dias corridos para recorrer (Decreto 3.048/1999, art. 305), " +
                    $"sem contar o dia da ciência: vence em {D(venc)}{Prorrogado(ciencia, DiasRecurso, venc)}. " +
                    "Outro caminho é ajuizar a ação: o indeferimento já mostra o interesse de agir."));
            }
        }

        if (r.Situacao == SituacaoRequerimentoInss.Concedido && EspeciesBeneficioInss.Temporario(r.Especie) && r.Dcb is { } dcbLida)
        {
            var dcb = Parede(dcbLida);
            var ultimoDia = dcb.Date.AddDays(-1);
            lista.Add(Prazo(ChaveProrrogacao, "Pedido de prorrogação do benefício", ultimoDia,
                $"Benefício cessa em {D(dcb)} (DCB). O pedido de prorrogação cabe nos {JanelaProrrogacao} dias anteriores " +
                $"(a partir de {D(dcb.Date.AddDays(-JanelaProrrogacao))}); prazo marcado para a véspera da DCB."));
        }

        if (r.PericiaEm is { } pericia)
            lista.Add(new SugestaoPrazoInssDto(ChavePericia, "evento", "Perícia médica no INSS", Parede(pericia),
                string.IsNullOrWhiteSpace(r.PericiaLocal) ? "Perícia médica agendada." : $"Perícia médica em {r.PericiaLocal.Trim()}."));

        return lista.Where(s => s.Data.Date >= hoje).ToList();
    }

    /// <summary>Último dia do prazo: começo excluído, dias corridos, vencimento em dia útil.</summary>
    public static DateTime Vencimento(DateTime ciencia, int dias)
    {
        var venc = ciencia.Date.AddDays(dias);
        while (!FeriadosService.IsDiaUtil(venc)) venc = venc.AddDays(1);
        return venc;
    }

    private static string Prorrogado(DateTime ciencia, int dias, DateTime venc) =>
        venc == ciencia.Date.AddDays(dias)
            ? ""
            : $" (o {dias}º dia, {D(ciencia.Date.AddDays(dias))}, não tem expediente: passa para o dia útil seguinte)";

    // Sem hora, o prazo fica ao meio-dia (como na calculadora e na triagem de publicações).
    private static SugestaoPrazoInssDto Prazo(string chave, string titulo, DateTime dia, string memoria) =>
        new(chave, "prazo", titulo, Utc(dia.Date.AddHours(12)), memoria);

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    /// <summary>
    /// Valor "de parede" gravado no banco (Kind=Utc). Com o comportamento legado do Npgsql, a
    /// leitura volta como Local no fuso do servidor; aqui ela é desfeita, para o cálculo não
    /// depender do fuso da máquina (em produção os containers rodam em UTC).
    /// </summary>
    public static DateTime Parede(DateTime d) =>
        d.Kind == DateTimeKind.Local ? DateTime.SpecifyKind(d.ToUniversalTime(), DateTimeKind.Utc) : Utc(d);

    private static string D(DateTime d) => d.ToString("dd/MM/yyyy", BrasiliaTime.PtBr);
}
