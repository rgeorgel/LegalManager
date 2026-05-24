using LegalManager.Domain.Enums;

namespace LegalManager.Domain;

public static class PlanoRestricoes
{
    public static int MaxUsuarios(PlanoTipo plano) => plano switch
    {
        PlanoTipo.Free => 1,
        PlanoTipo.Plus => 1,
        PlanoTipo.Pro => 2,
        _ => 5
    };
    public static int MaxProcessosMonitorados(PlanoTipo plano) => plano switch
    {
        PlanoTipo.Free => 20,
        PlanoTipo.Plus => 40,
        PlanoTipo.Pro => 100,
        PlanoTipo.Max => 250,
        _ => 500
    };
    public static int ArmazenamentoLimiteMB(PlanoTipo plano) => plano switch
    {
        PlanoTipo.Free => 1024,
        PlanoTipo.Plus => 2 * 1024,
        PlanoTipo.Pro => 5 * 1024,
        PlanoTipo.Max => 10 * 1024,
        _ => 20 * 1024
    };
    public static bool PermiteFinanceiro(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermiteIndicadores(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermiteCalculadoraPrazos(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermitePortalCliente(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermiteCapturacaoPublicacoes(PlanoTipo plano) => plano is PlanoTipo.Pro or PlanoTipo.Max or PlanoTipo.Enterprise;
    public static bool PermiteTemplatesDocumentos(PlanoTipo plano) => plano is PlanoTipo.Pro or PlanoTipo.Max or PlanoTipo.Enterprise;
    public static int MaxCapturasPublicacoes(PlanoTipo plano) => plano switch
    {
        PlanoTipo.Pro => 1,
        PlanoTipo.Max => 3,
        PlanoTipo.Enterprise => 3,
        _ => 0
    };
}
