using System.Globalization;
using System.Text.Json;
using LegalManager.Application.DTOs.Assistente;
using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Timesheet;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using F = LegalManager.Infrastructure.Assistente.AssistenteFerramentas;

namespace LegalManager.Infrastructure.Assistente;

/// <summary>
/// O que a confirmação guarda para permitir desfazer: o registro como era antes (alterações),
/// o id do que foi criado (cadastros) e o AtualizadoEm deixado pela confirmação.
/// </summary>
internal sealed record DadosDesfazer(JsonElement? Antes, Guid? CriadoId, DateTime? AtualizadoEm);

/// <summary>
/// Executa as alterações propostas pelo assistente (<see cref="AssistenteFerramentas"/>) quando o
/// usuário clica em "Confirmar" no chat — e as desfaz, se ele pedir logo em seguida. Grava pelos
/// mesmos serviços das telas e registra na auditoria, como os controllers. Só quem fez a pergunta
/// (na mesma sessão de identidade) confirma, cancela ou desfaz.
/// </summary>
public class AssistenteAcoes(
    AppDbContext db,
    ITenantContext tenant,
    IContatoService contatos,
    ITarefaService tarefas,
    IEventoService eventos,
    ITimesheetService timesheet,
    IAuditService audit,
    ILogger<AssistenteAcoes> logger) : IAssistenteAcoesService
{
    /// <summary>Tempo para confirmar: depois disso os dados mostrados podem estar desatualizados.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);

    /// <summary>Tempo, depois de confirmar, em que dá para desfazer pelo chat.</summary>
    public static readonly TimeSpan JanelaDesfazer = TimeSpan.FromMinutes(30);

    private sealed record Resultado(Guid? EntidadeId, object? Antes = null, Guid? CriadoId = null);

    // ── Confirmar ────────────────────────────────────────────────────────────

    public async Task<AssistenteAcaoDto?> ConfirmarAsync(Guid acaoId, string? ip, CancellationToken ct = default)
    {
        var acao = await DoUsuarioAsync(acaoId, ct);
        if (acao is null) return null;
        // Já resolvida (ex.: clique duplo) ou expirada: só devolve a situação atual.
        if (acao.Status != StatusAcaoAssistente.Pendente || acao.ExpiraEm < DateTime.UtcNow) return ParaDto(acao);

        // Marca antes de executar: Status é token de concorrência, então duas confirmações
        // simultâneas não gravam duas vezes.
        acao.ResolvidaEm = DateTime.UtcNow;
        if (!await MarcarAsync(acao, StatusAcaoAssistente.Confirmada, ct)) return ParaDto(acao);

        try
        {
            var r = acao.Tipo switch
            {
                F.AtualizarContato => await AtualizarContatoAsync(Dados<DadosAtualizarContato>(acao), ip, ct),
                F.CriarTarefa => await CriarTarefaAsync(Dados<DadosCriarTarefa>(acao), ip, ct),
                F.AlterarPrazoTarefa => await AlterarPrazoAsync(Dados<DadosAlterarPrazo>(acao), ip, ct),
                F.AtualizarTarefa => await AtualizarTarefaAsync(Dados<DadosAtualizarTarefa>(acao), ip, ct),
                F.CriarEvento => await CriarEventoAsync(Dados<DadosCriarEvento>(acao), ip, ct),
                F.RemarcarEvento => await RemarcarEventoAsync(Dados<DadosRemarcarEvento>(acao), ip, ct),
                F.RegistrarAtendimento => await RegistrarAtendimentoAsync(Dados<DadosRegistrarAtendimento>(acao), ip, ct),
                F.RegistrarHoras => await RegistrarHorasAsync(Dados<DadosRegistrarHoras>(acao), ip, ct),
                _ => throw new InvalidOperationException($"Ação desconhecida: {acao.Tipo}.")
            };
            acao.EntidadeId = r.EntidadeId;
            var antes = r.Antes is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(r.Antes, F.JsonDados);
            acao.DesfazerJson = JsonSerializer.Serialize(
                new DadosDesfazer(antes, r.CriadoId, await AtualizadoEmAsync(acao.Tipo, r.CriadoId ?? r.EntidadeId, ct)), F.JsonDados);
        }
        catch (Exception ex) when (Esperada(ex))
        {
            logger.LogWarning(ex, "Assistente IA: falha ao executar a ação {Id} ({Tipo})", acao.Id, acao.Tipo);
            Descartar(acao);
            acao.Status = StatusAcaoAssistente.Falhou;
            acao.Erro = Mensagem(ex, "Não foi possível gravar a alteração. Faça-a pela tela do sistema.");
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return ParaDto(acao);
    }

    public async Task<AssistenteAcaoDto?> CancelarAsync(Guid acaoId, CancellationToken ct = default)
    {
        var acao = await DoUsuarioAsync(acaoId, ct);
        if (acao is null) return null;
        if (acao.Status != StatusAcaoAssistente.Pendente) return ParaDto(acao);

        acao.ResolvidaEm = DateTime.UtcNow;
        await MarcarAsync(acao, StatusAcaoAssistente.Cancelada, ct);
        return ParaDto(acao);
    }

    // ── Desfazer ─────────────────────────────────────────────────────────────

    public async Task<AssistenteAcaoDto?> DesfazerAsync(Guid acaoId, string? ip, CancellationToken ct = default)
    {
        var acao = await DoUsuarioAsync(acaoId, ct);
        if (acao is null) return null;
        if (DesfazerAte(acao) is not { } ate || ate < DateTime.UtcNow)
        {
            if (acao.Status == StatusAcaoAssistente.Confirmada)
                return ParaDto(acao) with { Erro = "O prazo para desfazer pelo chat terminou. Altere pela tela do sistema." };
            return ParaDto(acao);
        }
        var d = JsonSerializer.Deserialize<DadosDesfazer>(acao.DesfazerJson!, F.JsonDados)!;

        // Alguém mexeu no registro depois da confirmação: voltar os valores apagaria essa mudança.
        if (await AtualizadoEmAsync(acao.Tipo, d.CriadoId ?? acao.EntidadeId, ct) != d.AtualizadoEm)
            return ParaDto(acao) with { Erro = "O registro foi alterado depois da confirmação. Desfaça pela tela do sistema." };

        if (!await MarcarAsync(acao, StatusAcaoAssistente.Desfeita, ct)) return ParaDto(acao);
        try
        {
            switch (acao.Tipo)
            {
                case F.AtualizarContato:
                    await RestaurarContatoAsync(Antes<ContatoResponseDto>(d), ip, ct);
                    break;
                case F.AlterarPrazoTarefa or F.AtualizarTarefa:
                    var tarefa = Antes<TarefaResponseDto>(d);
                    var atual = await tarefas.GetByIdAsync(tarefa.Id, ct) ?? throw new KeyNotFoundException("A tarefa não existe mais.");
                    var restaurada = await GravarTarefaAsync(tarefa, ct);
                    await audit.LogAsync(tenant.CreateEntry(AuditActions.Update, AuditEntities.Tarefa, tarefa.Id, atual, restaurada, ip), ct);
                    break;
                case F.RemarcarEvento:
                    await RestaurarEventoAsync(Antes<EventoResponseDto>(d), ip, ct);
                    break;
                case F.CriarTarefa:
                    var criada = await tarefas.GetByIdAsync(d.CriadoId!.Value, ct) ?? throw new KeyNotFoundException("A tarefa já foi excluída.");
                    await tarefas.DeleteAsync(criada.Id, ct);
                    await audit.LogAsync(tenant.CreateEntry(AuditActions.Delete, AuditEntities.Tarefa, criada.Id, criada, null, ip), ct);
                    break;
                case F.CriarEvento:
                    var evento = await eventos.GetByIdAsync(d.CriadoId!.Value, ct) ?? throw new KeyNotFoundException("O evento já foi excluído.");
                    await eventos.DeleteAsync(evento.Id, ct);
                    await audit.LogAsync(tenant.CreateEntry(AuditActions.Delete, AuditEntities.Evento, evento.Id, evento, null, ip), ct);
                    break;
                case F.RegistrarAtendimento:
                    var atendimento = await db.Atendimentos
                        .FirstOrDefaultAsync(x => x.Id == d.CriadoId && x.TenantId == tenant.TenantId, ct)
                        ?? throw new KeyNotFoundException("O atendimento já foi excluído.");
                    db.Atendimentos.Remove(atendimento);
                    await db.SaveChangesAsync(ct);
                    await audit.LogAsync(tenant.CreateEntry(AuditActions.Delete, AuditEntities.Atendimento, atendimento.Id,
                        new { atendimento.ContatoId, atendimento.Data, atendimento.Descricao }, null, ip), ct);
                    break;
                case F.RegistrarHoras:
                    var registro = await timesheet.GetByIdAsync(d.CriadoId!.Value, tenant.TenantId, ct)
                        ?? throw new KeyNotFoundException("O lançamento de horas já foi excluído.");
                    await timesheet.DeletarAsync(registro.Id, tenant.TenantId, ct);
                    await audit.LogAsync(tenant.CreateEntry(AuditActions.Delete, AuditEntities.RegistroTempo, registro.Id, registro, null, ip), ct);
                    break;
                default:
                    throw new InvalidOperationException($"Ação desconhecida: {acao.Tipo}.");
            }
            acao.DesfeitaEm = DateTime.UtcNow;
            acao.Erro = null;
        }
        catch (Exception ex) when (Esperada(ex))
        {
            logger.LogWarning(ex, "Assistente IA: falha ao desfazer a ação {Id} ({Tipo})", acao.Id, acao.Tipo);
            Descartar(acao);
            acao.Status = StatusAcaoAssistente.Confirmada;
            acao.Erro = Mensagem(ex, "Não foi possível desfazer. Altere pela tela do sistema.");
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return ParaDto(acao);
    }

    // ── Execução por tipo ────────────────────────────────────────────────────

    private async Task<Resultado> AtualizarContatoAsync(DadosAtualizarContato d, string? ip, CancellationToken ct)
    {
        var atual = await contatos.GetByIdAsync(d.ContatoId, ct) ?? throw new KeyNotFoundException("O contato não existe mais.");
        string? V(string campo, string? valorAtual) => d.Campos.TryGetValue(campo, out var novo) ? novo : valorAtual;

        var nascimento = V(nameof(Contato.DataNascimento), atual.DataNascimento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var tags = atual.Tags
            .Where(t => !d.TagsRemover.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Concat(d.TagsAdicionar.Where(t => !atual.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        var dto = new UpdateContatoDto(
            Enum.Parse<TipoPessoa>(V(nameof(Contato.Tipo), atual.Tipo.ToString())!),
            Enum.Parse<TipoContato>(V(nameof(Contato.TipoContato), atual.TipoContato.ToString())!),
            V(nameof(Contato.Nome), atual.Nome)!,
            V(nameof(Contato.CpfCnpj), atual.CpfCnpj),
            V(nameof(Contato.Oab), atual.Oab),
            V(nameof(Contato.Email), atual.Email),
            V(nameof(Contato.Telefone), atual.Telefone),
            V(nameof(Contato.Endereco), atual.Endereco),
            V(nameof(Contato.Cidade), atual.Cidade),
            V(nameof(Contato.Estado), atual.Estado),
            V(nameof(Contato.Cep), atual.Cep),
            nascimento is null ? null : DateTime.ParseExact(nascimento, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            V(nameof(Contato.Observacoes), atual.Observacoes),
            atual.NotificacaoHabilitada,
            tags);

        var result = await contatos.UpdateAsync(d.ContatoId, dto, ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Update, AuditEntities.Contato, d.ContatoId, atual, result, ip), ct);
        return new Resultado(d.ContatoId, atual);
    }

    private async Task RestaurarContatoAsync(ContatoResponseDto antes, string? ip, CancellationToken ct)
    {
        var atual = await contatos.GetByIdAsync(antes.Id, ct) ?? throw new KeyNotFoundException("O contato não existe mais.");
        var result = await contatos.UpdateAsync(antes.Id, new UpdateContatoDto(
            antes.Tipo, antes.TipoContato, antes.Nome, antes.CpfCnpj, antes.Oab, antes.Email, antes.Telefone,
            antes.Endereco, antes.Cidade, antes.Estado, antes.Cep, antes.DataNascimento, antes.Observacoes,
            antes.NotificacaoHabilitada, antes.Tags), ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Update, AuditEntities.Contato, antes.Id, atual, result, ip), ct);
    }

    private async Task<Resultado> CriarTarefaAsync(DadosCriarTarefa d, string? ip, CancellationToken ct)
    {
        var dto = new CreateTarefaDto(d.Titulo, d.Descricao, d.ResponsavelId, d.Prazo, d.Prioridade,
            d.ProcessoId, d.ContatoId, d.Tags.Count > 0 ? d.Tags : null, d.Tipo);
        var result = await tarefas.CreateAsync(dto, ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Create, AuditEntities.Tarefa, result.Id, null, dto, ip), ct);
        return new Resultado(result.Id, CriadoId: result.Id);
    }

    private async Task<Resultado> AlterarPrazoAsync(DadosAlterarPrazo d, string? ip, CancellationToken ct)
    {
        var atual = await tarefas.GetByIdAsync(d.TarefaId, ct) ?? throw new KeyNotFoundException("A tarefa não existe mais.");
        // Prazo calculado (dias a partir de uma data) vira data fixa: senão o serviço recalcularia
        // e ignoraria a nova data.
        var result = await GravarTarefaAsync(atual with { Prazo = d.Prazo, DataInicio = null, QuantidadeDias = null, TipoCalculo = null }, ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Update, AuditEntities.Tarefa, d.TarefaId, atual, result, ip), ct);
        return new Resultado(d.TarefaId, atual);
    }

    private async Task<Resultado> AtualizarTarefaAsync(DadosAtualizarTarefa d, string? ip, CancellationToken ct)
    {
        var atual = await tarefas.GetByIdAsync(d.TarefaId, ct) ?? throw new KeyNotFoundException("A tarefa não existe mais.");
        var result = await GravarTarefaAsync(atual with
        {
            Titulo = d.Titulo ?? atual.Titulo,
            Descricao = d.AlteraDescricao ? d.Descricao : atual.Descricao,
            Prioridade = d.Prioridade ?? atual.Prioridade,
            Status = d.Status ?? atual.Status,
            ResponsavelId = d.ResponsavelId ?? atual.ResponsavelId
        }, ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Update, AuditEntities.Tarefa, d.TarefaId, atual, result, ip), ct);
        return new Resultado(d.TarefaId, atual);
    }

    /// <summary>
    /// Grava a tarefa exatamente com os valores dados. O serviço recalcula o prazo quando há
    /// DataInicio/QuantidadeDias (sem os feriados adicionais da criação): por isso grava com o
    /// prazo fixo e devolve os campos do cálculo direto na entidade.
    /// </summary>
    private async Task<TarefaResponseDto> GravarTarefaAsync(TarefaResponseDto t, CancellationToken ct)
    {
        var result = await tarefas.UpdateAsync(t.Id, new UpdateTarefaDto(t.Titulo, t.Descricao, t.ResponsavelId, t.Prazo,
            t.Prioridade, t.Status, t.ProcessoId, t.ContatoId, t.Tags, t.Tipo), ct);
        if (t.DataInicio is null && t.QuantidadeDias is null && t.TipoCalculo is null) return result;

        var entidade = await db.Tarefas.FirstAsync(x => x.Id == t.Id && x.TenantId == tenant.TenantId, ct);
        entidade.DataInicio = t.DataInicio;
        entidade.QuantidadeDias = t.QuantidadeDias;
        entidade.TipoCalculo = t.TipoCalculo;
        await db.SaveChangesAsync(ct);
        return result with { DataInicio = t.DataInicio, QuantidadeDias = t.QuantidadeDias, TipoCalculo = t.TipoCalculo };
    }

    private async Task<Resultado> CriarEventoAsync(DadosCriarEvento d, string? ip, CancellationToken ct)
    {
        var dto = new CreateEventoDto(d.Titulo, d.Tipo, d.Inicio, d.Fim, d.Local, d.ResponsavelId, d.ProcessoId, d.Observacoes);
        var result = await eventos.CreateAsync(dto, ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Create, AuditEntities.Evento, result.Id, null, dto, ip), ct);
        return new Resultado(result.Id, CriadoId: result.Id);
    }

    private async Task<Resultado> RemarcarEventoAsync(DadosRemarcarEvento d, string? ip, CancellationToken ct)
    {
        var atual = await eventos.GetByIdAsync(d.EventoId, ct) ?? throw new KeyNotFoundException("O evento não existe mais.");
        var result = await eventos.UpdateAsync(d.EventoId, new UpdateEventoDto(atual.Titulo, atual.Tipo, d.Inicio, d.Fim,
            atual.Local, atual.ResponsavelId, atual.ProcessoId, atual.Observacoes), ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Update, AuditEntities.Evento, d.EventoId, atual, result, ip), ct);
        return new Resultado(d.EventoId, atual);
    }

    private async Task RestaurarEventoAsync(EventoResponseDto antes, string? ip, CancellationToken ct)
    {
        var atual = await eventos.GetByIdAsync(antes.Id, ct) ?? throw new KeyNotFoundException("O evento não existe mais.");
        var result = await eventos.UpdateAsync(antes.Id, new UpdateEventoDto(antes.Titulo, antes.Tipo, antes.DataHora, antes.DataHoraFim,
            antes.Local, antes.ResponsavelId, antes.ProcessoId, antes.Observacoes), ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Update, AuditEntities.Evento, antes.Id, atual, result, ip), ct);
    }

    private async Task<Resultado> RegistrarAtendimentoAsync(DadosRegistrarAtendimento d, string? ip, CancellationToken ct)
    {
        var dto = new CreateAtendimentoDto(d.Descricao, d.Data);
        var result = await contatos.AddAtendimentoAsync(d.ContatoId, dto, ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Create, AuditEntities.Atendimento, result.Id, null,
            new { d.ContatoId, d.Data, d.Descricao }, ip), ct);
        // EntidadeId fica no contato (link do cartão); o atendimento criado vai para o desfazer.
        return new Resultado(d.ContatoId, CriadoId: result.Id);
    }

    private async Task<Resultado> RegistrarHorasAsync(DadosRegistrarHoras d, string? ip, CancellationToken ct)
    {
        var dto = new CriarRegistroManualDto(d.InicioUtc, d.FimUtc, d.Descricao, d.ProcessoId, d.TarefaId);
        var result = await timesheet.CriarManualAsync(tenant.TenantId, tenant.UserId, dto, ct);
        await audit.LogAsync(tenant.CreateEntry(AuditActions.Create, AuditEntities.RegistroTempo, result.Id, null, dto, ip), ct);
        return new Resultado(result.Id, CriadoId: result.Id);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<AcaoAssistente?> DoUsuarioAsync(Guid id, CancellationToken ct)
    {
        var impersonadoPor = tenant.ImpersonadoPorId;
        return db.AcoesAssistente.FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenant.TenantId
            && a.UsuarioId == tenant.UserId && a.ImpersonadoPorId == impersonadoPor, ct);
    }

    /// <summary>
    /// Muda o status e grava na hora. Status é token de concorrência: se outra requisição mudou
    /// antes (clique duplo), recarrega e devolve false.
    /// </summary>
    private async Task<bool> MarcarAsync(AcaoAssistente acao, StatusAcaoAssistente status, CancellationToken ct)
    {
        acao.Status = status;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(acao).ReloadAsync(ct);
            return false;
        }
    }

    /// <summary>Uma falha de gravação deixa entidades sujas no contexto: descarta tudo menos a ação.</summary>
    private void Descartar(AcaoAssistente acao)
    {
        db.ChangeTracker.Clear();
        db.Attach(acao);
    }

    private static bool Esperada(Exception ex) =>
        ex is KeyNotFoundException or ArgumentException or InvalidOperationException or JsonException or DbUpdateException;

    private static string Mensagem(Exception ex, string generica) =>
        ex is KeyNotFoundException or ArgumentException ? ex.Message : generica;

    /// <summary>AtualizadoEm atual do registro afetado (null para os que não têm esse campo).</summary>
    private async Task<DateTime?> AtualizadoEmAsync(string tipo, Guid? id, CancellationToken ct)
    {
        if (id is not { } x) return null;
        var t = tenant.TenantId;
        return tipo switch
        {
            F.AtualizarContato => await db.Contatos.Where(c => c.Id == x && c.TenantId == t).Select(c => c.AtualizadoEm).FirstOrDefaultAsync(ct),
            F.CriarTarefa or F.AlterarPrazoTarefa or F.AtualizarTarefa =>
                await db.Tarefas.Where(c => c.Id == x && c.TenantId == t).Select(c => c.AtualizadoEm).FirstOrDefaultAsync(ct),
            F.CriarEvento or F.RemarcarEvento =>
                await db.Eventos.Where(c => c.Id == x && c.TenantId == t).Select(c => c.AtualizadoEm).FirstOrDefaultAsync(ct),
            _ => null
        };
    }

    private static T Dados<T>(AcaoAssistente acao) =>
        JsonSerializer.Deserialize<T>(acao.DadosJson, F.JsonDados) ?? throw new JsonException("Dados da ação vazios.");

    private static T Antes<T>(DadosDesfazer d) =>
        d.Antes is { } e ? e.Deserialize<T>(F.JsonDados) ?? throw new JsonException("Sem dados anteriores.")
            : throw new JsonException("Sem dados anteriores.");

    // ── DTO ──────────────────────────────────────────────────────────────────

    private static DateTime? DesfazerAte(AcaoAssistente a) =>
        a.Status == StatusAcaoAssistente.Confirmada && a.DesfazerJson is not null && a.ResolvidaEm is { } r
            ? r + JanelaDesfazer : null;

    public static AssistenteAcaoDto ParaDto(AcaoAssistente a)
    {
        var expirada = a.Status == StatusAcaoAssistente.Pendente && a.ExpiraEm < DateTime.UtcNow;
        var desfazerAte = DesfazerAte(a);
        return new AssistenteAcaoDto(
            a.Id, a.Tipo, a.Titulo, Alteracoes(a.AlteracoesJson),
            expirada ? "Expirada" : a.Status.ToString(), a.ExpiraEm, Link(a), a.Erro,
            desfazerAte > DateTime.UtcNow ? desfazerAte : null);
    }

    private static string? Link(AcaoAssistente a)
    {
        // Registros criados só existem depois de confirmados (e somem ao desfazer).
        var existe = a.Status == StatusAcaoAssistente.Confirmada;
        if (a.EntidadeId is not { } id) return null;
        return a.Tipo switch
        {
            F.AtualizarContato or F.RegistrarAtendimento => $"/pages/contato-detalhe.html?id={id}",
            F.CriarTarefa => existe ? $"/pages/tarefas.html?abrirId={id}" : null,
            F.AlterarPrazoTarefa or F.AtualizarTarefa => $"/pages/tarefas.html?abrirId={id}",
            F.CriarEvento when existe => F.LinkEvento(id, DataDoEvento(a)),
            F.RemarcarEvento => F.LinkEvento(id, DataDoEvento(a)),
            F.RegistrarHoras when existe => "/pages/timesheet.html",
            _ => null
        };
    }

    private static DateTime DataDoEvento(AcaoAssistente a)
    {
        try
        {
            using var doc = JsonDocument.Parse(a.DadosJson);
            return doc.RootElement.GetProperty("Inicio").GetDateTime();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return a.CriadoEm;
        }
    }

    private static List<AssistenteAlteracaoDto> Alteracoes(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(e => new AssistenteAlteracaoDto(
                e.GetProperty("campo").GetString() ?? "",
                e.TryGetProperty("antes", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null,
                e.TryGetProperty("depois", out var y) && y.ValueKind == JsonValueKind.String ? y.GetString() : null)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return []; }
    }
}
