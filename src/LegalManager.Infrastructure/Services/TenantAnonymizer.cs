using System.Reflection;
using System.Text.RegularExpressions;
using LegalManager.Domain;

namespace LegalManager.Infrastructure.Services;

public class TenantAnonymizer
{
    public const string AnonymizedPhonePlaceholder = "+5511900000000";

    private static readonly Regex EmailRegex = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled);

    private static readonly Regex PhoneRegex = new(
        @"(?<!\d)(?:\+?55\s?)?(?:\(?\d{2}\)?\s?)?9?\d{4}[-\s]?\d{4}(?!\d)",
        RegexOptions.Compiled);

    public int AnonymizeInPlace(object entity)
    {
        if (entity is null) return 0;

        var changes = 0;
        var type = entity.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.GetIndexParameters().Length > 0) continue;

            var value = prop.GetValue(entity);
            if (value is not string s || string.IsNullOrWhiteSpace(s)) continue;

            var newValue = AnonymizeString(prop.Name, s);
            if (!ReferenceEquals(newValue, s))
            {
                prop.SetValue(entity, newValue);
                changes++;
            }
        }

        return changes;
    }

    public string AnonymizeString(string propertyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var isEmailLike = LooksLikeEmail(propertyName) || LooksLikeEmailField(value);
        var isPhoneLike = LooksLikePhone(propertyName);

        if (!isEmailLike && !isPhoneLike)
        {
            if (LooksLikeEmailField(value)) isEmailLike = true;
            else if (LooksLikePhoneField(value)) isPhoneLike = true;
        }

        if (isEmailLike)
        {
            return EmailRegex.Replace(value, match =>
            {
                var local = SanitizeLocalPart(match.Value);
                return $"{local}@{TenantExportConstants.ReplicaEmailDomain}";
            });
        }

        if (isPhoneLike)
        {
            return PhoneRegex.Replace(value, match => AnonymizedPhonePlaceholder);
        }

        return value;
    }

    public int AnonymizeAllEmailsInString(string propertyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var count = 0;
        foreach (Match m in EmailRegex.Matches(value))
            count++;
        return count;
    }

    private static bool LooksLikeEmail(string propertyName) =>
        TenantExportConstants.AnonymizedEmailPropertyNames.Contains(propertyName) ||
        propertyName.StartsWith("Email", StringComparison.OrdinalIgnoreCase) ||
        TenantExportConstants.IdentityUserNameLikeProperties.Contains(propertyName);

    private static bool LooksLikePhone(string propertyName) =>
        TenantExportConstants.AnonymizedPhonePropertyNames.Contains(propertyName) ||
        propertyName.EndsWith("Telefone", StringComparison.OrdinalIgnoreCase) ||
        propertyName.EndsWith("Celular", StringComparison.OrdinalIgnoreCase) ||
        propertyName.EndsWith("WhatsApp", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeEmailField(string value) =>
        EmailRegex.IsMatch(value);

    private static bool LooksLikePhoneField(string value) =>
        PhoneRegex.IsMatch(value);

    private static string SanitizeLocalPart(string email)
    {
        var atIdx = email.LastIndexOf('@');
        var local = atIdx > 0 ? email[..atIdx] : email;
        local = local.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(local))
            local = TenantExportConstants.EmailLocalPartFallback;
        local = Regex.Replace(local, @"[^a-z0-9._%+\-]", "-");
        if (local.Length > 60) local = local[..60];
        return local;
    }

    public string GetReplicaEmailDomain() => TenantExportConstants.ReplicaEmailDomain;

    public string GetReplicaPassword() => TenantExportConstants.DefaultReplicaPassword;
}
