using LegalManager.Domain.Entities;

namespace LegalManager.Domain;

/// <summary>
/// Recursos em teste com alguns escritórios: ficam escondidos (telas e API) até o super admin
/// liberar o escritório em /superadmin/tenant-detalhe.html. Para abrir um recurso a todos, troque
/// <see cref="Liberado"/> para responder true para ele e, depois, tire-o do catálogo.
/// </summary>
public static class RecursosEmTeste
{
    public const string AssinaturaDigital = "assinatura-digital";

    public sealed record Recurso(string Chave, string Nome, string Descricao);

    public static readonly IReadOnlyList<Recurso> Catalogo =
    [
        new(AssinaturaDigital, "Assinatura eletrônica",
            "Envio de contratos para assinatura pelo Contratos: aba Integrações, botão Assinatura e cartão no portal do cliente."),
    ];

    public static bool Existe(string? chave) => chave != null && Catalogo.Any(r => r.Chave == chave);

    public static bool Liberado(Tenant? tenant, string chave) =>
        tenant != null && tenant.RecursosLiberados.Contains(chave);

    /// <summary>Os recursos do catálogo liberados para o escritório (o que o frontend recebe).</summary>
    public static IReadOnlyList<string> DoTenant(Tenant? tenant) =>
        Catalogo.Select(r => r.Chave).Where(c => Liberado(tenant, c)).ToList();
}
