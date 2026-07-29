using System.Net.Http.Headers;
using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Identity;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.Infrastructure.Storage;
using LegalManager.Infrastructure.Escavador;
using LegalManager.Infrastructure.Tribunais;
using LegalManager.Infrastructure.Tribunais.Dje;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Resend;
using Serilog;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

Serilog.Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddIdentity<Usuario, IdentityRole<Guid>>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 8;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<AppDbContext>()
.AddErrorDescriber<LegalManager.Infrastructure.Identity.PortugueseIdentityErrorDescriber>()
.AddDefaultTokenProviders();

var jwtSettings = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!))
    };
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IContatoService, ContatoService>();
builder.Services.AddScoped<IProcessoService, ProcessoService>();
builder.Services.AddScoped<ITarefaService, TarefaService>();
builder.Services.AddScoped<IEventoService, EventoService>();
builder.Services.AddScoped<INotificacaoService, NotificacaoService>();
builder.Services.AddScoped<IMonitoramentoService, MonitoramentoService>();
builder.Services.AddScoped<IPublicacaoService, PublicacaoService>();
builder.Services.AddScoped<IPortalClienteService, PortalClienteService>();
builder.Services.AddScoped<IFinanceiroService, FinanceiroService>();
builder.Services.AddScoped<IIndicadoresService, IndicadoresService>();
builder.Services.AddScoped<ITimesheetService, TimesheetService>();
builder.Services.AddScoped<IPreferenciasNotificacaoService, PreferenciasNotificacaoService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddSingleton<IStorageService, OciStorageService>();
builder.Services.AddScoped<IDocumentoService, DocumentoService>();
builder.Services.AddScoped<IPastaService, PastaService>();
builder.Services.AddScoped<IModeloDocumentoService, ModeloDocumentoService>();
builder.Services.AddScoped<IPasswordHasher<LegalManager.Domain.Entities.AcessoCliente>,
    PasswordHasher<LegalManager.Domain.Entities.AcessoCliente>>();
builder.Services.AddScoped<AlertasJob>();
builder.Services.AddScoped<TrialRevertJob>();
builder.Services.AddScoped<MonitoramentoJob>();
builder.Services.AddScoped<EscavadorMovimentacoesPollingJob>();
builder.Services.AddScoped<EscavadorOabSyncJob>();
builder.Services.AddScoped<PublicacaoClassificacaoService>();
builder.Services.AddScoped<PublicacaoMapper>();
builder.Services.AddScoped<ITenantOabService, TenantOabService>();
builder.Services.AddScoped<EscavadorTierManagementJob>();

builder.Services.AddHttpClient<IIAService, IAService>(client =>
{
    var apiKey = builder.Configuration["IA:ApiKey"]
              ?? builder.Configuration["IA_API_KEY"]
              ?? builder.Configuration["IA:API_KEY"]
              ?? throw new InvalidOperationException("IA:ApiKey não configurado");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
});
builder.Services.AddScoped<ICreditoService, CreditoService>();
builder.Services.AddScoped<ITraducaoService, TraducaoService>();
builder.Services.AddScoped<IPecaJuridicaService, PecaJuridicaService>();
builder.Services.AddScoped<IResumoProcessoService, ResumoProcessoService>();
builder.Services.AddScoped<SeedService>();
builder.Services.AddScoped<IHonorarioService, HonorarioService>();
builder.Services.AddScoped<IConfiguracaoHonorarioService, ConfiguracaoHonorarioService>();

if (builder.Configuration.GetValue<bool>("Escavador:UseMock"))
{
    builder.Services.AddScoped<IEscavadorService, LegalManager.Infrastructure.Escavador.EscavadorMockClient>();
}
else
{
    builder.Services.AddHttpClient<IEscavadorService, LegalManager.Infrastructure.Escavador.EscavadorHttpClient>(client =>
    {
        var baseUrl = builder.Configuration["Escavador:BaseUrl"] ?? "https://api.escavador.com";
        client.BaseAddress = new Uri(baseUrl);
        var token = builder.Configuration["Escavador:ApiToken"] ?? "";
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        CheckCertificateRevocationList = false
    });
}

