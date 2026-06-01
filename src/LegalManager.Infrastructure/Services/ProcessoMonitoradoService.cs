using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class ProcessoMonitoradoService : IProcessoMonitoradoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;
    private readonly DataJudAdapter _dataJud;

    public ProcessoMonitoradoService(AppDbContext context, ITenantContext tenant, DataJudAdapter dataJud)
    {
        _context = context;
        _tenant = tenant;
        _dataJud = dataJud;
    }

    public async Task<IEnumerable<ProcessoMonitoradoResponseDto>> GetAllAsync(CancellationToken ct = default)
        => await _context.ProcessosMonitorados
            .Where(p => p.TenantId == _tenant.TenantId)
            .OrderBy(p => p.CriadoEm)
            .Select(p => new ProcessoMonitoradoResponseDto(p.Id, p.NumeroCNJ, p.NomeExibicao, p.Ativo, p.CriadoEm))
            .ToListAsync(ct);

    public async Task<ProcessoMonitoradoCreateResultDto> CreateAsync(CreateProcessoMonitoradoDto dto, CancellationToken ct = default)
    {
        var numeroCNJ = FormatNumeroCNJ(dto.NumeroCNJ);

        var count = await _context.ProcessosMonitorados.CountAsync(p => p.TenantId == _tenant.TenantId, ct);
        var limite = Domain.PlanoRestricoes.MaxProcessosMonitorados(_tenant.Plano);
        if (count >= limite)
            throw new InvalidOperationException($"Limite de {limite} processos monitorados atingido no plano atual.");

        if (await _context.ProcessosMonitorados.AnyAsync(
                p => p.TenantId == _tenant.TenantId && p.NumeroCNJ == numeroCNJ, ct))
            throw new InvalidOperationException($"Processo '{numeroCNJ}' já está sendo monitorado.");

        var entity = new ProcessoMonitorado
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            NumeroCNJ = numeroCNJ,
            NomeExibicao = dto.NomeExibicao?.Trim(),
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        _context.ProcessosMonitorados.Add(entity);

        string? tribunal = null;
        string? vara = null;
        string? mensagem = null;
        bool processoEncontrado = false;

        var consulta = await _dataJud.ConsultarAsync(numeroCNJ, ct);
        if (consulta.Encontrado)
        {
            tribunal = consulta.NomeTribunal;
            vara = consulta.Vara;
            processoEncontrado = true;
            mensagem = "Processo encontrado e criado automaticamente.";

            foreach (var m in consulta.Movimentos)
            {
                entity.Andamentos.Add(new ProcessoMonitoradoAndamento
                {
                    Id = Guid.NewGuid(),
                    ProcessoMonitoradoId = entity.Id,
                    TenantId = _tenant.TenantId,
                    Data = m.Data,
                    Descricao = m.Descricao,
                    CodigoCNJ = m.CodigoCNJ,
                    OrgaoJulgador = m.OrgaoJulgador,
                    Fonte = FonteAndamento.DataJud,
                    CriadoEm = DateTime.UtcNow
                });
            }

            var processoExistente = await _context.Processos
                .AnyAsync(p => p.TenantId == _tenant.TenantId && p.NumeroCNJ == numeroCNJ, ct);

            if (!processoExistente)
            {
                var novoProcesso = new Processo
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenant.TenantId,
                    NumeroCNJ = numeroCNJ,
                    Tribunal = tribunal,
                    Vara = vara,
                    AreaDireito = AreaDireito.Outro,
                    Fase = FaseProcessual.Conhecimento,
                    Status = StatusProcesso.Ativo,
                    Monitorado = false,
                    CriadoEm = DateTime.UtcNow,
                    UltimoMonitoramento = DateTime.UtcNow
                };
                _context.Processos.Add(novoProcesso);
            }
        }
        else
        {
            mensagem = "Processo monitorado. Dados serão preenchidos quando houver movimentação.";
        }

        await _context.SaveChangesAsync(ct);

        return new ProcessoMonitoradoCreateResultDto(
            entity.Id,
            entity.NumeroCNJ,
            entity.NomeExibicao,
            entity.Ativo,
            entity.CriadoEm,
            processoEncontrado,
            tribunal,
            vara,
            mensagem);
    }

    public async Task ToggleAtivoAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.ProcessosMonitorados
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenant.TenantId, ct)
            ?? throw new KeyNotFoundException("Processo monitorado não encontrado.");

        entity.Ativo = !entity.Ativo;
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.ProcessosMonitorados
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenant.TenantId, ct)
            ?? throw new KeyNotFoundException("Processo monitorado não encontrado.");

        _context.ProcessosMonitorados.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }

    private static string FormatNumeroCNJ(string numero)
    {
        var digits = new string(numero.Where(char.IsDigit).ToArray());
        if (digits.Length < 7) return numero;
        return $"{digits[..7]}-{digits[7..9]}.{digits[9..13]}.{digits[13]}.{digits[14..16]}.{digits[16..20]}";
    }
}