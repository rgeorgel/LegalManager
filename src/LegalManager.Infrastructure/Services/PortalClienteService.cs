using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LegalManager.Application.DTOs.PortalCliente;
using LegalManager.Application.DTOs.Tenants;
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

public class PortalClienteService : IPortalClienteService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IPasswordHasher<AcessoCliente> _hasher;
    private readonly IEmailService _email;
    private readonly ILogger<PortalClienteService> _logger;

    public static readonly TimeSpan ValidadeRedefinicao = TimeSpan.FromHours(1);

    /// <summary>Envios do cliente no mesmo processo dentro desta janela somam no mesmo aviso.</summary>
    public static readonly TimeSpan JanelaAgrupamentoDocumentos = TimeSpan.FromMinutes(10);
    private static readonly Regex QuantidadeDocumentos = new(@"^(\d+) documentos pelo Portal do Cliente", RegexOptions.Compiled);

    public PortalClienteService(
        AppDbContext context,
        IConfiguration config,
        IPasswordHasher<AcessoCliente> hasher,
        IEmailService email,
        ILogger<PortalClienteService> logger)
    {
        _context = context;
        _config = config;
        _hasher = hasher;
        _email = email;
        _logger = logger;
    }

    public async Task<PortalAuthResponseDto> LoginAsync(LoginPortalDto dto, CancellationToken ct = default)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        // O mesmo e-mail pode ter acesso em mais de um escritório; cada acesso é de um só
        // escritório (o JWT leva um tenant). A senha é conferida em cada um — nunca se entra num
        // acesso sem a senha dele, senão um escritório que criasse um acesso com o e-mail do
        // cliente e uma senha conhecida passaria a ver os processos dele nos outros escritórios.
        var acessos = await _context.AcessosCliente
            .Include(a => a.Contato)
            .Include(a => a.Tenant)
            .Where(a => a.Email == email && a.Ativo)
            .ToListAsync(ct);
        if (acessos.Count == 0)
            throw new UnauthorizedAccessException("E-mail ou senha inválidos.");

        // Preferência: o acesso que é parte do processo do link; depois o usado mais recentemente.
        var contatosDoProcesso = dto.ProcessoId is { } processoId
            ? await _context.ProcessoPartes
                .Where(pt => pt.ProcessoId == processoId)
                .Select(pt => new { pt.ContatoId, pt.Processo.TenantId })
                .ToListAsync(ct)
            : [];
        var candidatos = acessos
            .OrderByDescending(a => contatosDoProcesso.Any(c => c.ContatoId == a.ContatoId && c.TenantId == a.TenantId))
            .ThenByDescending(a => a.UltimoAcessoEm ?? DateTime.MinValue)
            .ThenBy(a => a.CriadoEm)
            .ToList();

        var agora = DateTime.UtcNow;
        var liberados = candidatos.Where(a => !a.EstaBloqueado(agora)).ToList();
        if (liberados.Count == 0)
            throw new UnauthorizedAccessException(BloqueioLogin.Mensagem);

        var acesso = liberados.FirstOrDefault(a =>
            _hasher.VerifyHashedPassword(a, a.SenhaHash, dto.Senha) != PasswordVerificationResult.Failed);
        if (acesso == null)
        {
            foreach (var a in liberados) a.RegistrarFalha(agora);
            await _context.SaveChangesAsync(ct);
            var todosBloqueados = candidatos.All(a => a.EstaBloqueado(agora));
            throw new UnauthorizedAccessException(todosBloqueados ? BloqueioLogin.Mensagem : "E-mail ou senha inválidos.");
        }

        acesso.RegistrarSucesso();
        acesso.UltimoAcessoEm = agora;
        await _context.SaveChangesAsync(ct);

        var (token, expires) = GerarJwt(acesso);

        return new PortalAuthResponseDto(
            token,
            expires,
            new ClientePerfilDto(
                acesso.Id,
                acesso.ContatoId,
                acesso.Contato.Nome,
                acesso.Email,
                acesso.Contato.Telefone,
                TemaDo(acesso.Tenant),
                acesso.Tenant.Nome
            )
        );
    }

    /// <summary>
    /// "Esqueci minha senha" do portal. E-mail desconhecido é ignorado em silêncio. O login é só
    /// por e-mail, então o mesmo link vale para todos os acessos ativos com ele (cliente de mais
    /// de um escritório) e a senha nova passa a valer em todos.
    /// </summary>
    public async Task SolicitarRedefinicaoAsync(string emailInformado, CancellationToken ct = default)
    {
        var email = emailInformado.Trim().ToLowerInvariant();
        var acessos = await _context.AcessosCliente
            .Where(a => a.Email == email && a.Ativo)
            .ToListAsync(ct);
        if (acessos.Count == 0) return;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var expiraEm = DateTime.UtcNow.Add(ValidadeRedefinicao);
        foreach (var acesso in acessos)
        {
            acesso.TokenRedefinicao = HashToken(token);
            acesso.TokenRedefinicaoExpiraEm = expiraEm;
        }
        await _context.SaveChangesAsync(ct);

        var link = $"{_config["App:FrontendUrl"]}/cliente/definir-senha.html?token={Uri.EscapeDataString(token)}";
        try { await _email.EnviarResetSenhaAsync(email, link, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Falha ao enviar redefinição de senha do portal ao acesso {AcessoId}", acessos[0].Id); }
    }

    public async Task RedefinirSenhaAsync(RedefinirSenhaPortalDto dto, CancellationToken ct = default)
    {
        var hash = HashToken(dto.Token);
        var agora = DateTime.UtcNow;
        var acessos = await _context.AcessosCliente
            .Where(a => a.TokenRedefinicao == hash && a.Ativo && a.TokenRedefinicaoExpiraEm > agora)
            .ToListAsync(ct);
        if (acessos.Count == 0)
            throw new InvalidOperationException("Link inválido ou expirado. Peça um novo link em \"Esqueci minha senha\".");

        foreach (var acesso in acessos)
        {
            acesso.SenhaHash = _hasher.HashPassword(acesso, dto.NovaSenha);
            acesso.TokenRedefinicao = null;
            acesso.TokenRedefinicaoExpiraEm = null;
            acesso.RegistrarSucesso(); // quem provou ter acesso ao e-mail sai do bloqueio
        }
        await _context.SaveChangesAsync(ct);
    }

    public async Task<ClientePerfilDto> GetPerfilAsync(Guid acessoId, CancellationToken ct = default)
    {
        var acesso = await _context.AcessosCliente
            .Include(a => a.Contato)
            .Include(a => a.Tenant)
            .FirstOrDefaultAsync(a => a.Id == acessoId, ct)
            ?? throw new KeyNotFoundException("Acesso não encontrado.");

        // O portal relê o perfil a cada página: mudanças de tema/logo do escritório aparecem sem novo login.
        return new ClientePerfilDto(acesso.Id, acesso.ContatoId, acesso.Contato.Nome, acesso.Email,
            acesso.Contato.Telefone, TemaDo(acesso.Tenant), acesso.Tenant.Nome);
    }

    private static TenantThemeDto TemaDo(Tenant tenant) => new(
        tenant.PrimaryColor,
        tenant.SidebarColor,
        tenant.AccentColor,
        tenant.LayoutMode,
        tenant.LogoUrl,
        tenant.CustomCss);

    public async Task<IEnumerable<MeuProcessoDto>> GetMeusProcessosAsync(
        Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var processos = await _context.Processos
            .Where(p => p.TenantId == tenantId &&
                        p.Partes.Any(pt => pt.ContatoId == contatoId) &&
                        p.Status != StatusProcesso.Arquivado)
            .Select(p => new
            {
                p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca, p.AreaDireito,
                p.Fase, p.Status, p.CriadoEm,
                TipoParte = p.Partes
                    .Where(pt => pt.ContatoId == contatoId)
                    .Select(pt => pt.TipoParte)
                    .FirstOrDefault(),
                TotalAndamentos = p.Andamentos.Count(a => a.VisivelCliente)
            })
            .ToListAsync(ct);

        return processos.Select(p => new MeuProcessoDto(
            p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca, p.AreaDireito,
            p.Fase, p.Status, p.TipoParte, p.TotalAndamentos, p.CriadoEm));
    }

    public async Task<MeuProcessoDto?> GetProcessoAsync(
        Guid processoId, Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var p = await _context.Processos
            .Where(p => p.Id == processoId && p.TenantId == tenantId &&
                        p.Partes.Any(pt => pt.ContatoId == contatoId))
            .Select(p => new
            {
                p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca, p.AreaDireito,
                p.Fase, p.Status, p.CriadoEm,
                TipoParte = p.Partes
                    .Where(pt => pt.ContatoId == contatoId)
                    .Select(pt => pt.TipoParte)
                    .FirstOrDefault(),
                TotalAndamentos = p.Andamentos.Count(a => a.VisivelCliente)
            })
            .FirstOrDefaultAsync(ct);

        if (p == null) return null;

        return new MeuProcessoDto(p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca,
            p.AreaDireito, p.Fase, p.Status, p.TipoParte, p.TotalAndamentos, p.CriadoEm);
    }

    public async Task<IEnumerable<MeuAndamentoDto>> GetAndamentosAsync(
        Guid processoId, Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var autorizado = await _context.Processos
            .AnyAsync(p => p.Id == processoId && p.TenantId == tenantId &&
                           p.Partes.Any(pt => pt.ContatoId == contatoId), ct);

        if (!autorizado) throw new UnauthorizedAccessException("Acesso negado ao processo.");

        return await _context.Andamentos
            .Where(a => a.ProcessoId == processoId && a.VisivelCliente)
            .OrderByDescending(a => a.Data)
            .Select(a => new MeuAndamentoDto(
                a.Id, a.Data, a.Tipo, a.Descricao, a.DescricaoTraduzidaIA, a.Fonte, a.CriadoEm))
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<MeuResumoDto>> GetResumosAsync(
        Guid processoId, Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var autorizado = await _context.Processos
            .AnyAsync(p => p.Id == processoId && p.TenantId == tenantId &&
                           p.Partes.Any(pt => pt.ContatoId == contatoId), ct);

        if (!autorizado) throw new UnauthorizedAccessException("Acesso negado ao processo.");

        return await _context.ResumosProcesso
            .Where(r => r.ProcessoId == processoId && r.TenantId == tenantId && r.VisivelCliente
                        && r.Status == StatusResumoProcesso.Concluido)
            .OrderByDescending(r => r.GeradoEm)
            .Select(r => new MeuResumoDto(
                r.Id, r.Conteudo, r.GeradoPor!.UserName ?? "Escritório", r.GeradoEm))
            .ToListAsync(ct);
    }

    // Vai para o advogado responsável pelo processo; sem responsável ativo, para os admins do escritório.
    public async Task NotificarDocumentoEnviadoAsync(
        Guid processoId, Guid contatoId, Guid tenantId, string nomeDocumento, CancellationToken ct = default)
    {
        try
        {
            var processo = await _context.Processos.AsNoTracking()
                .Where(p => p.Id == processoId && p.TenantId == tenantId)
                .Select(p => new { p.NumeroCNJ, p.AdvogadoResponsavelId })
                .FirstOrDefaultAsync(ct);
            if (processo == null) return;

            var nomeCliente = await _context.Contatos.AsNoTracking()
                .Where(c => c.Id == contatoId && c.TenantId == tenantId)
                .Select(c => c.Nome)
                .FirstOrDefaultAsync(ct) ?? "Cliente";

            var ativos = _context.Users.Where(u => u.TenantId == tenantId && u.Ativo);
            var destinatarios = processo.AdvogadoResponsavelId is { } responsavelId
                    && await ativos.AnyAsync(u => u.Id == responsavelId, ct)
                ? [responsavelId]
                : await ativos.Where(u => u.Perfil == PerfilUsuario.Admin).Select(u => u.Id).ToListAsync(ct);

            var agora = DateTime.UtcNow;
            var documento = nomeDocumento.Length > 200 ? nomeDocumento[..200] + "…" : nomeDocumento;
            var url = $"/pages/processo-detalhe.html?id={processoId}&tab=documentos";
            var prefixo = $"{nomeCliente} enviou ";
            var desde = agora - JanelaAgrupamentoDocumentos;
            foreach (var usuarioId in destinatarios)
            {
                // Vários arquivos de uma vez (o portal envia um por requisição) viram um aviso só:
                // o ainda não lido, do mesmo cliente e processo, dos últimos minutos, é atualizado.
                var recente = await _context.Notificacoes
                    .Where(n => n.UsuarioId == usuarioId && n.TenantId == tenantId && !n.Lida
                             && n.Tipo == TipoNotificacao.DocumentoCliente && n.Url == url && n.CriadaEm >= desde)
                    .OrderByDescending(n => n.CriadaEm)
                    .FirstOrDefaultAsync(ct);
                if (recente != null && recente.Mensagem.StartsWith(prefixo, StringComparison.Ordinal))
                {
                    var anteriores = QuantidadeDocumentos.Match(recente.Mensagem[prefixo.Length..]) is { Success: true } m
                        ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
                        : 1;
                    recente.Titulo = $"Documentos enviados pelo cliente — {processo.NumeroCNJ}";
                    recente.Mensagem = $"{prefixo}{anteriores + 1} documentos pelo Portal do Cliente. Último: \"{documento}\".";
                    recente.CriadaEm = agora;
                    continue;
                }

                _context.Notificacoes.Add(new Notificacao
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UsuarioId = usuarioId,
                    Tipo = TipoNotificacao.DocumentoCliente,
                    Titulo = $"Documento enviado pelo cliente — {processo.NumeroCNJ}",
                    Mensagem = $"{prefixo}\"{documento}\" pelo Portal do Cliente.",
                    Url = url,
                    Lida = false,
                    CriadaEm = agora
                });
            }
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // O documento já foi gravado: falha no aviso não pode virar erro para o cliente.
            _logger.LogError(ex, "Erro ao notificar documento enviado pelo cliente no processo {ProcessoId}", processoId);
        }
    }

    public async Task<AcessoPortalInfoDto> CriarAcessoAsync(
        Guid contatoId, CriarAcessoPortalDto dto, Guid tenantId, CancellationToken ct = default)
    {
        var contato = await _context.Contatos
            .FirstOrDefaultAsync(c => c.Id == contatoId && c.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contato não encontrado.");

        var nomeEscritorio = await _context.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Nome)
            .FirstOrDefaultAsync(ct) ?? "Escritório";

        var portalUrl = $"{_config["App:FrontendUrl"]}/cliente/index.html";
        var emailNormalizado = dto.Email.Trim().ToLowerInvariant();

        var existente = await _context.AcessosCliente
            .FirstOrDefaultAsync(a => a.ContatoId == contatoId && a.TenantId == tenantId, ct);

        if (existente != null)
        {
            existente.Email = emailNormalizado;
            existente.SenhaHash = _hasher.HashPassword(existente, dto.Senha);
            existente.RegistrarSucesso(); // senha nova do escritório desbloqueia o acesso
            existente.Ativo = true;
            await _context.SaveChangesAsync(ct);
            await _email.EnviarAcessoPortalAsync(emailNormalizado, contato.Nome, nomeEscritorio, dto.Senha, portalUrl, ct);
            return MapInfo(existente);
        }

        if (await _context.AcessosCliente.AnyAsync(
                a => a.TenantId == tenantId && a.Email == emailNormalizado, ct))
            throw new InvalidOperationException("Este e-mail já está em uso no portal.");

        var acesso = new AcessoCliente
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContatoId = contatoId,
            Email = emailNormalizado,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        acesso.SenhaHash = _hasher.HashPassword(acesso, dto.Senha);

        _context.AcessosCliente.Add(acesso);
        await _context.SaveChangesAsync(ct);
        _ = _email.EnviarAcessoPortalAsync(emailNormalizado, contato.Nome, nomeEscritorio, dto.Senha, portalUrl, ct);

        return MapInfo(acesso);
    }

    public async Task<AcessoPortalInfoDto?> GetAcessoAsync(Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var a = await _context.AcessosCliente
            .FirstOrDefaultAsync(x => x.ContatoId == contatoId && x.TenantId == tenantId, ct);
        return a == null ? null : MapInfo(a);
    }

    public async Task RevogarAcessoAsync(Guid contatoId, Guid tenantId, CancellationToken ct = default)
    {
        var acesso = await _context.AcessosCliente
            .FirstOrDefaultAsync(a => a.ContatoId == contatoId && a.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Acesso ao portal não encontrado.");

        _context.AcessosCliente.Remove(acesso);
        await _context.SaveChangesAsync(ct);
    }

    private (string token, DateTime expires) GerarJwt(AcessoCliente acesso)
    {
        var jwtSettings = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!));
        var expires = DateTime.UtcNow.AddHours(24);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, acesso.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, acesso.Email),
            new Claim("tenantId", acesso.TenantId.ToString()),
            new Claim("contatoId", acesso.ContatoId.ToString()),
            new Claim(ClaimTypes.Role, "Cliente"),
            new Claim("tipo", "portal"),
            new Claim("nome", acesso.Contato.Nome),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    // Só o hash fica no banco: um vazamento da tabela não entrega links de senha válidos.
    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static AcessoPortalInfoDto MapInfo(AcessoCliente a) =>
        new(a.Id, a.ContatoId, a.Email, a.Ativo, a.CriadoEm, a.UltimoAcessoEm,
            !string.IsNullOrEmpty(a.TokenConvite) && a.TokenConviteExpiraEm > DateTime.UtcNow);
}
