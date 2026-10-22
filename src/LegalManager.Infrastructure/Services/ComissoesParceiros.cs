using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Regras de comissão dos parceiros (ver Parceiro), compartilhadas entre o webhook do Stripe,
/// a troca de plano, o portal do parceiro e o super admin.
/// </summary>
public static class ComissoesParceiros
{
    /// <summary>
    /// Gera a comissão do parceiro que indicou o tenant sobre um pagamento de assinatura recém
    /// registrado (checkout, renovação ou diferença de troca de plano). Compra de créditos de IA
    /// não comissiona. Não salva — o chamador grava junto com o Faturamento.
    /// </summary>
    public static async Task<ComissaoParceiro?> RegistrarAsync(
        AppDbContext db, Tenant tenant, Faturamento faturamento, CancellationToken ct = default)
    {
        if (tenant.ParceiroId is not { } parceiroId || faturamento.Valor <= 0) return null;

        var parceiro = await db.Parceiros.FirstOrDefaultAsync(p => p.Id == parceiroId, ct);
        if (parceiro is not { Ativo: true } || parceiro.PercentualComissao <= 0) return null;

        if (await db.ComissoesParceiros.AnyAsync(c => c.FaturamentoId == faturamento.Id, ct)) return null;

        var pagoEm = faturamento.DataPagamento ?? faturamento.DataCriacao;
        if (parceiro.MesesComissao is { } meses)
        {
            var inicio = await InicioComissaoAsync(db, parceiroId, tenant.Id, ct);
            if (inicio is { } i && pagoEm >= i.AddMonths(meses)) return null;
        }

        var comissao = new ComissaoParceiro
        {
            Id = Guid.NewGuid(),
            ParceiroId = parceiroId,
            TenantId = tenant.Id,
            NomeEscritorio = tenant.Nome,
            FaturamentoId = faturamento.Id,
            Descricao = faturamento.Descricao,
            ValorPagamento = faturamento.Valor,
            Percentual = parceiro.PercentualComissao,
            Valor = Calcular(faturamento.Valor, parceiro.PercentualComissao),
            PagoEm = pagoEm,
            CriadoEm = DateTime.UtcNow
        };
        db.ComissoesParceiros.Add(comissao);
        return comissao;
    }

    public static decimal Calcular(decimal valorPagamento, decimal percentual) =>
        Math.Round(valorPagamento * percentual / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Primeiro pagamento comissionado do tenant para o parceiro — início da janela de MesesComissao.</summary>
    public static Task<DateTime?> InicioComissaoAsync(AppDbContext db, Guid parceiroId, Guid tenantId, CancellationToken ct = default) =>
        db.ComissoesParceiros
            .Where(c => c.ParceiroId == parceiroId && c.TenantId == tenantId)
            .MinAsync(c => (DateTime?)c.PagoEm, ct);

    /// <summary>Situação de uma comissão: disponível (sem solicitação) ou o status da solicitação.</summary>
    public static string Situacao(StatusSolicitacaoPagamento? statusSolicitacao) => statusSolicitacao switch
    {
        StatusSolicitacaoPagamento.Pendente => "Solicitada",
        StatusSolicitacaoPagamento.Paga => "Paga",
        // Recusada não acontece: recusar desvincula as comissões, que voltam a ficar disponíveis.
        _ => "Disponivel"
    };

    /// <summary>Situação comercial do escritório indicado, como o parceiro a vê.</summary>
    public static string SituacaoEscritorio(Tenant t) =>
        t.Status == StatusTenant.Cancelado ? "Cancelado"
        : t.StripeSubscriptionId != null && t.ValorMensalAssinatura != null ? "Assinante"
        : t.Status == StatusTenant.Trial ? "Em teste"
        : t.Plano != PlanoTipo.Free && t.PlanoExpiraEm != null ? "Período grátis"
        : "Gratuito";

    public record Totais(decimal Total, decimal Disponivel, decimal Solicitado, decimal Pago);

    public static async Task<Totais> CalcularTotaisAsync(AppDbContext db, Guid parceiroId, CancellationToken ct = default)
    {
        var comissoes = await db.ComissoesParceiros
            .Where(c => c.ParceiroId == parceiroId)
            .Select(c => new { c.Valor, Status = c.Solicitacao == null ? (StatusSolicitacaoPagamento?)null : c.Solicitacao.Status })
            .ToListAsync(ct);

        decimal Soma(string situacao) => comissoes.Where(c => Situacao(c.Status) == situacao).Sum(c => c.Valor);
        return new Totais(comissoes.Sum(c => c.Valor), Soma("Disponivel"), Soma("Solicitada"), Soma("Paga"));
    }
}
