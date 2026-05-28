using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace LegalManager.Infrastructure.Services;

public class ResumoProcessoService : IResumoProcessoService
{
    private readonly AppDbContext _context;
    private readonly IIAService _iaService;
    private readonly ICreditoService _creditoService;
    private readonly ITenantContext _tenantContext;

    public ResumoProcessoService(
        AppDbContext context,
        IIAService iaService,
        ICreditoService creditoService,
        ITenantContext tenantContext)
    {
        _context = context;
        _iaService = iaService;
        _creditoService = creditoService;
        _tenantContext = tenantContext;
    }

    public async Task<ResumoProcessoResponseDto> GerarAsync(GerarResumoDto dto, Guid usuarioId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        if (!await _creditoService.TemCreditoDisponivelAsync(TipoCreditoAI.TraducaoAndamento, 1, ct))
            throw new InvalidOperationException("Créditos de IA insuficientes.");

        var processo = await _context.Processos
            .Include(p => p.Partes).ThenInclude(pt => pt.Contato)
            .FirstOrDefaultAsync(p => p.Id == dto.ProcessoId && p.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("Processo não encontrado.");

        var andamentos = await _context.Andamentos
            .Where(a => a.ProcessoId == dto.ProcessoId)
            .OrderByDescending(a => a.Data)
            .Take(20)
            .ToListAsync(ct);

        var documentos = await _context.Documentos
            .Where(d => d.ProcessoId == dto.ProcessoId)
            .Select(d => new { d.Nome, d.Tipo })
            .ToListAsync(ct);

        var tarefas = await _context.Tarefas
            .Where(t => t.ProcessoId == dto.ProcessoId && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada)
            .Select(t => new { t.Titulo, t.Status, t.Prazo, t.Prioridade })
            .ToListAsync(ct);

        var parteNomes = processo.Partes?.Select(p =>
            $"{p.TipoParte}: {p.Contato?.Nome ?? "—"}") ?? [];

        var sb = new StringBuilder();
        sb.AppendLine($"Número CNJ: {processo.NumeroCNJ}");
        sb.AppendLine($"Tribunal: {processo.Tribunal ?? "—"}");
        sb.AppendLine($"Vara: {processo.Vara ?? "—"}");
        sb.AppendLine($"Comarca: {processo.Comarca ?? "—"}");
        sb.AppendLine($"Área do Direito: {processo.AreaDireito}");
        sb.AppendLine($"Fase: {processo.Fase}");
        sb.AppendLine($"Status: {processo.Status}");
        if (processo.TipoAcao != null) sb.AppendLine($"Tipo de Ação: {processo.TipoAcao}");
        if (processo.ValorCausa.HasValue) sb.AppendLine($"Valor da Causa: R$ {processo.ValorCausa:N2}");
        if (processo.Classe != null) sb.AppendLine($"Classe: {processo.Classe}");
        if (processo.Assuntos != null) sb.AppendLine($"Assuntos: {processo.Assuntos}");
        if (processo.Observacoes != null) sb.AppendLine($"Observações: {processo.Observacoes}");

        sb.AppendLine();
        sb.AppendLine("=== PARTES ===");
        var partesList = parteNomes.ToList();
        sb.AppendLine(partesList.Count > 0 ? string.Join("\n", partesList) : "Nenhuma parte cadastrada.");

        sb.AppendLine();
        sb.AppendLine("=== ANDAMENTOS (mais recentes) ===");
        if (andamentos.Count > 0)
        {
            foreach (var a in andamentos)
                sb.AppendLine($"[{a.Data:dd/MM/yyyy}] {a.Tipo}: {a.Descricao}");
        }
        else
        {
            sb.AppendLine("Nenhum andamento registrado.");
        }

        sb.AppendLine();
        sb.AppendLine("=== DOCUMENTOS ===");
        if (documentos.Count > 0)
            foreach (var d in documentos)
                sb.AppendLine($"- {d.Nome} ({d.Tipo})");
        else
            sb.AppendLine("Nenhum documento anexado.");

        sb.AppendLine();
        sb.AppendLine("=== TAREFAS EM ABERTO ===");
        if (tarefas.Count > 0)
        {
            foreach (var t in tarefas)
            {
                var prazo = t.Prazo.HasValue ? $", prazo: {t.Prazo:dd/MM/yyyy}" : "";
                sb.AppendLine($"- [{t.Prioridade}] {t.Titulo} ({t.Status}{prazo})");
            }
        }
        else
        {
            sb.AppendLine("Nenhuma tarefa em aberto.");
        }

        var conteudo = await _iaService.GerarResumoProcessoAsync(sb.ToString(), ct);

        await _creditoService.ConsumirCreditoAsync(TipoCreditoAI.TraducaoAndamento, 1, ct);

        var resumo = new ResumoProcesso
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcessoId = dto.ProcessoId,
            GeradoPorId = usuarioId,
            Conteudo = conteudo,
            GeradoEm = DateTime.UtcNow
        };

        _context.ResumosProcesso.Add(resumo);
        await _context.SaveChangesAsync(ct);

        var usuario = await _context.Users.FindAsync([usuarioId], ct);

        return new ResumoProcessoResponseDto(
            resumo.Id,
            resumo.ProcessoId,
            resumo.Conteudo,
            usuario?.UserName ?? "Usuário",
            resumo.GeradoEm
        );
    }

    public async Task<IEnumerable<ResumoProcessoResponseDto>> ListarAsync(Guid processoId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        var resumos = await _context.ResumosProcesso
            .Where(r => r.ProcessoId == processoId && r.TenantId == tenantId)
            .OrderByDescending(r => r.GeradoEm)
            .Include(r => r.GeradoPor)
            .ToListAsync(ct);

        return resumos.Select(r => new ResumoProcessoResponseDto(
            r.Id,
            r.ProcessoId,
            r.Conteudo,
            r.GeradoPor?.UserName ?? "Usuário",
            r.GeradoEm
        ));
    }
}
