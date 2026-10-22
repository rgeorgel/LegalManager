using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LegalManager.Application.DTOs.Parceiros;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Conta do parceiro (login, convite e redefinição de senha) e as consultas do portal do
/// parceiro, reaproveitadas pelo super admin. Ver Parceiro e ComissoesParceiros.
/// </summary>
public class ParceiroService(
    AppDbContext db,
    IPasswordHasher<Parceiro> hasher,
    IEmailService email,
    IConfiguration config,
    ILogger<ParceiroService> logger)
{
    public const string Role = "Parceiro";
    public static readonly TimeSpan ValidadeConvite = TimeSpan.FromDays(7);
    // O e-mail de redefinição (EnviarResetSenhaAsync) avisa que o link vale 1 hora.
    public static readonly TimeSpan ValidadeRedefinicao = TimeSpan.FromHours(1);

    // ── Conta ────────────────────────────────────────────────────────────────

    public async Task<ParceiroAuthResponseDto> LoginAsync(LoginParceiroDto dto, CancellationToken ct = default)
    {
        var emailNorm = dto.Email.Trim().ToLowerInvariant();
        var parceiro = await db.Parceiros.FirstOrDefaultAsync(p => p.Email == emailNorm, ct);
        if (parceiro is not { Ativo: true } || parceiro.SenhaHash is null
            || hasher.VerifyHashedPassword(parceiro, parceiro.SenhaHash, dto.Senha) == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException("E-mail ou senha inválidos.");

        parceiro.UltimoAcessoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return GerarAuthResponse(parceiro);
    }

    /// <summary>Define a senha a partir do link de convite ou de redefinição, e já entra no portal.</summary>
    public async Task<ParceiroAuthResponseDto> DefinirSenhaAsync(DefinirSenhaParceiroDto dto, CancellationToken ct = default)
    {
        var hash = HashToken(dto.Token);
        var parceiro = await db.Parceiros.FirstOrDefaultAsync(p => p.TokenSenha == hash, ct);
        if (parceiro is not { Ativo: true } || parceiro.TokenSenhaExpiraEm < DateTime.UtcNow)
            throw new InvalidOperationException("Link inválido ou expirado. Peça um novo link.");

        parceiro.SenhaHash = hasher.HashPassword(parceiro, dto.Senha);
        parceiro.TokenSenha = null;
        parceiro.TokenSenhaExpiraEm = null;
        parceiro.UltimoAcessoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return GerarAuthResponse(parceiro);
    }

    /// <summary>"Esqueci minha senha" do portal. E-mail desconhecido é ignorado em silêncio.</summary>
    public async Task EsqueciSenhaAsync(string emailInformado, CancellationToken ct = default)
    {
        var emailNorm = emailInformado.Trim().ToLowerInvariant();
        var parceiro = await db.Parceiros.FirstOrDefaultAsync(p => p.Email == emailNorm, ct);
        if (parceiro is not { Ativo: true }) return;

        var link = await GerarLinkSenhaAsync(parceiro, ValidadeRedefinicao, ct);
        try { await email.EnviarResetSenhaAsync(parceiro.Email, link.Link, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao enviar redefinição de senha ao parceiro {ParceiroId}", parceiro.Id); }
    }

    /// <summary>
    /// Convite (ou novo link de senha) gerado pelo super admin: invalida o link anterior, envia por
    /// e-mail e devolve o link para ser repassado por outro canal se preciso.
    /// </summary>
    public async Task<LinkSenhaParceiroDto> EnviarConviteAsync(Parceiro parceiro, CancellationToken ct = default)
    {
        var link = await GerarLinkSenhaAsync(parceiro, ValidadeConvite, ct);
        try { await email.EnviarConviteParceiroAsync(parceiro.Email, parceiro.Nome, parceiro.Codigo, link.Link, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao enviar convite ao parceiro {ParceiroId}", parceiro.Id); }
        return link;
    }

    private async Task<LinkSenhaParceiroDto> GerarLinkSenhaAsync(Parceiro parceiro, TimeSpan validade, CancellationToken ct)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        parceiro.TokenSenha = HashToken(token);
        parceiro.TokenSenhaExpiraEm = DateTime.UtcNow.Add(validade);
        await db.SaveChangesAsync(ct);

        var link = $"{config["App:FrontendUrl"]}/parceiro/definir-senha.html?token={Uri.EscapeDataString(token)}";
        return new LinkSenhaParceiroDto(link, parceiro.TokenSenhaExpiraEm.Value);
    }

    // Só o hash fica no banco: um vazamento da tabela não entrega links de senha válidos.
    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private ParceiroAuthResponseDto GerarAuthResponse(Parceiro parceiro)
    {
        var jwt = config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
        var expires = DateTime.UtcNow.AddHours(12);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, parceiro.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, parceiro.Email),
            new Claim(ClaimTypes.Role, Role),
            new Claim("tipo", "parceiro"),
            new Claim("nome", parceiro.Nome),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new ParceiroAuthResponseDto(
            new JwtSecurityTokenHandler().WriteToken(token), expires,
            new ParceiroPerfilDto(parceiro.Id, parceiro.Nome, parceiro.Email, parceiro.Codigo));
    }

    public Task<bool> EstaAtivoAsync(Guid parceiroId, CancellationToken ct = default) =>
        db.Parceiros.AnyAsync(p => p.Id == parceiroId && p.Ativo, ct);

    // ── Cadastro de escritório ───────────────────────────────────────────────

    /// <summary>Parceiro ativo com o código, ou null. Usado no cadastro do escritório.</summary>
    public static Task<Parceiro?> BuscarPorCodigoAsync(AppDbContext db, string codigo, CancellationToken ct = default)
    {
        var code = Parceiro.Normalizar(codigo);
        return db.Parceiros.FirstOrDefaultAsync(p => p.Codigo == code && p.Ativo, ct);
    }

    // ── Consultas do portal (e do super admin) ──────────────────────────────

    public async Task<ParceiroResumoDto?> GetResumoAsync(Guid parceiroId, CancellationToken ct = default)
    {
        var parceiro = await db.Parceiros.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parceiroId, ct);
        if (parceiro is null) return null;

        var totais = await ComissoesParceiros.CalcularTotaisAsync(db, parceiroId, ct);
        var escritorios = await db.Tenants.Where(t => t.ParceiroId == parceiroId).ToListAsync(ct);
        var pendente = await db.SolicitacoesPagamentoParceiros
            .AnyAsync(s => s.ParceiroId == parceiroId && s.Status == StatusSolicitacaoPagamento.Pendente, ct);

        return new ParceiroResumoDto(
            parceiro.Nome, parceiro.Email, parceiro.Codigo, parceiro.PercentualComissao, parceiro.MesesComissao,
            parceiro.ChavePix, escritorios.Count,
            escritorios.Count(t => ComissoesParceiros.SituacaoEscritorio(t) == "Assinante"),
            totais.Total, totais.Disponivel, totais.Solicitado, totais.Pago, pendente);
    }

    /// <summary>
    /// Escritórios indicados (vinculados agora) e os que já geraram comissão mas foram
    /// desvinculados ou excluídos — esses aparecem pelo nome copiado na comissão.
    /// </summary>
    public async Task<List<EscritorioIndicadoDto>> ListarEscritoriosAsync(Guid parceiroId, CancellationToken ct = default)
    {
        var parceiro = await db.Parceiros.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parceiroId, ct);
        if (parceiro is null) return [];

        var comissoes = await db.ComissoesParceiros
            .Where(c => c.ParceiroId == parceiroId)
            .GroupBy(c => c.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Nome = g.OrderByDescending(c => c.PagoEm).Select(c => c.NomeEscritorio).First(),
                Primeiro = g.Min(c => c.PagoEm),
                Ultimo = g.Max(c => c.PagoEm),
                TotalPago = g.Sum(c => c.ValorPagamento),
                TotalComissao = g.Sum(c => c.Valor)
            })
            .ToDictionaryAsync(x => x.TenantId, ct);

        var ids = comissoes.Keys.ToList();
        var tenants = await db.Tenants.AsNoTracking()
            .Where(t => t.ParceiroId == parceiroId || ids.Contains(t.Id))
            .ToListAsync(ct);

        var lista = tenants.Select(t =>
        {
            comissoes.TryGetValue(t.Id, out var c);
            return new EscritorioIndicadoDto(
                t.Id, t.Nome, t.CriadoEm, t.ParceiroId == parceiroId ? t.ParceiroVinculadoEm : null,
                t.Plano.ToString(), t.PeriodoBilling,
                t.ParceiroId == parceiroId ? ComissoesParceiros.SituacaoEscritorio(t) : "Desvinculado",
                t.ValorMensalAssinatura, c?.Primeiro, c?.Ultimo, c?.TotalPago ?? 0, c?.TotalComissao ?? 0,
                ComissaoAte(parceiro, c?.Primeiro));
        }).ToList();

        // Escritórios excluídos: só restou o histórico de comissões.
        var existentes = tenants.Select(t => t.Id).ToHashSet();
        lista.AddRange(comissoes.Values.Where(c => !existentes.Contains(c.TenantId)).Select(c =>
            new EscritorioIndicadoDto(
                c.TenantId, c.Nome, c.Primeiro, null, "—", null, "Encerrado", null,
                c.Primeiro, c.Ultimo, c.TotalPago, c.TotalComissao, ComissaoAte(parceiro, c.Primeiro))));

        return lista.OrderByDescending(e => e.VinculadoEm ?? e.CadastradoEm).ToList();
    }

    private static DateTime? ComissaoAte(Parceiro parceiro, DateTime? primeiroPagamento) =>
        parceiro.MesesComissao is { } meses && primeiroPagamento is { } inicio ? inicio.AddMonths(meses) : null;

    public async Task<List<ComissaoParceiroDto>> ListarComissoesAsync(Guid parceiroId, CancellationToken ct = default)
    {
        var comissoes = await db.ComissoesParceiros.AsNoTracking()
            .Where(c => c.ParceiroId == parceiroId)
            .OrderByDescending(c => c.PagoEm)
            .Select(c => new
            {
                c.Id, c.TenantId, c.NomeEscritorio, c.Descricao, c.ValorPagamento, c.Percentual, c.Valor, c.PagoEm,
                c.SolicitacaoId,
                Status = c.Solicitacao == null ? (StatusSolicitacaoPagamento?)null : c.Solicitacao.Status
            })
            .ToListAsync(ct);

        return comissoes.Select(c => new ComissaoParceiroDto(
            c.Id, c.TenantId, c.NomeEscritorio, c.Descricao, c.ValorPagamento, c.Percentual, c.Valor, c.PagoEm,
            ComissoesParceiros.Situacao(c.Status), c.SolicitacaoId)).ToList();
    }

    public Task<List<SolicitacaoPagamentoDto>> ListarSolicitacoesAsync(Guid? parceiroId, StatusSolicitacaoPagamento? status = null,
        CancellationToken ct = default)
    {
        var query = db.SolicitacoesPagamentoParceiros.AsNoTracking();
        if (parceiroId is { } id) query = query.Where(s => s.ParceiroId == id);
        if (status is { } st) query = query.Where(s => s.Status == st);

        return query
            .OrderByDescending(s => s.SolicitadaEm)
            .Select(s => new SolicitacaoPagamentoDto(
                s.Id, s.ParceiroId, s.Parceiro.Nome, s.Valor, s.Status.ToString(), s.ChavePix, s.ObservacaoParceiro,
                s.SolicitadaEm, s.ResolvidaEm, s.Resposta, s.Comissoes.Count))
            .ToListAsync(ct);
    }

    // ── Solicitação de pagamento ─────────────────────────────────────────────

    /// <summary>
    /// Reúne todas as comissões disponíveis numa solicitação de pagamento. A chave Pix informada
    /// passa a ser a padrão do parceiro.
    /// </summary>
    public async Task<SolicitacaoPagamentoDto> SolicitarPagamentoAsync(Guid parceiroId, SolicitarPagamentoDto dto, CancellationToken ct = default)
    {
        var parceiro = await db.Parceiros.FirstOrDefaultAsync(p => p.Id == parceiroId && p.Ativo, ct)
            ?? throw new InvalidOperationException("Parceiro não encontrado.");

        if (await db.SolicitacoesPagamentoParceiros.AnyAsync(
                s => s.ParceiroId == parceiroId && s.Status == StatusSolicitacaoPagamento.Pendente, ct))
            throw new InvalidOperationException("Você já tem uma solicitação de pagamento em análise. Aguarde a conclusão para pedir outra.");

        var disponiveis = await db.ComissoesParceiros
            .Where(c => c.ParceiroId == parceiroId && c.SolicitacaoId == null)
            .ToListAsync(ct);
        var valor = disponiveis.Sum(c => c.Valor);
        if (valor <= 0)
            throw new InvalidOperationException("Não há comissões disponíveis para solicitar.");

        var chavePix = dto.ChavePix.Trim();
        var solicitacao = new SolicitacaoPagamentoParceiro
        {
            Id = Guid.NewGuid(),
            ParceiroId = parceiroId,
            Valor = valor,
            Status = StatusSolicitacaoPagamento.Pendente,
            ChavePix = chavePix,
            ObservacaoParceiro = string.IsNullOrWhiteSpace(dto.Observacao) ? null : dto.Observacao.Trim(),
            SolicitadaEm = DateTime.UtcNow
        };
        db.SolicitacoesPagamentoParceiros.Add(solicitacao);
        foreach (var c in disponiveis) c.SolicitacaoId = solicitacao.Id;
        parceiro.ChavePix = chavePix;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Parceiro {ParceiroId} solicitou pagamento de R$ {Valor} ({Qtd} comissões)",
            parceiroId, valor, disponiveis.Count);

        return new SolicitacaoPagamentoDto(
            solicitacao.Id, parceiroId, parceiro.Nome, valor, solicitacao.Status.ToString(), chavePix,
            solicitacao.ObservacaoParceiro, solicitacao.SolicitadaEm, null, null, disponiveis.Count);
    }
}
