namespace LegalManager.Domain;

public static class TenantExportConstants
{
    public const int SchemaVersion = 1;

    public const string ReplicaEmailDomain = "causify-replica.com";

    public const string DefaultReplicaPassword = "Causify@Replic@2026!";

    public const string EmailLocalPartFallback = "usuario";

    public static readonly HashSet<string> AnonymizedEmailPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Email",
        "EmailSecundario",
        "EmailContato",
        "NormalizedEmail",
    };

    public static readonly HashSet<string> AnonymizedPhonePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Telefone",
        "Celular",
        "WhatsApp",
        "Phone",
        "PhoneNumber",
        "PhoneNumberConfirmed",
    };

    public static readonly HashSet<string> IdentityUserNameLikeProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "UserName",
        "NormalizedUserName",
    };

    public const string SourceSystemMarker = "causify-replica-export";
}
