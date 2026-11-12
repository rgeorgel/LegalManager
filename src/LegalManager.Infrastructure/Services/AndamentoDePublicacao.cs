using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// A movimentação que uma publicação de diário gera no processo do escritório. O webhook do
/// monitoramento de processo já cria a sua; as que chegam pela OAB (webhook e busca de 30 dias)
/// passam por aqui, para o processo e o "Últimas movimentações" do dashboard verem a publicação.
/// A descrição é o próprio texto da publicação (já sem HTML) — é também a chave que evita repetir.
/// </summary>
public static class AndamentoDePublicacao
{
    public static Andamento Montar(Publicacao publicacao, Guid processoId, DateTime criadoEm) => new()
    {
        Id = Guid.NewGuid(),
        ProcessoId = processoId,
        TenantId = publicacao.TenantId,
        Data = publicacao.DataPublicacao,
        Tipo = Tipo(publicacao.Tipo),
        Descricao = Descricao(publicacao),
        Fonte = FonteAndamento.Automatico,
        CriadoEm = criadoEm
    };

    public static string Descricao(Publicacao p) =>
        !string.IsNullOrWhiteSpace(p.Conteudo) ? p.Conteudo
        : !string.IsNullOrWhiteSpace(p.Snippet) ? p.Snippet
        : "Publicação no diário oficial";

    public static TipoAndamento Tipo(TipoPublicacao tipo) => tipo switch
    {
        TipoPublicacao.Decisao => TipoAndamento.Decisao,
        TipoPublicacao.Despacho => TipoAndamento.Despacho,
        TipoPublicacao.Intimacao => TipoAndamento.Intimacao,
        TipoPublicacao.Audiencia => TipoAndamento.Audiencia,
        _ => TipoAndamento.Publicacao
    };

    /// <summary>Leva a data do andamento para o processo, se for a mais recente.</summary>
    public static void AtualizarUltimoAndamento(Processo processo, DateTime data)
    {
        if (data > (processo.UltimoAndamentoEm ?? DateTime.MinValue))
            processo.UltimoAndamentoEm = data;
    }

    /// <summary>
    /// Para as publicações gravadas antes desta regra: vincula as do webhook da OAB ao processo do
    /// escritório com o mesmo CNJ e cria a movimentação delas e das da busca de 30 dias. Roda ao subir
    /// a API; idempotente (a movimentação com a mesma data e texto não é criada de novo).
    /// </summary>
    public static async Task<int> CriarFaltantesAsync(AppDbContext db, CancellationToken ct = default)
    {
        var candidatas = await db.Publicacoes
            .Where(p => p.FonteCaptura == FonteCaptura.Escavador && p.Conteudo != ""
                        && ((p.ProcessoId == null && p.NumeroCNJ != null
                             && db.Processos.Any(pr => pr.TenantId == p.TenantId && pr.NumeroCNJ == p.NumeroCNJ))
                            || (p.ProcessoId != null && p.UuidExterno != null && p.UuidExterno.StartsWith("busca-"))))
            .Where(p => !db.Andamentos.Any(a => a.TenantId == p.TenantId && a.Data == p.DataPublicacao
                                                && a.Descricao == p.Conteudo
                                                && a.Processo.NumeroCNJ == p.NumeroCNJ))
            .ToListAsync(ct);
        if (candidatas.Count == 0) return 0;

        var tenants = candidatas.Select(p => p.TenantId).Distinct().ToList();
        var cnjs = candidatas.Where(p => p.NumeroCNJ != null).Select(p => p.NumeroCNJ!).Distinct().ToList();
        var ids = candidatas.Where(p => p.ProcessoId != null).Select(p => p.ProcessoId!.Value).Distinct().ToList();
        var processos = await db.Processos
            .Where(pr => ids.Contains(pr.Id) || (tenants.Contains(pr.TenantId) && cnjs.Contains(pr.NumeroCNJ)))
            .ToListAsync(ct);

        var criadas = 0;
        foreach (var pub in candidatas)
        {
            var processo = pub.ProcessoId is { } id
                ? processos.FirstOrDefault(pr => pr.Id == id)
                : processos.FirstOrDefault(pr => pr.TenantId == pub.TenantId && pr.NumeroCNJ == pub.NumeroCNJ);
            if (processo == null) continue;

            pub.ProcessoId = processo.Id;
            db.Andamentos.Add(Montar(pub, processo.Id, pub.CapturaEm));
            AtualizarUltimoAndamento(processo, pub.DataPublicacao);
            criadas++;
        }

        await db.SaveChangesAsync(ct);
        return criadas;
    }
}
