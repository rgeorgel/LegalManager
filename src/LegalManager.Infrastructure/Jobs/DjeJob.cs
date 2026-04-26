using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

public class DjeJob
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly AppDbContext? _context;
    private readonly IEnumerable<IDjeAdapter>? _adapters;
    private readonly ILogger<DjeJob> _logger;

    public DjeJob(IServiceProvider serviceProvider, ILogger<DjeJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public DjeJob(AppDbContext context, IEnumerable<IDjeAdapter> adapters, ILogger<DjeJob> logger)
    {
        _context = context;
        _adapters = adapters;
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken ct)
    {
        _logger.LogInformation("[DjeJob] Iniciando captura de publicações DJE.");

        AppDbContext context;
        List<IDjeAdapter> adapters;

        if (_context != null && _adapters != null)
        {
            context = _context;
            adapters = _adapters.ToList();
        }
        else
        {
            using var scope = _serviceProvider!.CreateScope();
            context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            adapters = scope.ServiceProvider.GetServices<IDjeAdapter>().ToList();
        }

        if (adapters.Count == 0)
        {
            _logger.LogWarning("[DjeJob] Nenhum adapter DJE registrado.");
            return;
        }

        var nomesCaptura = await context.NomesCaptura
            .Where(n => n.Ativo)
            .Select(n => new { n.Id, n.TenantId, n.Nome })
            .ToListAsync(ct);

        if (!nomesCaptura.Any())
        {
            _logger.LogInformation("[DjeJob] Nenhum nome de captura configurado.");
            return;
        }

        var tenantIds = nomesCaptura.Select(n => n.TenantId).Distinct().ToList();
        int totalNovas = 0;

        foreach (var tenantId in tenantIds)
        {
            var nomesTenant = nomesCaptura.Where(n => n.TenantId == tenantId).ToList();

            foreach (var nome in nomesTenant)
            {
                foreach (var adapter in adapters)
                {
                    try
                    {
                        var resultado = await adapter.ConsultarPorNomeAsync(
                            nome.Nome,
                            dataInicio: DateTime.UtcNow.AddDays(-7),
                            dataFim: DateTime.UtcNow,
                            ct);

                        if (!resultado.Sucesso || resultado.Publicacoes.Count == 0)
                            continue;

                        var novas = await ProcessarPublicacoesAsync(
                            context, tenantId, nome.Nome, adapter.Sigla, resultado.Publicacoes, ct);

                        totalNovas += novas;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[DjeJob] Erro ao consultar {Adapter} para '{Nome}'",
                            adapter.Sigla, nome.Nome);
                    }
                }
            }
        }

        _logger.LogInformation("[DjeJob] Concluído. {Total} nova(s) publicação(ões) capturada(s).", totalNovas);
    }

    private async Task<int> ProcessarPublicacoesAsync(
        AppDbContext context,
        Guid tenantId,
        string nome,
        string siglaTribunal,
        List<DjePublicacao> publicacoes,
        CancellationToken ct)
    {
        var processoIdsJaNotificados = new HashSet<Guid?>();

        foreach (var pub in publicacoes)
        {
            var hash = GerarHash(pub);
            var jaExiste = await context.Publicacoes
                .AnyAsync(p => p.HashDje == hash, ct);

            if (jaExiste)
            {
                _logger.LogDebug("[DjeJob] Publicação duplicada: {Hash}", hash[..8]);
                continue;
            }

            Guid? processoId = null;
            if (!string.IsNullOrEmpty(pub.Conteudo))
            {
                var numeroProcesso = ExtrairNumeroProcesso(pub.Conteudo);
                if (numeroProcesso != null)
                {
                    var processo = await context.Processos
                        .Where(p => p.TenantId == tenantId && p.NumeroCNJ == numeroProcesso)
                        .Select(p => new { p.Id, p.AdvogadoResponsavelId })
                        .FirstOrDefaultAsync(ct);

                    processoId = processo?.Id;
                }
            }

            var tipo = InferirTipo(pub.Tipo ?? pub.Titulo ?? pub.Conteudo);
            var urgente = pub.Urgente || (pub.PrazoDias.HasValue && pub.PrazoDias < 5);

            context.Publicacoes.Add(new Publicacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcessoId = processoId,
                NumeroCNJ = ExtrairNumeroProcesso(pub.Conteudo),
                Diario = siglaTribunal,
                DataPublicacao = pub.DataPublicacao,
                Conteudo = pub.Conteudo,
                Tipo = tipo,
                Status = StatusPublicacao.Nova,
                Urgente = urgente,
                IdExterno = pub.Id,
                HashDje = hash,
                Secao = pub.Secao,
                Pagina = pub.Pagina,
                ClassificacaoIA = pub.Tipo,
                CapturaEm = DateTime.UtcNow
            });

            if (processoId.HasValue)
                processoIdsJaNotificados.Add(processoId);
        }

        await context.SaveChangesAsync(ct);

        if (processoIdsJaNotificados.Count > 0)
        {
            await NotificarAdvogadosAsync(context, tenantId, processoIdsJaNotificados, ct);
        }

        return publicacoes.Count;
    }

    private async Task NotificarAdvogadosAsync(
        AppDbContext context,
        Guid tenantId,
        HashSet<Guid?> processoIds,
        CancellationToken ct)
    {
        var responsaveis = await context.Processos
            .Where(p => processoIds.Contains(p.Id) && p.AdvogadoResponsavelId.HasValue)
            .Select(p => new { p.AdvogadoResponsavelId, p.NumeroCNJ })
            .Distinct()
            .ToListAsync(ct);

        foreach (var r in responsaveis)
        {
            var advogado = await context.Users
                .Where(u => u.Id == r.AdvogadoResponsavelId)
                .Select(u => new { u.Id, u.Nome, u.Email })
                .FirstOrDefaultAsync(ct);

            if (advogado == null) continue;

            context.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UsuarioId = advogado.Id,
                Tipo = TipoNotificacao.Geral,
                Titulo = "Nova publicação DJE",
                Mensagem = $"Nova publicação no processo {r.NumeroCNJ} foi capturada de diário oficial.",
                Url = "/pages/publicacoes.html",
                Lida = false,
                CriadaEm = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync(ct);
    }

    internal static string GerarHash(DjePublicacao pub)
    {
        var input = $"{pub.SiglaTribunal}|{pub.DataPublicacao:yyyyMMdd}|{pub.Tipo}|{pub.Titulo}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..32];
    }

    private static string? ExtrairNumeroProcesso(string texto)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            texto, @"\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}");
        return match.Success ? match.Value : null;
    }

    private static TipoPublicacao InferirTipo(string texto)
    {
        var lower = texto.ToLowerInvariant();
        return lower switch
        {
            var s when s.Contains("prazo") || s.Contains("recurso") => TipoPublicacao.Prazo,
            var s when s.Contains("audiên") || s.Contains("audienc") || s.Contains("julgament") => TipoPublicacao.Audiencia,
            var s when s.Contains("decis") || s.Contains("senten") || s.Contains("acórd") => TipoPublicacao.Decisao,
            var s when s.Contains("despacho") => TipoPublicacao.Despacho,
            var s when s.Contains("intim") => TipoPublicacao.Intimacao,
            _ => TipoPublicacao.Outro
        };
    }
}
