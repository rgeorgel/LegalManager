using LegalManager.Application.DTOs.Honorarios;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class ConfiguracaoHonorarioServiceTests
{
    private static (AppDbContext db, ConfiguracaoHonorarioService service, Guid tenantId) Criar()
    {
        var tenantId = Guid.NewGuid();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"cfg-honorario-tests-{Guid.NewGuid()}")
            .Options;
        var db = new AppDbContext(opts);
        db.Tenants.Add(new Tenant { Id = tenantId, Nome = "Escritório", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        db.SaveChanges();

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (db, new ConfiguracaoHonorarioService(db, audit.Object), tenantId);
    }

    [Fact]
    public async Task ObterOuCriarPadrao_PrimeiraVez_RetornaDefaults()
    {
        var (db, service, tenantId) = Criar();
        var dto = await service.ObterOuCriarPadraoAsync(tenantId);
        Assert.Equal(0m, dto.MetaMensalPadrao);
        Assert.Equal(0.02m, dto.PercentualMultaDefault);
        Assert.Equal(0.015m, dto.PercentualJurosMensalDefault);
        Assert.Equal(3, dto.DiasAvisoVencimento);
    }

    [Fact]
    public async Task Salvar_PersisteValores()
    {
        var (db, service, tenantId) = Criar();
        var dto = await service.SalvarAsync(tenantId, new ConfiguracaoHonorarioDto(
            "Escritório X", "Dr. Teste", "OAB/SP 123.456",
            "Rua A, 100", "(11) 99999-1111", "contato@x.com",
            null, 15000m, 0.03m, 0.02m, 5
        ));
        Assert.Equal("Escritório X", dto.NomeEscritorio);
        Assert.Equal(15000m, dto.MetaMensalPadrao);
        Assert.Equal(0.03m, dto.PercentualMultaDefault);
        Assert.Single(db.ConfiguracoesHonorarios);
    }

    [Fact]
    public async Task Salvar_MultaForaDoRange_LancaExcecao()
    {
        var (db, service, tenantId) = Criar();
        await Assert.ThrowsAsync<ArgumentException>(() => service.SalvarAsync(tenantId, new ConfiguracaoHonorarioDto(
            null, null, null, null, null, null, null, 0, 1.5m, 0.01m, 3
        )));
    }

    [Fact]
    public async Task Salvar_JurosForaDoRange_LancaExcecao()
    {
        var (db, service, tenantId) = Criar();
        await Assert.ThrowsAsync<ArgumentException>(() => service.SalvarAsync(tenantId, new ConfiguracaoHonorarioDto(
            null, null, null, null, null, null, null, 0, 0.02m, 1.5m, 3
        )));
    }

    [Fact]
    public async Task Salvar_MetaNegativa_LancaExcecao()
    {
        var (db, service, tenantId) = Criar();
        await Assert.ThrowsAsync<ArgumentException>(() => service.SalvarAsync(tenantId, new ConfiguracaoHonorarioDto(
            null, null, null, null, null, null, null, -1, 0.02m, 0.015m, 3
        )));
    }
}
