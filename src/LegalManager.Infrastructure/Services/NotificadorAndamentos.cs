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
/// preferência "Publicações" dele. A notificação do sino só entra no contexto; quem chama salva.
/// </summary>
public class NotificadorAndamentos(AppDbContext db, IEmailService email, ILogger<NotificadorAndamentos> logger)
{
    /// <summary>Sino e e-mail na hora — para os jobs, que já juntam os andamentos do processo numa execução.</summary>
    public async Task NotificarAsync(Processo processo, IReadOnlyCollection<Andamento> novos, DateTime agora, CancellationToken ct = default)
    {
        var email = await PrepararAsync(processo, novos, agora, ct: ct);
        if (email != null) await EnviarEmailAsync(email, ct);
    }

    /// <summary>
    /// Põe a notificação do sino no contexto e devolve o e-mail a enviar (null se não há), para quem
    /// chama mandar só depois de salvar — o webhook que falha ao gravar é reenviado pelo Escavador,
    /// e o e-mail mandado antes do SaveChanges saía de novo. Com <paramref name="chaveDedup"/>, a
    /// notificação leva a chave (índice único): o segundo aviso com a mesma chave não é criado, e o
    /// que chega ao mesmo tempo derruba o SaveChanges em vez de mandar outro e-mail.
    /// </summary>
    public async Task<EmailAndamento?> PrepararAsync(
        Processo processo, IReadOnlyCollection<Andamento> novos, DateTime agora, string? chaveDedup = null,
        CancellationToken ct = default)
    {
        if (processo.AdvogadoResponsavelId is not { } usuarioId || novos.Count == 0) return null;
        if (chaveDedup != null && await db.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveDedup, ct)) return null;

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
                CriadaEm = agora,
                ChaveDedup = chaveDedup
            });
        }

        if (!prefs.PublicacoesEmail) return null;

        var advogado = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.Nome, u.Email })
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(advogado?.Email)) return null;

        return new EmailAndamento(advogado.Email, advogado.Nome, processo.NumeroCNJ, ultimo.Descricao, processo.Id, novos.Count);
    }

    public async Task EnviarEmailAsync(EmailAndamento e, CancellationToken ct = default)
    {
        try
        {
            await email.EnviarNovoAndamentoAsync(e.Para, e.Nome, e.NumeroCnj, e.Descricao, e.ProcessoId, e.Quantidade, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao enviar e-mail de novo andamento do processo {CNJ}", e.NumeroCnj);
        }
    }

    private static string Resumir(string texto, int max) => texto.Length > max ? texto[..max] + "…" : texto;
}

/// <summary>E-mail de andamento novo, montado antes de salvar e enviado depois.</summary>
public sealed record EmailAndamento(string Para, string Nome, string NumeroCnj, string Descricao, Guid ProcessoId, int Quantidade);
