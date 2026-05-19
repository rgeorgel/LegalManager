using LegalManager.Domain.Enums;

namespace LegalManager.Domain;

public static class PlanoRestricoes
{
    public static int MaxUsuarios(PlanoTipo plano) => plano switch
    {
        PlanoTipo.Free => 1,
        PlanoTipo.Plus => 2,
        _ => 5
    };
    public static int MaxProcessosMonitorados(PlanoTipo plano) => plano switch
    {
        PlanoTipo.Free => 20,
        PlanoTipo.Plus => 100,
        _ => 500
    };
    public static int ArmazenamentoLimiteMB(PlanoTipo plano) => plano switch
    {
        PlanoTipo.Free => 1024,
        PlanoTipo.Plus => 5 * 1024,
        _ => 20 * 1024
    };
    public static bool PermiteFinanceiro(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermiteIndicadores(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermiteCalculadoraPrazos(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermitePortalCliente(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermiteCapturacaoPublicacoes(PlanoTipo plano) => plano is PlanoTipo.Pro or PlanoTipo.Enterprise;
    public static bool PermiteTemplatesDocumentos(PlanoTipo plano) => plano is PlanoTipo.Pro or PlanoTipo.Enterprise;
}