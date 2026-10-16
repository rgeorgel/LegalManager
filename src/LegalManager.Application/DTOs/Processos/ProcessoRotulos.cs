using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Processos;

/// <summary>Rótulos em português dos enums de processo, para relatórios (mesmos textos da tela).</summary>
public static class ProcessoRotulos
{
    public static string Area(AreaDireito area) => area switch
    {
        AreaDireito.Tributario => "Tributário",
        AreaDireito.Previdenciario => "Previdenciário",
        AreaDireito.Familia => "Família",
        AreaDireito.Imobiliario => "Imobiliário",
        _ => area.ToString()
    };

    public static string Fase(FaseProcessual fase) => fase switch
    {
        FaseProcessual.Execucao => "Execução",
        FaseProcessual.Cumprimento => "Cumprimento de Sentença",
        FaseProcessual.InqueritoPolicial => "Inquérito Policial",
        FaseProcessual.InvestigacaoDefensiva => "Investigação Defensiva",
        _ => fase.ToString()
    };

    public static string Status(StatusProcesso status) => status.ToString();
}
