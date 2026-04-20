using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class ContatoService : IContatoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public ContatoService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<ContatoResponseDto> CreateAsync(CreateContatoDto dto, CancellationToken ct = default)
    {
        var contato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Tipo = dto.Tipo,
            TipoContato = dto.TipoContato,
            Nome = dto.Nome,
            CpfCnpj = dto.CpfCnpj,
            Oab = dto.Oab,
            Email = dto.Email,
            Telefone = dto.Telefone,
            Endereco = dto.Endereco,
            Cidade = dto.Cidade,
            Estado = dto.Estado,
            Cep = dto.Cep,
            DataNascimento = dto.DataNascimento,
            Observacoes = dto.Observacoes,
            NotificacaoHabilitada = dto.NotificacaoHabilitada,
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            Tags = dto.Tags?.Select(t => new ContatoTag { Id = Guid.NewGuid(), Tag = t }).ToList() ?? []
        };

        _context.Contatos.Add(contato);
        await _context.SaveChangesAsync(ct);

        return MapToResponse(contato);
    }

    public async Task<ContatoResponseDto> UpdateAsync(Guid id, UpdateContatoDto dto, CancellationToken ct = default)
    {
        var contato = await _context.Contatos
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == _tenantContext.TenantId, ct)
            ?? throw new KeyNotFoundException("Contato não encontrado.");

        contato.Tipo = dto.Tipo;
        contato.TipoContato = dto.TipoContato;
        contato.Nome = dto.Nome;
        contato.CpfCnpj = dto.CpfCnpj;
        contato.Oab = dto.Oab;
        contato.Email = dto.Email;
        contato.Telefone = dto.Telefone;
        contato.Endereco = dto.Endereco;
        contato.Cidade = dto.Cidade;
        contato.Estado = dto.Estado;
        contato.Cep = dto.Cep;
        contato.DataNascimento = dto.DataNascimento;
        contato.Observacoes = dto.Observacoes;
        contato.NotificacaoHabilitada = dto.NotificacaoHabilitada;
        contato.AtualizadoEm = DateTime.UtcNow;

        _context.ContatoTags.RemoveRange(contato.Tags);
        contato.Tags = dto.Tags?.Select(t => new ContatoTag { Id = Guid.NewGuid(), ContatoId = id, Tag = t }).ToList() ?? [];

        await _context.SaveChangesAsync(ct);
        return MapToResponse(contato);
    }

    public async Task<ContatoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var contato = await _context.Contatos
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == _tenantContext.TenantId, ct);

        return contato == null ? null : MapToResponse(contato);
    }

    public async Task<PagedResultDto<ContatoListItemDto>> GetAllAsync(ContatoFiltroDto filtro, CancellationToken ct = default)
    {
        var query = _context.Contatos
            .Include(c => c.Tags)
            .Where(c => c.TenantId == _tenantContext.TenantId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.ToLower();
            query = query.Where(c =>
                c.Nome.ToLower().Contains(busca) ||
                (c.CpfCnpj != null && c.CpfCnpj.Contains(busca)) ||
                (c.Email != null && c.Email.ToLower().Contains(busca)));
        }

        if (filtro.TipoContato.HasValue)
            query = query.Where(c => c.TipoContato == filtro.TipoContato.Value);

        if (filtro.Tipo.HasValue)
            query = query.Where(c => c.Tipo == filtro.Tipo.Value);

        if (filtro.Ativo.HasValue)
            query = query.Where(c => c.Ativo == filtro.Ativo.Value);

        if (!string.IsNullOrWhiteSpace(filtro.Tag))
            query = query.Where(c => c.Tags.Any(t => t.Tag == filtro.Tag));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.Nome)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .ToListAsync(ct);

        return new PagedResultDto<ContatoListItemDto>(
            items.Select(MapToListItem),
            total,
            filtro.Page,
            filtro.PageSize,
            (int)Math.Ceiling((double)total / filtro.PageSize)
        );
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var contato = await _context.Contatos
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == _tenantContext.TenantId, ct)
            ?? throw new KeyNotFoundException("Contato não encontrado.");

        contato.Ativo = false;
        contato.AtualizadoEm = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<AtendimentoResponseDto> AddAtendimentoAsync(Guid contatoId, CreateAtendimentoDto dto, CancellationToken ct = default)
    {
        var exists = await _context.Contatos
            .AnyAsync(c => c.Id == contatoId && c.TenantId == _tenantContext.TenantId, ct);

        if (!exists) throw new KeyNotFoundException("Contato não encontrado.");

        var atendimento = new Atendimento
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            ContatoId = contatoId,
            UsuarioId = _tenantContext.UserId,
            Descricao = dto.Descricao,
            Data = dto.Data,
            CriadoEm = DateTime.UtcNow
        };

        _context.Atendimentos.Add(atendimento);
        await _context.SaveChangesAsync(ct);

        var nomeUsuario = await _context.Users
            .Where(u => u.Id == _tenantContext.UserId)
            .Select(u => u.Nome)
            .FirstOrDefaultAsync(ct) ?? "";

        return new AtendimentoResponseDto(atendimento.Id, atendimento.Descricao, atendimento.Data,
            atendimento.UsuarioId, nomeUsuario, atendimento.CriadoEm);
    }

    public async Task<IEnumerable<AtendimentoResponseDto>> GetAtendimentosAsync(Guid contatoId, CancellationToken ct = default)
    {
        var atendimentos = await _context.Atendimentos
            .Include(a => a.Usuario)
            .Where(a => a.ContatoId == contatoId && a.TenantId == _tenantContext.TenantId)
            .OrderByDescending(a => a.Data)
            .ToListAsync(ct);

        return atendimentos.Select(a => new AtendimentoResponseDto(
            a.Id, a.Descricao, a.Data, a.UsuarioId, a.Usuario.Nome, a.CriadoEm));
    }

    private static ContatoResponseDto MapToResponse(Contato c) => new(
        c.Id, c.Tipo, c.TipoContato, c.Nome, c.CpfCnpj, c.Oab, c.Email,
        c.Telefone, c.Endereco, c.Cidade, c.Estado, c.Cep, c.DataNascimento,
        c.Observacoes, c.NotificacaoHabilitada, c.Ativo,
        c.Tags.Select(t => t.Tag).ToList(), c.CriadoEm);

    private static ContatoListItemDto MapToListItem(Contato c) => new(
        c.Id, c.Tipo, c.TipoContato, c.Nome, c.CpfCnpj, c.Email, c.Telefone,
        c.Ativo, c.Tags.Select(t => t.Tag).ToList());
}
