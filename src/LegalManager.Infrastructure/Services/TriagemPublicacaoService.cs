using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Aceitar, ignorar ou refazer a sugestão da IA para uma publicação. O prazo e a audiência são
/// criados pelos mesmos serviços das telas e registrados na auditoria, como no assistente.
/// </summary>
public class TriagemPublicacaoService(
    AppDbContext db,
    ITenantContext tenant,
    ITarefaService tarefas,
    IEventoService eventos,
    IAuditService audit) : ITriagemPublicacaoService
{
    public async Task<TriagemPublicacaoDto> AceitarAsync(Guid publicacaoId, AceitarTriagemDto dto, string? ip, CancellationToken ct = default)
    {
        var triagem = await CarregarAsync(publicacaoId, ct) ?? throw new KeyNotFoundException("Sugestão não encontrada.");
        if (triagem.Status != StatusTriagem.Sugerida)
            throw new InvalidOperationException("Esta sugestão já foi resolvida.");

        var pub = triagem.Publicacao;
        DateTime data;
        if (dto.CriarEvento)
            data = dto.Data ?? triagem.AudienciaDataHora
                   ?? throw new ArgumentException("Informe a data e a hora da audiência.");
        else
            // Sem hora informada, o prazo fica ao meio-dia (como na calculadora de prazos).
            data = dto.Data ?? triagem.Vencimento?.Date.AddHours(12)
                   ?? throw new ArgumentException("Informe o vencimento do prazo.");
        data = DateTime.SpecifyKind(data, DateTimeKind.Utc);
        var responsavel = dto.ResponsavelId ?? pub.Processo?.AdvogadoResponsavelId ?? tenant.UserId;
        var titulo = string.IsNullOrWhiteSpace(dto.Titulo) ? null : dto.Titulo.Trim();

        // Marca antes de criar: Status é token de concorrência, então dois cliques não criam dois prazos.
        triagem.Status = StatusTriagem.Aceita;
        triagem.ResolvidaPorId = tenant.UserId;
        triagem.ResolvidaEm = DateTime.UtcNow;
        await SalvarResolucaoAsync(ct);

        try
        {
            if (dto.CriarEvento)
            {
                var evento = new CreateEventoDto(titulo ?? "Audiência", TipoEvento.Audiencia, data, null, null,
                    responsavel, pub.ProcessoId, Observacoes(triagem));
                var criado = await eventos.CreateAsync(evento, ct);
                triagem.EventoId = criado.Id;
                await audit.LogAsync(tenant.CreateEntry(AuditActions.Create, AuditEntities.Evento, criado.Id, null, evento, ip), ct);
            }
            else
            {
                // Só a data final: com início + dias o TarefaService recalcularia o prazo ao editar,
                // sem os feriados locais e o recesso usados aqui.
                var tarefa = new CreateTarefaDto(titulo ?? triagem.Ato ?? "Prazo da publicação", Observacoes(triagem),
                    responsavel, data, pub.Urgente ? PrioridadeTarefa.Urgente : PrioridadeTarefa.Alta,
                    pub.ProcessoId, null, null, TipoTarefa.Prazo);
                var criada = await tarefas.CreateAsync(tarefa, ct);
                triagem.TarefaId = criada.Id;
                await audit.LogAsync(tenant.CreateEntry(AuditActions.Create, AuditEntities.Tarefa, criada.Id, null, tarefa, ip), ct);
            }
        }
        catch
        {
            // Desfaz a marcação; o que o serviço deixou no contexto sem gravar não vai junto.
            foreach (var e in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                e.State = EntityState.Detached;
            triagem.Status = StatusTriagem.Sugerida;
            triagem.ResolvidaPorId = null;
            triagem.ResolvidaEm = null;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        if (pub.Status == StatusPublicacao.Nova) pub.Status = StatusPublicacao.Lida;
        await db.SaveChangesAsync(ct);
        return ParaDto(triagem);
    }

    public async Task<TriagemPublicacaoDto> IgnorarAsync(Guid publicacaoId, CancellationToken ct = default)
    {
        var triagem = await CarregarAsync(publicacaoId, ct) ?? throw new KeyNotFoundException("Sugestão não encontrada.");
        if (triagem.Status != StatusTriagem.Sugerida)
            throw new InvalidOperationException("Esta sugestão já foi resolvida.");
        triagem.Status = StatusTriagem.Ignorada;
        triagem.ResolvidaPorId = tenant.UserId;
        triagem.ResolvidaEm = DateTime.UtcNow;
        await SalvarResolucaoAsync(ct);
        return ParaDto(triagem);
    }

    public async Task<TriagemPublicacaoDto> RefazerAsync(Guid publicacaoId, CancellationToken ct = default)
    {
        var pub = await db.Publicacoes
            .FirstOrDefaultAsync(p => p.Id == publicacaoId && p.TenantId == tenant.TenantId, ct)
            ?? throw new KeyNotFoundException("Publicação não encontrada.");
        var triagem = await db.TriagensPublicacao.FirstOrDefaultAsync(t => t.PublicacaoId == pub.Id, ct);
        if (triagem is null)
        {
            triagem = new TriagemPublicacao
            {
                Id = Guid.NewGuid(),
                TenantId = pub.TenantId,
                PublicacaoId = pub.Id,
                CriadoEm = DateTime.UtcNow
            };
            db.TriagensPublicacao.Add(triagem);
        }
        else if (triagem.Status is StatusTriagem.Aceita)
        {
            throw new InvalidOperationException("O prazo desta publicação já foi criado.");
        }
        else if (triagem.Status is StatusTriagem.Pendente)
        {
            return ParaDto(triagem);
        }

        triagem.Status = StatusTriagem.Pendente;
        triagem.Tentativas = 0;
        triagem.Erro = null;
        triagem.ProcessadaEm = null;
        triagem.Resumo = triagem.Ato = triagem.PrazoTrecho = triagem.AudienciaTrecho = triagem.MemoriaCalculo = null;
        triagem.PrazoDias = null;
        triagem.TipoCalculo = null;
        triagem.DataPublicacao = triagem.Vencimento = triagem.AudienciaDataHora = null;
        triagem.ResolvidaPorId = null;
        triagem.ResolvidaEm = null;
        await SalvarResolucaoAsync(ct);
        return ParaDto(triagem);
    }

    private Task<TriagemPublicacao?> CarregarAsync(Guid publicacaoId, CancellationToken ct) =>
        db.TriagensPublicacao
            .Include(t => t.Publicacao).ThenInclude(p => p.Processo)
            .FirstOrDefaultAsync(t => t.PublicacaoId == publicacaoId && t.TenantId == tenant.TenantId, ct);

    private async Task SalvarResolucaoAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Esta sugestão acabou de ser resolvida por outra pessoa. Atualize a página.");
        }
    }

    private static string Observacoes(TriagemPublicacao t)
    {
        var pub = t.Publicacao;
        var linhas = new List<string>
        {
            $"Criado a partir da sugestão da IA para a publicação de {pub.DataPublicacao.ToString("dd/MM/yyyy", BrasiliaTime.PtBr)} ({pub.Diario})."
        };
        if (t.Resumo is not null) linhas.Add($"Resumo: {t.Resumo}");
        if (t.MemoriaCalculo is not null) linhas.Add($"Cálculo: {t.MemoriaCalculo}");
        var trecho = t.PrazoTrecho ?? t.AudienciaTrecho;
        if (trecho is not null) linhas.Add($"Trecho: “{trecho}”");
        linhas.Add("Confira o prazo na publicação antes de protocolar.");
        return string.Join("\n", linhas);
    }

    public static TriagemPublicacaoDto ParaDto(TriagemPublicacao t) => new(
        t.Status, t.Resumo, t.Ato, t.PrazoDias, t.TipoCalculo, t.PrazoTrecho, t.DataPublicacao, t.Vencimento,
        t.MemoriaCalculo, t.AudienciaDataHora, t.AudienciaTrecho, t.TarefaId, t.EventoId, t.Erro);
}
