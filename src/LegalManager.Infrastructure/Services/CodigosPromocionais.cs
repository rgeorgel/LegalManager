using System.Linq.Expressions;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Regras de uso de CodigoPromocional compartilhadas entre o cadastro (AuthService), a tela
/// de assinatura e o super admin. Um tenant "usou" o código se entrou com ele no cadastro
/// (Tenant.VoucherUtilizado) ou o aplicou depois na assinatura (Tenant.CodigoDesconto).
/// </summary>
public static class CodigosPromocionais
{
    public static Expression<Func<Tenant, bool>> UsadoPor(string codigo) =>
        t => t.VoucherUtilizado == codigo || t.CodigoDesconto == codigo;

    /// <summary>
    /// Retorna o código se ele puder ser usado agora; senão lança InvalidOperationException
    /// com a mensagem para o usuário. Código inativo é tratado como inexistente, para não
    /// revelar códigos desligados. <paramref name="tenantId"/>: tenant que está aplicando o
    /// código — se ele já conta como uso, não é barrado pelo limite.
    /// </summary>
    public static async Task<CodigoPromocional> ValidarAsync(
        AppDbContext db, string codigo, Guid? tenantId = null, CancellationToken ct = default)
    {
        var code = CodigoPromocional.Normalizar(codigo);

        var promo = await db.CodigosPromocionais.FirstOrDefaultAsync(c => c.Codigo == code, ct);
        if (promo is null || !promo.Ativo)
            throw new InvalidOperationException("Código promocional inválido.");

        if (promo.ValidoAte.HasValue && promo.ValidoAte.Value < DateOnly.FromDateTime(BrasiliaTime.Hoje))
            throw new InvalidOperationException("Código promocional expirado.");

        if (promo.MaxUsos.HasValue)
        {
            var usos = db.Tenants.Where(UsadoPor(code));
            if (tenantId.HasValue) usos = usos.Where(t => t.Id != tenantId.Value);
            if (await usos.CountAsync(ct) >= promo.MaxUsos.Value)
                throw new InvalidOperationException("Código promocional esgotado.");
        }

        return promo;
    }
}
