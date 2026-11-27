using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class RequerimentoInssService(
    AppDbContext db, ITenantContext tenant, ITarefaService tarefas, IEventoService eventos) : IRequerimentoInssService
{
    public async Task<RequerimentoInssResponseDto?> ObterAsync(Guid processoId, CancellationToken ct = default)
    {
        var processo = await CarregarAsync(processoId, ct);
        return processo.RequerimentoInss is null ? null : await ParaDtoAsync(processo, processo.RequerimentoInss, ct);
    }

    public async Task<RequerimentoInssResponseDto> SalvarAsync(Guid processoId, RequerimentoInssDto dto, CancellationToken ct = default)
    {
        var processo = await CarregarAsync(processoId, ct);
        if (processo.Tipo != TipoProcesso.Administrativo)
            throw new InvalidOperationException("Os dados do INSS são de processo administrativo.");

        var r = processo.RequerimentoInss;
        if (r is null)
        {
            r = new RequerimentoInss { ProcessoId = processo.Id, TenantId = processo.TenantId };
            db.RequerimentosInss.Add(r);
        }
        Aplicar(r, dto);
        r.AtualizadoEm = DateTime.UtcNow;
        processo.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ParaDtoAsync(processo, r, ct);
    }

    public async Task<SugestaoInssCriadaDto> CriarSugestaoAsync(Guid processoId, string chave, CancellationToken ct = default)
    {
        var processo = await CarregarAsync(processoId, ct);
        if (processo.RequerimentoInss is null) throw new KeyNotFoundException("Processo sem dados do INSS.");

        var sugestao = (await PendentesAsync(processo, processo.RequerimentoInss, ct)).FirstOrDefault(s => s.Chave == chave)
            ?? throw new KeyNotFoundException("Sugestão não encontrada ou já criada.");
        var responsavel = processo.AdvogadoResponsavelId ?? tenant.UserId;

        if (sugestao.Tipo == "evento")
        {
            var evento = await eventos.CreateAsync(new CreateEventoDto(
                sugestao.Titulo, TipoEvento.Pericia, sugestao.Data, null, processo.RequerimentoInss.PericiaLocal,
                responsavel, processo.Id, sugestao.Memoria), ct);
            return new SugestaoInssCriadaDto("evento", evento.Id);
        }

        // Só a data final: com início + dias o TarefaService recalcularia em dias de prazo processual.
        var tarefa = await tarefas.CreateAsync(new CreateTarefaDto(
            sugestao.Titulo, sugestao.Memoria, responsavel, sugestao.Data, PrioridadeTarefa.Alta,
            processo.Id, null, null, TipoTarefa.Prazo), ct);
        return new SugestaoInssCriadaDto("prazo", tarefa.Id);
    }

    /// <summary>Copia o DTO para a entidade, validando e limpando os textos.</summary>
    public static void Aplicar(RequerimentoInss r, RequerimentoInssDto dto)
    {
        if (dto.Especie is { } especie && !EspeciesBeneficioInss.Valida(especie))
            throw new InvalidOperationException($"Espécie de benefício B{especie} desconhecida.");
        if (dto.Dib is { } dib && dto.Dcb is { } dcb && dcb.Date < dib.Date)
            throw new InvalidOperationException("A DCB não pode ser anterior à DIB.");
        if (dto.Rmi < 0)
            throw new InvalidOperationException("A RMI não pode ser negativa.");

        r.Especie = dto.Especie;
        r.NumeroBeneficio = Limpar(dto.NumeroBeneficio);
        r.Unidade = Limpar(dto.Unidade);
        r.Situacao = dto.Situacao;
        r.DataCiencia = Dia(dto.DataCiencia);
        r.Dib = Dia(dto.Dib);
        r.Dcb = Dia(dto.Dcb);
        r.Rmi = dto.Rmi;
        r.NumeroRecurso = Limpar(dto.NumeroRecurso);
        r.InstanciaRecurso = dto.InstanciaRecurso;
        r.PericiaEm = dto.PericiaEm is { } p ? DateTime.SpecifyKind(p, DateTimeKind.Utc) : null;
        r.PericiaLocal = Limpar(dto.PericiaLocal);
    }

    private async Task<Processo> CarregarAsync(Guid processoId, CancellationToken ct) =>
        await db.Processos
            .Include(p => p.RequerimentoInss)
            .FirstOrDefaultAsync(p => p.Id == processoId && p.TenantId == tenant.TenantId, ct)
        ?? throw new KeyNotFoundException("Processo não encontrado.");

    private async Task<RequerimentoInssResponseDto> ParaDtoAsync(Processo p, RequerimentoInss r, CancellationToken ct) =>
        new(p.Id, r.Especie, r.Especie is { } e ? EspeciesBeneficioInss.Rotulo(e) : null, r.NumeroBeneficio, r.Unidade,
            r.Situacao, r.DataCiencia, r.Dib, r.Dcb, r.Rmi, r.NumeroRecurso, r.InstanciaRecurso,
            r.PericiaEm, r.PericiaLocal, p.DataAjuizamento, await PendentesAsync(p, r, ct));

    /// <summary>Sugestões ainda não criadas: some a que já tem tarefa (mesmo título e dia) ou evento.</summary>
    private async Task<IReadOnlyList<SugestaoPrazoInssDto>> PendentesAsync(Processo p, RequerimentoInss r, CancellationToken ct)
    {
        var hoje = FusoHorario.HojeParede(await db.DoTenantAsync(p.TenantId, ct));
        var sugestoes = PrazosInss.Sugerir(r, hoje);
        if (sugestoes.Count == 0) return sugestoes;

        var tarefasDoProcesso = await db.Tarefas.AsNoTracking()
            .Where(t => t.ProcessoId == p.Id && t.TenantId == p.TenantId && t.Prazo != null)
            .Select(t => new { t.Titulo, Prazo = t.Prazo!.Value })
            .ToListAsync(ct);
        var pericias = await db.Eventos.AsNoTracking()
            .Where(e => e.ProcessoId == p.Id && e.TenantId == p.TenantId && e.Tipo == TipoEvento.Pericia)
            .Select(e => e.DataHora)
            .ToListAsync(ct);

        return sugestoes.Where(s => s.Tipo == "evento"
                ? !pericias.Any(d => PrazosInss.Parede(d) == s.Data)
                : !tarefasDoProcesso.Any(t => t.Titulo == s.Titulo && PrazosInss.Parede(t.Prazo).Date == s.Data.Date))
            .ToList();
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    // Datas puras: meia-noite, Kind=Utc (como as demais datas sem hora do sistema).
    private static DateTime? Dia(DateTime? d) => d is { } v ? DateTime.SpecifyKind(v.Date, DateTimeKind.Utc) : null;
}
