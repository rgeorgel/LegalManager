using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Honorarios;
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
    private readonly IHonorarioService _honorarioService;

    public ContatoService(AppDbContext context, ITenantContext tenantContext, IHonorarioService honorarioService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _honorarioService = honorarioService;
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

    public async Task<ContatoResponseDto?> GetByNomeAsync(string nome, CancellationToken ct = default)
    {
        var contato = await _context.Contatos
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(c =>
                c.TenantId == _tenantContext.TenantId &&
                c.Nome.ToLower() == nome.ToLower(),
                ct);

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
            .ApplySort(filtro.SortBy, filtro.SortDir)
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

    public async Task<ContatoPerfilDto> GetPerfilAsync(Guid contatoId, CancellationToken ct = default)
    {
        var contatoExists = await _context.Contatos
            .AnyAsync(c => c.Id == contatoId && c.TenantId == _tenantContext.TenantId, ct);
        if (!contatoExists) throw new KeyNotFoundException("Contato não encontrado.");

        var tenantId = _tenantContext.TenantId;

        var processosAtivos = await _context.ProcessoPartes
            .Where(pp => pp.ContatoId == contatoId && pp.Processo.TenantId == tenantId && pp.Processo.Status == Domain.Enums.StatusProcesso.Ativo)
            .Select(pp => pp.ProcessoId)
            .Distinct()
            .CountAsync(ct);

        // Lançamentos financeiros "avulsos" (módulo Financeiro), lançados diretamente contra o contato.
        var lancamentosPendentes = await _context.LancamentosFinanceiros
            .Where(l => l.ContatoId == contatoId && l.TenantId == tenantId && l.Status == Domain.Enums.StatusLancamento.Pendente)
            .ToListAsync(ct);
        var saldoFinanceiro = lancamentosPendentes.Sum(l => l.Tipo == Domain.Enums.TipoLancamento.Receita ? l.Valor : -l.Valor);

        // Parcelas de contratos de honorários ainda não pagas (pendentes + em atraso). Esses valores
        // nunca viram LancamentoFinanceiro enquanto não são pagos — só ao registrar o pagamento é que
        // um lançamento (Status=Pago) é criado — por isso precisam ser somados à parte aqui.
        // Contratos encerrados ficam de fora: uma vez encerrado, o valor que ainda constava como
        // pendente deixa de compor o saldo em aberto do contato.
        var contratosHonorarios = await _honorarioService.ListarAsync(
            tenantId, new FiltroContratoHonorario(null, contatoId, null, null, 1, 500), ct);
        saldoFinanceiro += contratosHonorarios.Items
            .Where(c => c.Status != Domain.Enums.StatusContratoHonorario.Encerrado)
            .Sum(c => c.ValorPendente + c.ValorEmAtraso);

        var proximaTarefa = await _context.Tarefas
            .Where(t => t.ContatoId == contatoId && t.TenantId == tenantId
                && t.Prazo != null && t.Prazo >= DateTime.UtcNow
                && t.Status != Domain.Enums.StatusTarefa.Concluida && t.Status != Domain.Enums.StatusTarefa.Cancelada)
            .OrderBy(t => t.Prazo)
            .FirstOrDefaultAsync(ct);

        var ultimoAtendimentoEm = await _context.Atendimentos
            .Where(a => a.ContatoId == contatoId && a.TenantId == tenantId)
            .OrderByDescending(a => a.Data)
            .Select(a => (DateTime?)a.Data)
            .FirstOrDefaultAsync(ct);

        var resumo = new ContatoResumoDto(
            processosAtivos,
            saldoFinanceiro,
            proximaTarefa == null ? null : new TimelineItemDto("Tarefa", proximaTarefa.Prazo!.Value, proximaTarefa.Titulo, proximaTarefa.Descricao, $"/pages/tarefas.html?abrirId={proximaTarefa.Id}"),
            ultimoAtendimentoEm
        );

        var timeline = new List<TimelineItemDto>();

        var atendimentos = await _context.Atendimentos
            .Where(a => a.ContatoId == contatoId && a.TenantId == tenantId)
            .ToListAsync(ct);
        timeline.AddRange(atendimentos.Select(a => new TimelineItemDto("Atendimento", a.Data, "Atendimento registrado", a.Descricao)));

        var tarefas = await _context.Tarefas
            .Where(t => t.ContatoId == contatoId && t.TenantId == tenantId)
            .ToListAsync(ct);
        timeline.AddRange(tarefas.Select(t => t.Status == Domain.Enums.StatusTarefa.Concluida && t.ConcluidaEm != null
            ? new TimelineItemDto("Tarefa", t.ConcluidaEm.Value, $"Tarefa concluída: {t.Titulo}", t.Descricao, $"/pages/tarefas.html?abrirId={t.Id}")
            : new TimelineItemDto("Tarefa", t.CriadoEm, $"Tarefa criada: {t.Titulo}", t.Descricao, $"/pages/tarefas.html?abrirId={t.Id}")));

        // Honorários e Financeiro são recursos pagos (indisponíveis no plano Free) — só linkamos
        // pra lá se o tenant atual realmente tem acesso, senão o clique cai numa tela vazia/402.
        var podeVerHonorarios = Domain.PlanoRestricoes.PermiteHonorariosContratos(_tenantContext.Plano);
        var podeVerFinanceiro = Domain.PlanoRestricoes.PermiteFinanceiro(_tenantContext.Plano);

        var lancamentos = await _context.LancamentosFinanceiros
            .Where(l => l.ContatoId == contatoId && l.TenantId == tenantId)
            .ToListAsync(ct);
        timeline.AddRange(lancamentos.Select(l => new TimelineItemDto(
            "Financeiro",
            l.DataPagamento ?? l.DataVencimento,
            $"{(l.Tipo == Domain.Enums.TipoLancamento.Receita ? "Receita" : "Despesa")} — {l.Categoria}",
            $"R$ {l.Valor:N2} ({l.Status})",
            l.ContratoHonorarioId != null ? (podeVerHonorarios ? $"/pages/honorarios-contrato-detalhe.html?id={l.ContratoHonorarioId}" : null)
                : l.ProcessoId != null ? $"/pages/processo-detalhe.html?id={l.ProcessoId}"
                : (podeVerFinanceiro ? "/pages/financeiro.html" : null))));

        var processosVinculados = await _context.ProcessoPartes
            .Include(pp => pp.Processo)
            .Where(pp => pp.ContatoId == contatoId && pp.Processo.TenantId == tenantId)
            .ToListAsync(ct);
        timeline.AddRange(processosVinculados.Select(pp => new TimelineItemDto(
            "Processo",
            pp.Processo.CriadoEm,
            $"Vinculado ao processo {pp.Processo.NumeroCNJ}",
            pp.TipoParte.ToString(),
            $"/pages/processo-detalhe.html?id={pp.ProcessoId}")));

        var timelineOrdenada = timeline.OrderByDescending(t => t.Data).Take(100).ToList();

        return new ContatoPerfilDto(resumo, timelineOrdenada);
    }

    public async Task<ContatoVinculoResponseDto> AddVinculoAsync(Guid contatoId, CreateContatoVinculoDto dto, CancellationToken ct = default)
    {
        if (contatoId == dto.ContatoRelacionadoId)
            throw new InvalidOperationException("Um contato não pode ser vinculado a si mesmo.");

        var tenantId = _tenantContext.TenantId;

        var contato = await _context.Contatos.FirstOrDefaultAsync(c => c.Id == contatoId && c.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contato não encontrado.");
        var relacionado = await _context.Contatos.FirstOrDefaultAsync(c => c.Id == dto.ContatoRelacionadoId && c.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contato relacionado não encontrado.");

        var vinculo = new ContatoVinculo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContatoId = contatoId,
            ContatoRelacionadoId = dto.ContatoRelacionadoId,
            Tipo = dto.Tipo,
            Observacao = dto.Observacao,
            CriadoEm = DateTime.UtcNow
        };

        _context.ContatoVinculos.Add(vinculo);
        await _context.SaveChangesAsync(ct);

        return new ContatoVinculoResponseDto(vinculo.Id, contato.Id, contato.Nome, relacionado.Id, relacionado.Nome, vinculo.Tipo, vinculo.Observacao, vinculo.CriadoEm);
    }

    public async Task<IEnumerable<ContatoVinculoResponseDto>> GetVinculosAsync(Guid contatoId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        var contato = await _context.Contatos.FirstOrDefaultAsync(c => c.Id == contatoId && c.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contato não encontrado.");

        var vinculos = await _context.ContatoVinculos
            .Include(v => v.Contato)
            .Include(v => v.ContatoRelacionado)
            .Where(v => v.TenantId == tenantId && (v.ContatoId == contatoId || v.ContatoRelacionadoId == contatoId))
            .OrderByDescending(v => v.CriadoEm)
            .ToListAsync(ct);

        return vinculos.Select(v =>
        {
            var outro = v.ContatoId == contatoId ? v.ContatoRelacionado : v.Contato;
            return new ContatoVinculoResponseDto(v.Id, contato.Id, contato.Nome, outro.Id, outro.Nome, v.Tipo, v.Observacao, v.CriadoEm);
        });
    }

    public async Task RemoveVinculoAsync(Guid contatoId, Guid vinculoId, CancellationToken ct = default)
    {
        var vinculo = await _context.ContatoVinculos
            .FirstOrDefaultAsync(v => v.Id == vinculoId && v.TenantId == _tenantContext.TenantId
                && (v.ContatoId == contatoId || v.ContatoRelacionadoId == contatoId), ct)
            ?? throw new KeyNotFoundException("Vínculo não encontrado.");

        _context.ContatoVinculos.Remove(vinculo);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<ContatoDuplicadoGrupoDto>> GetDuplicadosAsync(CancellationToken ct = default)
    {
        var contatos = await _context.Contatos
            .Where(c => c.TenantId == _tenantContext.TenantId && c.Ativo)
            .Select(c => new ContatoDuplicadoItemDto(c.Id, c.Nome, c.TipoContato, c.Email, c.Telefone, c.CpfCnpj))
            .ToListAsync(ct);

        var grupos = new List<ContatoDuplicadoGrupoDto>();

        static string SoDigitos(string s) => new(s.Where(char.IsDigit).ToArray());

        void AdicionarGrupos(string criterio, Func<ContatoDuplicadoItemDto, string?> chaveFn)
        {
            var agrupado = contatos
                .Select(c => new { Item = c, Chave = chaveFn(c) })
                .Where(x => !string.IsNullOrWhiteSpace(x.Chave))
                .GroupBy(x => x.Chave!)
                .Where(g => g.Select(x => x.Item.Id).Distinct().Count() > 1);

            foreach (var g in agrupado)
                grupos.Add(new ContatoDuplicadoGrupoDto(criterio, g.Key, g.Select(x => x.Item).ToList()));
        }

        AdicionarGrupos("CpfCnpj", c => string.IsNullOrWhiteSpace(c.CpfCnpj) ? null : SoDigitos(c.CpfCnpj));
        AdicionarGrupos("Email", c => string.IsNullOrWhiteSpace(c.Email) ? null : c.Email.Trim().ToLowerInvariant());
        AdicionarGrupos("Telefone", c => string.IsNullOrWhiteSpace(c.Telefone) ? null : SoDigitos(c.Telefone));
        AdicionarGrupos("Nome", c => string.IsNullOrWhiteSpace(c.Nome) ? null : c.Nome.Trim().ToLowerInvariant());

        return grupos;
    }

    public async Task<IEnumerable<ContatoAniversarianteDto>> GetAniversariantesAsync(int? mes, CancellationToken ct = default)
    {
        var mesAlvo = mes is >= 1 and <= 12 ? mes.Value : DateTime.UtcNow.Month;

        var aniversariantes = await _context.Contatos
            .Where(c => c.TenantId == _tenantContext.TenantId && c.Ativo
                && c.DataNascimento != null && c.DataNascimento.Value.Month == mesAlvo)
            .ToListAsync(ct);

        return aniversariantes
            .OrderBy(c => c.DataNascimento!.Value.Day)
            .Select(c => new ContatoAniversarianteDto(c.Id, c.Nome, c.DataNascimento!.Value, c.Email, c.Telefone));
    }

    public async Task<ContatoFiltroSalvoResponseDto> AddFiltroSalvoAsync(CreateContatoFiltroSalvoDto dto, CancellationToken ct = default)
    {
        var filtro = new ContatoFiltroSalvo
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            UsuarioId = _tenantContext.UserId,
            Nome = dto.Nome,
            Busca = dto.Busca,
            TipoContato = dto.TipoContato,
            Tipo = dto.Tipo,
            Tag = dto.Tag,
            CriadoEm = DateTime.UtcNow
        };

        _context.ContatoFiltrosSalvos.Add(filtro);
        await _context.SaveChangesAsync(ct);

        return MapToFiltroSalvoResponse(filtro);
    }

    public async Task<IEnumerable<ContatoFiltroSalvoResponseDto>> GetFiltrosSalvosAsync(CancellationToken ct = default)
    {
        var filtros = await _context.ContatoFiltrosSalvos
            .Where(f => f.TenantId == _tenantContext.TenantId && f.UsuarioId == _tenantContext.UserId)
            .OrderBy(f => f.Nome)
            .ToListAsync(ct);

        return filtros.Select(MapToFiltroSalvoResponse);
    }

    public async Task RemoveFiltroSalvoAsync(Guid filtroId, CancellationToken ct = default)
    {
        var filtro = await _context.ContatoFiltrosSalvos
            .FirstOrDefaultAsync(f => f.Id == filtroId && f.TenantId == _tenantContext.TenantId && f.UsuarioId == _tenantContext.UserId, ct)
            ?? throw new KeyNotFoundException("Filtro salvo não encontrado.");

        _context.ContatoFiltrosSalvos.Remove(filtro);
        await _context.SaveChangesAsync(ct);
    }

    private static ContatoFiltroSalvoResponseDto MapToFiltroSalvoResponse(ContatoFiltroSalvo f) => new(
        f.Id, f.Nome, f.Busca, f.TipoContato, f.Tipo, f.Tag, f.CriadoEm);

    private static ContatoResponseDto MapToResponse(Contato c) => new(
        c.Id, c.Tipo, c.TipoContato, c.Nome, c.CpfCnpj, c.Oab, c.Email,
        c.Telefone, c.Endereco, c.Cidade, c.Estado, c.Cep, c.DataNascimento,
        c.Observacoes, c.NotificacaoHabilitada, c.Ativo,
        c.Tags.Select(t => t.Tag).ToList(), c.CriadoEm);

    private static ContatoListItemDto MapToListItem(Contato c) => new(
        c.Id, c.Tipo, c.TipoContato, c.Nome, c.CpfCnpj, c.Email, c.Telefone,
        c.Ativo, c.Tags.Select(t => t.Tag).ToList());
}

internal static class ContatoServiceSortExtensions
{
    private static readonly HashSet<string> AllowedSortBy = new(StringComparer.OrdinalIgnoreCase)
    {
        "nome", "tipo", "tipoContato", "cpfCnpj", "email", "telefone"
    };

    public static IQueryable<Contato> ApplySort(this IQueryable<Contato> query, string? sortBy, string? sortDir)
    {
        var key = (sortBy ?? string.Empty).Trim();
        var ascending = !string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(key) || !AllowedSortBy.Contains(key))
            return ascending
                ? query.OrderBy(c => c.Nome == null).ThenBy(c => c.Nome)
                : query.OrderByDescending(c => c.Nome);

        return key.ToLowerInvariant() switch
        {
            "nome" => ascending
                ? query.OrderBy(c => c.Nome == null).ThenBy(c => c.Nome)
                : query.OrderByDescending(c => c.Nome),
            "tipo" => ascending
                ? query.OrderBy(c => c.Tipo)
                : query.OrderByDescending(c => c.Tipo),
            "tipocontato" => ascending
                ? query.OrderBy(c => c.TipoContato)
                : query.OrderByDescending(c => c.TipoContato),
            "cpfcnpj" => ascending
                ? query.OrderBy(c => c.CpfCnpj == null).ThenBy(c => c.CpfCnpj)
                : query.OrderByDescending(c => c.CpfCnpj),
            "email" => ascending
                ? query.OrderBy(c => c.Email == null).ThenBy(c => c.Email)
                : query.OrderByDescending(c => c.Email),
            "telefone" => ascending
                ? query.OrderBy(c => c.Telefone == null).ThenBy(c => c.Telefone)
                : query.OrderByDescending(c => c.Telefone),
            _ => ascending
                ? query.OrderBy(c => c.Nome == null).ThenBy(c => c.Nome)
                : query.OrderByDescending(c => c.Nome)
        };
    }
}
