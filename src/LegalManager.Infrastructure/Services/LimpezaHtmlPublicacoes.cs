using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Limpa o HTML das publicações e andamentos automáticos gravados antes de toda entrada passar por
/// <see cref="TextoHtml"/> (o fluxo OAB do webhook gravava o trecho do Escavador como veio).
/// Roda ao subir a API; idempotente — depois da primeira vez não acha nada para mudar.
/// </summary>
public static class LimpezaHtmlPublicacoes
{
    public static async Task<int> ExecutarAsync(AppDbContext db, CancellationToken ct = default)
    {
        var alteradas = 0;

        var publicacoes = await db.Publicacoes
            .Where(p => p.Conteudo.Contains("<") || p.Conteudo.Contains("&")
                        || (p.Snippet != null && (p.Snippet.Contains("<") || p.Snippet.Contains("&"))))
            .ToListAsync(ct);
        foreach (var p in publicacoes)
        {
            var mudou = false;
            if (TextoHtml.TemHtml(p.Conteudo))
            {
                p.Conteudo = TextoHtml.ParaTexto(p.Conteudo) ?? "";
                mudou = true;
            }
            if (TextoHtml.TemHtml(p.Snippet))
            {
                p.Snippet = TextoHtml.ParaTexto(p.Snippet);
                mudou = true;
            }
            if (mudou) alteradas++;
        }

        var andamentos = await db.Andamentos
            .Where(a => a.Fonte == FonteAndamento.Automatico && (a.Descricao.Contains("<") || a.Descricao.Contains("&")))
            .ToListAsync(ct);
        foreach (var a in andamentos.Where(a => TextoHtml.TemHtml(a.Descricao)))
        {
            a.Descricao = TextoHtml.ParaTexto(a.Descricao) ?? "Atualização via Escavador";
            alteradas++;
        }

        if (alteradas > 0) await db.SaveChangesAsync(ct);
        return alteradas;
    }
}
