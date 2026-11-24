using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using System.Text.Json;
using System.Text.Json.Serialization;
using LegalManager.Domain;
using LegalManager.Infrastructure.Jurisprudencia;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class PecaJuridicaService : IPecaJuridicaService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IIAService _iaService;
    private readonly ICreditoService _creditoService;
    private readonly IJurisprudenciaService? _jurisprudencia;

    public PecaJuridicaService(
        AppDbContext context,
        ITenantContext tenantContext,
        IIAService iaService,
        ICreditoService creditoService,
        IJurisprudenciaService? jurisprudencia = null)
    {
        _context = context;
        _tenantContext = tenantContext;
        _iaService = iaService;
        _creditoService = creditoService;
        _jurisprudencia = jurisprudencia;
    }

    public async Task<PecaGeradaResponseDto> GerarPecaAsync(GerarPecaDto dto, Guid usuarioId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        if (!await _creditoService.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca, 1, ct))
        {
            throw new InvalidOperationException("Créditos de geração de peças esgotados.");
        }

        var tipoPecaStr = dto.Tipo.ToString();

        string contextoCompleto = dto.DescricaoSolicitacao;
        Processo? processo = null;

        if (dto.ProcessoId.HasValue)
        {
            processo = await _context.Processos
                .Include(p => p.Partes)
                .FirstOrDefaultAsync(p => p.Id == dto.ProcessoId.Value && p.TenantId == tenantId, ct);

            if (processo != null)
            {
                contextoCompleto = $"""
                    Processo: {processo.NumeroCNJ}
                    Tribunal: {processo.Tribunal}
                    Vara: {processo.Vara}
                    Comarca: {processo.Comarca}
                    Área: {processo.AreaDireito}
                    Tipo de Ação: {processo.TipoAcao}
                    ---
                    {dto.DescricaoSolicitacao}
                    """;
            }
        }

        // Jurisprudência verificada (recurso em teste): a IA só cita precedentes achados na base oficial.
        var liberado = _jurisprudencia is not null && await _context.Tenants.AsNoTracking()
            .AnyAsync(t => t.Id == tenantId && t.RecursosLiberados.Contains(RecursosEmTeste.JurisprudenciaVerificada), ct);

        IReadOnlyList<PrecedenteDto> precedentes = [];
        if (liberado)
        {
            var consulta = string.Join(' ', new[] { dto.DescricaoSolicitacao, processo?.TipoAcao, processo?.Classe, processo?.Assuntos }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            precedentes = await _jurisprudencia!.BuscarPrecedentesAsync(consulta, PrecedentesPorPeca, ct);
        }

        string conteudoGerado;
        string? jurisprudencia = null;
        if (precedentes.Count > 0)
        {
            conteudoGerado = await _iaService.GerarPecaComPrecedentesAsync(contextoCompleto, tipoPecaStr, ListaParaIA(precedentes), ct);
        }
        else
        {
            // Em paralelo: as duas chamadas juntas precisam caber no limite de tempo da requisição.
            var gerarPeca = _iaService.GerarPecaJuridicaAsync(contextoCompleto, tipoPecaStr, ct);
            var buscarEntendimentos = _iaService.BuscarJurisprudenciaAsync(
                dto.DescricaoSolicitacao.Split('\n').FirstOrDefault() ?? dto.DescricaoSolicitacao, ct);
            await Task.WhenAll(gerarPeca, buscarEntendimentos);
            conteudoGerado = await gerarPeca;
            jurisprudencia = await buscarEntendimentos;
        }

        // Toda citação do texto vai marcada; sem o recurso, como "não verificada".
        var verificacao = _jurisprudencia is not null
            ? await _jurisprudencia.VerificarTextoAsync(conteudoGerado, liberado, ct)
            : SoMarcar(conteudoGerado);

        var peca = new PecaGerada
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcessoId = dto.ProcessoId,
            GeradoPorId = usuarioId,
            Tipo = dto.Tipo,
            DescricaoSolicitacao = dto.DescricaoSolicitacao,
            ConteudoGerado = conteudoGerado,
            JurisprudenciaCitada = jurisprudencia,
            TesesSugeridas = null,
            CitacoesJson = JsonSerializer.Serialize(verificacao, Json),
            PrecedentesUsadosJson = precedentes.Count > 0 ? JsonSerializer.Serialize(precedentes, Json) : null,
            CriadoEm = DateTime.UtcNow
        };

        await _context.PecasGeradas.AddAsync(peca, ct);
        await _creditoService.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, ct);
        await _context.SaveChangesAsync(ct);

        return MapToDto(peca);
    }

    /// <summary>Quantos precedentes da base vão para o prompt da peça.</summary>
    public const int PrecedentesPorPeca = 8;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Lista numerada para o prompt: identificação, órgão, relator, data e trecho da ementa.</summary>
    public static string ListaParaIA(IReadOnlyList<PrecedenteDto> precedentes) =>
        string.Join("\n\n", precedentes.Select((p, i) =>
        {
            var dados = string.Join(", ", new[]
            {
                p.OrgaoJulgador,
                p.Relator is null ? null : $"Rel. Min. {p.Relator}",
                p.DataJulgamento is { } d ? $"julgado em {d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)}" : null,
            }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var texto = p.Texto.Length > 1200 ? p.Texto[..1200] + "…" : p.Texto;
            return $"[{i + 1}] {p.Identificacao}{(dados.Length > 0 ? $" — {dados}" : "")}\n{texto}";
        }));

    private static VerificacaoCitacoesDto SoMarcar(string texto)
    {
        var citacoes = ExtratorCitacoes.Extrair(texto)
            .Select(c => new CitacaoVerificadaDto(c.Tipo, c.Tribunal, c.Classe, c.Numero, c.Trecho, c.Inicio, c.Fim,
                StatusCitacao.NaoVerificada, "Não verificada — confira na fonte antes de usar."))
            .ToList();
        return new VerificacaoCitacoesDto(citacoes, 0, citacoes.Count, 0, "Conferência na base oficial não habilitada para o escritório.");
    }

    private static T? LerJson<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; }
    }

    public async Task<PecaGeradaResponseDto?> ObterPecaAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var peca = await _context.PecasGeradas
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);

        return peca == null ? null : MapToDto(peca);
    }

    public async Task<IEnumerable<PecaGeradaResponseDto>> ListarAsync(ListPecasGeradasDto filtro, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _context.PecasGeradas.Where(p => p.TenantId == tenantId);

        if (filtro.ProcessoId.HasValue)
            query = query.Where(p => p.ProcessoId == filtro.ProcessoId.Value);

        if (filtro.Tipo.HasValue)
            query = query.Where(p => p.Tipo == filtro.Tipo.Value);

        var pecas = await query
            .OrderByDescending(p => p.CriadoEm)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .ToListAsync(ct);

        return pecas.Select(MapToDto);
    }

    private static PecaGeradaResponseDto MapToDto(PecaGerada p) => new(
        p.Id,
        p.ProcessoId,
        p.GeradoPorId,
        p.Tipo,
        p.DescricaoSolicitacao,
        p.ConteudoGerado,
        p.JurisprudenciaCitada,
        p.TesesSugeridas,
        p.CriadoEm,
        LerJson<VerificacaoCitacoesDto>(p.CitacoesJson),
        LerJson<List<PrecedenteDto>>(p.PrecedentesUsadosJson)
    );
}