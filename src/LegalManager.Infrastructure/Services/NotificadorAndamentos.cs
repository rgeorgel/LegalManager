using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Aviso de andamentos novos de um processo — o mesmo para DataJud e Escavador (webhook e polling).
/// Vai para o advogado responsável: sino (link para o processo) e e-mail, cada um conforme a
/// preferência "Publicações" dele. Só adiciona a notificação ao contexto; quem chama salva.
/// </summary>
public class NotificadorAndamentos(AppDbContext db, IEmailService email, ILogger<NotificadorAndamentos> logger)
{
    public async Task NotificarAsync(Processo processo, IReadOnlyCollection<Andamento> novos, DateTime agora, CancellationToken ct = default)
    {
        if (processo.AdvogadoResponsavelId is not { } usuarioId || novos.Count == 0) return;

        // Leitura sem criar o registro: o GetOrCreate do serviço de preferências salva o contexto,
        // o que gravaria no meio do caminho o que o chamador ainda está montando.
        var prefs = await db.PreferenciasNotificacoes.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == processo.TenantId && p.UsuarioId == usuarioId, ct)
            ?? new PreferenciasNotificacao();

        var ultimo = novos.OrderByDescending(a => a.Data).First();

        if (prefs.PublicacoesInApp)
        {
            db.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = processo.TenantId,
                UsuarioId = usuarioId,
                Tipo = TipoNotificacao.NovoAndamento,
                Titulo = novos.Count == 1
                    ? $"Novo andamento — {processo.NumeroCNJ}"
                    : $"{novos.Count} novos andamentos — {processo.NumeroCNJ}",
                Mensagem = Resumir(ultimo.Descricao, 500),
                Url = $"/pages/processo-detalhe.html?id={processo.Id}",
                Lida = false,
                CriadaEm = agora
            });
        }

        if (!prefs.PublicacoesEmail) return;

        var advogado = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.Nome, u.Email })
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(advogado?.Email)) return;

        try
        {
            await email.EnviarNovoAndamentoAsync(advogado.Email, advogado.Nome, processo.NumeroCNJ,
                ultimo.Descricao, processo.Id, novos.Count, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao enviar e-mail de novo andamento do processo {CNJ}", processo.NumeroCNJ);
        }
    }

    private static string Resumir(string texto, int max) => texto.Length > max ? texto[..max] + "…" : texto;
}
