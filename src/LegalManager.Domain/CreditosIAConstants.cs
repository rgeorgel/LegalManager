namespace LegalManager.Domain;

public static class CreditosIAConstants
{
    /// <summary>
    /// Promoção temporária para incentivar o uso da IA: as funcionalidades de IA não descontam
    /// créditos e a compra de pacotes fica bloqueada (a aba continua visível, com um aviso).
    /// Volte para <c>false</c> para retomar a cobrança — o saldo de cada escritório está intacto.
    /// </summary>
    public const bool UsoGratuito = true;
}
