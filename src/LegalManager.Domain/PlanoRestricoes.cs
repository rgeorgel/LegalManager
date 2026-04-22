using LegalManager.Domain.Enums;

namespace LegalManager.Domain;

public static class PlanoRestricoes
{
    public static int MaxUsuarios(PlanoTipo plano) => plano == PlanoTipo.Free ? 1 : 5;
    public static int MaxProcessosMonitorados(PlanoTipo plano) => plano == PlanoTipo.Free ? 40 : 500;
    public static int MaxNomesCaptura(PlanoTipo plano) => plano == PlanoTipo.Free ? 0 : 3;
    public static int ArmazenamentoLimiteMB(PlanoTipo plano) => plano == PlanoTipo.Free ? 1024 : 20 * 1024;
    public static bool PermiteFinanceiro(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermiteIndicadores(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermiteCalculadoraPrazos(PlanoTipo plano) => plano != PlanoTipo.Free;
    public static bool PermitePortalCliente(PlanoTipo plano) => plano != PlanoTipo.Free;
}
