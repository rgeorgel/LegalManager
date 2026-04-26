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
using LegalManager.Infrastructure.Tribunais;
using LegalManager.Infrastructure.Tribunais.Dje;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
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
    options.Password.RequiredLength = 8;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<AppDbContext>()
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
builder.Services.AddScoped<IPrazoService, PrazoService>();
builder.Services.AddScoped<IPublicacaoService, PublicacaoService>();
builder.Services.AddScoped<INomeCapturaService, NomeCapturaService>();
builder.Services.AddScoped<IPortalClienteService, PortalClienteService>();
builder.Services.AddScoped<IFinanceiroService, FinanceiroService>();
builder.Services.AddScoped<IIndicadoresService, IndicadoresService>();
builder.Services.AddScoped<ITimesheetService, TimesheetService>();
builder.Services.AddScoped<IPreferenciasNotificacaoService, PreferenciasNotificacaoService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddSingleton<IStorageService, OciStorageService>();
builder.Services.AddScoped<IDocumentoService, DocumentoService>();
builder.Services.AddScoped<IPasswordHasher<LegalManager.Domain.Entities.AcessoCliente>,
    PasswordHasher<LegalManager.Domain.Entities.AcessoCliente>>();
builder.Services.AddScoped<AlertasJob>();
builder.Services.AddScoped<MonitoramentoJob>();
builder.Services.AddScoped<CapturaPublicacaoJob>();

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
builder.Services.AddScoped<SeedService>();

builder.Services.AddHttpClient<DataJudAdapter>(client =>
{
    client.BaseAddress = new Uri("https://api.datajud.cnj.jus.br");
    var apiKey = builder.Configuration["DataJud:ApiKey"] ?? "cDZHYzlZa0JadVREZDJCendOM3Yw";
    client.DefaultRequestHeaders.Add("Authorization", $"APIKey {apiKey}");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<TjspDjeAdapter>(client =>
{
    client.BaseAddress = new Uri("https://dje.tjsp.jus.br");
    client.Timeout = TimeSpan.FromSeconds(60);
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var role in new[] { "Admin", "Advogado", "Colaborador", "Cliente" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }
}

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire");

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

RecurringJob.AddOrUpdate<MonitoramentoJob>(
    "monitoramento-processos",
    job => job.ExecutarAsync(),
    "0 6 * * *"); // daily at 06:00 UTC (03:00 Brasília)

RecurringJob.AddOrUpdate<CapturaPublicacaoJob>(
    "captura-publicacoes",
    job => job.ExecutarAsync(),
    "0 7 * * *"); // daily at 07:00 UTC, after MonitoramentoJob

RecurringJob.AddOrUpdate<DjeJob>(
    "captura-dje",
    job => job.ExecutarAsync(),
    "0 9 * * *"); // daily at 09:00 UTC (06:00 Brasília) — após publicação dos diários

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