builder.Services.AddHttpClient<DataJudAdapter>(client =>
{
    var baseUrl = builder.Configuration["DataJud:BaseUrl"] ?? "https://api-publica.datajud.cnj.jus.br";
    client.BaseAddress = new Uri(baseUrl);
    var apiKey = builder.Configuration["DataJud:ApiKey"] ?? "";
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("Authorization", $"APIKey {apiKey}");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<TjspDjeAdapter>(client =>
{
    client.BaseAddress = new Uri("https://esaj.tjsp.jus.br");
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient<EsajTjspProcessosAdapter>(client =>
{
    client.BaseAddress = new Uri("https://esaj.tjsp.jus.br");
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<TjrjDjeAdapter>(client =>
{
    client.BaseAddress = new Uri("https://www.tjrj.jus.br");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient<TjmgDjeAdapter>(client =>
{
    client.BaseAddress = new Uri("https://www.tjmg.jus.br");
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddScoped<IDjeAdapter>(sp =>
    new JusBrasilDjeAdapter(
        sp.GetRequiredService<ILogger<JusBrasilDjeAdapter>>()));
builder.Services.AddScoped<IDjeAdapter>(sp =>
    new TjspDjeAdapter(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TjspDjeAdapter)),
        sp.GetRequiredService<ILogger<TjspDjeAdapter>>()));
builder.Services.AddScoped<IDjeAdapter>(sp =>
    new TjrjDjeAdapter(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TjrjDjeAdapter)),
        sp.GetRequiredService<ILogger<TjrjDjeAdapter>>()));
builder.Services.AddScoped<IDjeAdapter>(sp =>
    new TjmgDjeAdapter(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TjmgDjeAdapter)),
        sp.GetRequiredService<ILogger<TjmgDjeAdapter>>()));

builder.Services.AddScoped<DjeJob>();

builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
    o.ApiToken = builder.Configuration["Resend:ApiToken"] ?? "");
builder.Services.AddTransient<IResend, ResendClient>();
builder.Services.AddTransient<IEmailService, EmailService>();

builder.Services.AddHttpClient("Anthropic", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com");
    var apiKey = builder.Configuration["Anthropic:ApiKey"] ?? "";
    client.DefaultRequestHeaders.Add("x-api-key", apiKey);
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<IAbacatePayService, AbacatePayService>(client =>
{
    var baseUrl = builder.Configuration["AbacatePay:BaseUrl"] ?? "https://api.abacatepay.com/v1";
    if (!baseUrl.EndsWith('/')) baseUrl += '/';
    client.BaseAddress = new Uri(baseUrl);
    var apiKey = builder.Configuration["AbacatePay:ApiKey"] ?? "";
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
});

builder.Services.AddHttpClient("BCB", c =>
{
    c.BaseAddress = new Uri("https://api.bcb.gov.br/");
    c.DefaultRequestHeaders.Add("Accept", "application/json");
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("TJSP", c =>
{
    c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 LegalManager/1.0");
    c.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration["App:FrontendUrl"] ?? "http://localhost:5000")
              .AllowAnyHeader()
              .AllowAnyMethod()));

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    await db.Database.ExecuteSqlRawAsync("""
        UPDATE "Notificacoes"
        SET "Url" = '/pages/tarefas.html?abrirId=' || SUBSTRING("ChaveDedup" FROM 8 FOR 36)
        WHERE "Tipo" = 0
          AND "Url" = '/pages/tarefas.html'
          AND "ChaveDedup" LIKE 'tarefa-%'
          AND LENGTH("ChaveDedup") >= 43
        """);

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var role in new[] { "Admin", "Advogado", "Colaborador", "Cliente", "SuperAdmin" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }

    await SeedSuperAdminAsync(scope.ServiceProvider, builder.Configuration);

    // Enfileira job se algum dos 3 índices estiver ausente
    var tiposPresentes = await db.IndicesCorrecaoMonetaria
        .Select(i => i.Tipo).Distinct().CountAsync();
    if (tiposPresentes < 3)
    {
        var jobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
        jobClient.Enqueue<IndicesCorrecaoJob>(job => job.ExecutarAsync());
    }
}

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-XSS-Protection"] = "0";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://www.googletagmanager.com https://www.google-analytics.com https://client.crisp.chat https://www.clarity.ms https://*.clarity.ms https://static.cloudflareinsights.com https://connect.facebook.net; " +
        "style-src 'self' 'unsafe-inline' https://client.crisp.chat; " +
        "img-src 'self' data: https:; " +
        "font-src 'self' data: https://client.crisp.chat; " +
        "connect-src 'self' https: wss:; " +
        "frame-ancestors 'none'; " +
        "frame-src 'self' blob: https://www.facebook.com; " +
        "base-uri 'self'; " +
        "object-src 'none';";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value;
    if (!string.IsNullOrEmpty(path) && path.EndsWith('/') && path != "/")
    {
        var wwwroot = ctx.RequestServices.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>()?.ContentRootPath;
        if (!string.IsNullOrEmpty(wwwroot))
        {
            var indexPath = System.IO.Path.Combine(wwwroot, "wwwroot", path.TrimStart('/'), "index.html");
            if (System.IO.File.Exists(indexPath))
            {
                ctx.Response.Redirect(path + "index.html", permanent: false);
                return;
            }
        }
    }
    await next();
});

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path == "/cliente" || ctx.Request.Path == "/cliente/")
    {
        ctx.Response.Redirect("/cliente/index.html");
        return;
    }
    await next();
});

