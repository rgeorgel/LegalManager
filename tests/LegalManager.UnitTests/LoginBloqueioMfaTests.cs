using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Bloqueio por senhas erradas e login em duas etapas com UserManager de verdade (EF InMemory):
/// com mock, o Identity Lockout e o TOTP não seriam exercitados.
/// </summary>
public class LoginBloqueioMfaTests : IDisposable
{
    private const string Senha = "SenhaCorreta123";
    private const string Email = "advogado@teste.com";

    private readonly ServiceProvider _sp;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly UserManager<Usuario> _userManager;
    private readonly AuthService _auth;
    private readonly MfaService _mfa;
    private Usuario _usuario = null!;

    public LoginBloqueioMfaTests()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<Usuario>(o =>
            {
                // Mesmos valores de Program.cs.
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequiredLength = 8;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "chave-de-teste-com-pelo-menos-32-caracteres!!",
            ["Jwt:Issuer"] = "LegalManager",
            ["Jwt:Audience"] = "LegalManager",
            ["App:FrontendUrl"] = "https://app.teste"
        }).Build();

        _auth = new AuthService(_userManager, config, new Mock<IEmailService>().Object,
            new Mock<ICreditoService>().Object, _db, new Mock<IGoogleTokenValidator>().Object);
        _mfa = new MfaService(_userManager);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _sp.Dispose();
    }

    private async Task CriarUsuarioAsync(bool ativo = true)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        _usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Email = Email, UserName = Email,
            Nome = "Advogado", Perfil = PerfilUsuario.Admin, Ativo = ativo, CriadoEm = DateTime.UtcNow
        };
        var r = await _userManager.CreateAsync(_usuario, Senha);
        Assert.True(r.Succeeded, string.Join("; ", r.Errors.Select(e => e.Description)));
    }

    private Task<AuthResponseDto> Login(string senha) => _auth.LoginAsync(new LoginDto(Email, senha));

    // ── Bloqueio ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CincoSenhasErradas_BloqueiamAContaMesmoComASenhaCerta()
    {
        await CriarUsuarioAsync();

        for (var i = 1; i <= 4; i++)
        {
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("errada"));
            Assert.Equal("Credenciais inválidas.", ex.Message);
        }

        var quinta = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("errada"));
        Assert.Contains("bloqueada", quinta.Message);

        var comSenhaCerta = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login(Senha));
        Assert.Contains("bloqueada", comSenhaCerta.Message);
    }

    [Fact]
    public async Task LoginCerto_ZeraAsFalhasAnteriores()
    {
        await CriarUsuarioAsync();
        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("errada"));

        await Login(Senha);

        // Contagem zerou: mais 4 erros ainda não bloqueiam.
        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("errada"));
        Assert.NotNull(await Login(Senha));
    }

    [Fact]
    public async Task UsuarioDesativado_ComSenhaErrada_NaoRevelaQueOEmailExiste()
    {
        await CriarUsuarioAsync(ativo: false);
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("errada"));
        Assert.Equal("Credenciais inválidas.", ex.Message);
    }

    // ── MFA ──────────────────────────────────────────────────────────────────

    private async Task<(string chave, IReadOnlyList<string> codigos)> AtivarMfaAsync()
    {
        var cfg = await _mfa.ConfigurarAsync(_usuario.Id);
        Assert.StartsWith("data:image/png;base64,", cfg.QrCodeDataUri);
        var chave = cfg.Chave.Replace(" ", string.Empty).ToUpperInvariant();
        var r = await _mfa.AtivarAsync(_usuario.Id, Totp(chave));
        return (chave, r.Codigos);
    }

    [Fact]
    public async Task ComMfa_SenhaCertaNaoDevolveJwt_PedeOCodigo()
    {
        await CriarUsuarioAsync();
        await AtivarMfaAsync();

        var ex = await Assert.ThrowsAsync<MfaNecessarioException>(() => Login(Senha));

        // O token da 1ª etapa tem audience própria: o pipeline JWT das APIs o recusa.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(ex.MfaToken);
        Assert.Equal("LegalManager:mfa", jwt.Audiences.Single());
        Assert.True(jwt.ValidTo <= DateTime.UtcNow.AddMinutes(6));
    }

    [Fact]
    public async Task ComMfa_CodigoDoApp_ConcluiOLogin()
    {
        await CriarUsuarioAsync();
        var (chave, _) = await AtivarMfaAsync();
        var mfaToken = (await Assert.ThrowsAsync<MfaNecessarioException>(() => Login(Senha))).MfaToken;

        var result = await _auth.VerificarMfaAsync(new VerificarMfaDto(mfaToken, Totp(chave)));

        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        Assert.Equal(Email, result.Usuario.Email);
    }

    [Fact]
    public async Task ComMfa_CodigoErrado_RecusaEContaParaOBloqueio()
    {
        await CriarUsuarioAsync();
        var (chave, _) = await AtivarMfaAsync();
        var mfaToken = (await Assert.ThrowsAsync<MfaNecessarioException>(() => Login(Senha))).MfaToken;
        var errado = Totp(chave) == "000000" ? "111111" : "000000";

        for (var i = 1; i <= 4; i++)
        {
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _auth.VerificarMfaAsync(new VerificarMfaDto(mfaToken, errado)));
            Assert.Equal("Código inválido.", ex.Message);
        }
        var quinta = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.VerificarMfaAsync(new VerificarMfaDto(mfaToken, errado)));
        Assert.Contains("bloqueada", quinta.Message);

        // Bloqueada: nem o código certo passa.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.VerificarMfaAsync(new VerificarMfaDto(mfaToken, Totp(chave))));
    }

    [Fact]
    public async Task ComMfa_CodigoDeRecuperacao_ValeUmaVezSo()
    {
        await CriarUsuarioAsync();
        var (_, codigos) = await AtivarMfaAsync();
        Assert.Equal(10, codigos.Count);

        var token1 = (await Assert.ThrowsAsync<MfaNecessarioException>(() => Login(Senha))).MfaToken;
        Assert.NotNull(await _auth.VerificarMfaAsync(new VerificarMfaDto(token1, codigos[0].ToLowerInvariant())));

        var token2 = (await Assert.ThrowsAsync<MfaNecessarioException>(() => Login(Senha))).MfaToken;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.VerificarMfaAsync(new VerificarMfaDto(token2, codigos[0])));
        Assert.Equal(9, (await _mfa.StatusAsync(_usuario.Id)).CodigosRecuperacaoRestantes);
    }

    [Fact]
    public async Task MfaToken_InvalidoOuDeOutroLugar_Recusa()
    {
        await CriarUsuarioAsync();
        var (chave, _) = await AtivarMfaAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.VerificarMfaAsync(new VerificarMfaDto("nao-e-um-jwt", Totp(chave))));

        // O access token normal (audience das APIs) não serve como mfaToken.
        await _mfa.DesativarAsync(_usuario.Id, Senha);
        var accessToken = (await Login(Senha)).AccessToken;
        await AtivarMfaAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.VerificarMfaAsync(new VerificarMfaDto(accessToken, Totp(chave))));
    }

    [Fact]
    public async Task AtivarComCodigoErrado_NaoLigaOMfa()
    {
        await CriarUsuarioAsync();
        await _mfa.ConfigurarAsync(_usuario.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => _mfa.AtivarAsync(_usuario.Id, "123"));
        Assert.False((await _mfa.StatusAsync(_usuario.Id)).Ativo);
    }

    [Fact]
    public async Task Desativar_ExigeASenha_EDepoisLoginVoltaASerSoComSenha()
    {
        await CriarUsuarioAsync();
        await AtivarMfaAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _mfa.DesativarAsync(_usuario.Id, "errada"));
        Assert.True((await _mfa.StatusAsync(_usuario.Id)).Ativo);

        await _mfa.DesativarAsync(_usuario.Id, Senha);
        Assert.NotNull(await Login(Senha));
    }

    [Fact]
    public async Task SuperAdmin_DesativaOMfaDeQuemPerdeuOCelular_EAChaveAntigaDeixaDeValer()
    {
        await CriarUsuarioAsync();
        var (chaveAntiga, _) = await AtivarMfaAsync();

        Assert.True(await _mfa.DesativarPeloSuperAdminAsync(_usuario.Id));
        Assert.False(await _mfa.DesativarPeloSuperAdminAsync(_usuario.Id));
        Assert.NotNull(await Login(Senha));

        var novaChave = (await _mfa.ConfigurarAsync(_usuario.Id)).Chave.Replace(" ", string.Empty).ToUpperInvariant();
        Assert.NotEqual(chaveAntiga, novaChave);
    }

    // TOTP (RFC 6238: HMAC-SHA1, passos de 30 s, 6 dígitos) — o mesmo que o app autenticador gera.
    private static string Totp(string chaveBase32)
    {
        var chave = Base32(chaveBase32);
        var passo = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var contador = BitConverter.GetBytes(passo);
        if (BitConverter.IsLittleEndian) Array.Reverse(contador);
        var hash = HMACSHA1.HashData(chave, contador);
        var offset = hash[^1] & 0x0F;
        var binario = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binario % 1_000_000).ToString("D6");
    }

    private static byte[] Base32(string texto)
    {
        const string alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in texto.TrimEnd('='))
        {
            buffer = (buffer << 5) | alfabeto.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }
}
