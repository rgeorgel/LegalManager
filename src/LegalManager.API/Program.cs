using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Identity;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Resend;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Serilog.Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseNpgsql(connectionString));

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

builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
    o.ApiToken = builder.Configuration["Resend:ApiToken"] ?? "");
builder.Services.AddTransient<IResend, ResendClient>();
builder.Services.AddTransient<IEmailService, EmailService>();

builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddControllers();
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
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