RecurringJob.AddOrUpdate<AlertasJob>(
    "alertas-diarios",
    job => job.ExecutarAsync(),
    "0 */3 * * *"); // every 3 hours

RecurringJob.AddOrUpdate<TrialRevertJob>(
    "trial-revert",
    job => job.ExecutarAsync(),
    "0 * * * *"); // hourly catch-up for expired trials whose users haven't logged in

RecurringJob.AddOrUpdate<MonitoramentoJob>(
    "monitoramento-processos",
    job => job.ExecutarAsync(),
    "0 6 * * *"); // daily at 06:00 UTC (03:00 Brasília)

RecurringJob.AddOrUpdate<DjeJob>(
    "captura-dje",
    job => job.ExecutarAsync(CancellationToken.None),
    "0 9 * * *"); // daily at 09:00 UTC (06:00 Brasília) — após publicação dos diários

RecurringJob.AddOrUpdate<IndicesCorrecaoJob>(
    "indices-correcao-mensal",
    job => job.ExecutarAsync(),
    "0 6 15 * *"); // dia 15 de cada mês às 06:00 UTC — IPCA e IGP-M já publicados

// Escavador: modo configurável (Webhook | Polling | Hybrid).
// Webhook é primário; polling é backstop para Hybrid/Polling.
var escavadorModo = builder.Configuration["Escavador:ModoCaptura"] ?? "Hybrid";
if (escavadorModo is "Polling" or "Hybrid")
{
    var cron = builder.Configuration["Escavador:PollingCron"] ?? "0 6 * * *";
    RecurringJob.AddOrUpdate<EscavadorMovimentacoesPollingJob>(
        "escavador-movimentacoes-polling",
        job => job.ExecutarAsync(),
        cron);
}

// Classificação IA assíncrona: a cada 5 min enriquece Publicacoes Escavador recém-criadas
RecurringJob.AddOrUpdate<PublicacaoClassificacaoService>(
    "publicacao-classificacao-ia",
    job => job.ExecutarAsync(),
    "*/5 * * * *");

// Sync de OABs com Escavador (retry de monitoramentos não criados)
if (builder.Configuration.GetValue<bool>("Escavador:OabSyncEnabled", true))
{
    var oabCron = builder.Configuration["Escavador:OabSyncCron"] ?? "0 5 * * *";
    RecurringJob.AddOrUpdate<EscavadorOabSyncJob>(
        "escavador-oab-sync",
        job => job.ExecutarAsync(),
        oabCron);
}

RecurringJob.AddOrUpdate<EscavadorTierManagementJob>(
    "escavador-tier-semanal",
    job => job.AplicarTierSemanalAsync(),
    "0 7 1 * *"); // dia 1 de cada mês às 07:00 — Estratégia 5 (downgrade para semanal após 180 dias)
// Para ativar a Estratégia 4 (cancelar) em vez da 5, substituir por: job => job.SuspenderMonitoramentosAsync()

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

static async Task SeedSuperAdminAsync(IServiceProvider services, IConfiguration config)
{
    var systemTenantId = new Guid("00000000-0000-0000-0000-000000000001");
    var email = config["SuperAdmin:Email"] ?? "superadmin@causify.internal";
    var password = config["SuperAdmin:Password"] ?? "ChangeMeNow!1";

    var db = services.GetRequiredService<AppDbContext>();
    var userManager = services.GetRequiredService<UserManager<Usuario>>();

    if (!await db.Tenants.AnyAsync(t => t.Id == systemTenantId))
    {
        db.Tenants.Add(new Tenant
        {
            Id = systemTenantId,
            Nome = "Sistema",
            Plano = LegalManager.Domain.Enums.PlanoTipo.Enterprise,
            Status = LegalManager.Domain.Enums.StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    if (await userManager.FindByEmailAsync(email) == null)
    {
        var admin = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = systemTenantId,
            Nome = "Super Admin",
            Email = email,
            UserName = email,
            Perfil = LegalManager.Domain.Enums.PerfilUsuario.SuperAdmin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        var result = await userManager.CreateAsync(admin, password);
        if (result.Succeeded)
            await userManager.AddToRoleAsync(admin, "SuperAdmin");
    }
}
