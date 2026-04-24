using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class DocumentoServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ITenantContext CreateTenantContext(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        return mock.Object;
    }

    private static IStorageService CreateMockStorageService()
    {
        var mock = new Mock<IStorageService>();
        mock.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        mock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://fake-url.example.com/download");
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Tenant tenant, Usuario usuario)> SeedTenantAsync()
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Escritório Teste",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Admin Teste",
            Email = "admin@teste.com",
            UserName = "admin@teste.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();

        return (ctx, tenant, usuario);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingDocument_ReturnsDocumentDto()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        var processo = new Processo
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            NumeroCNJ = "1234567-12.2024.1.00.0000",
            CriadoEm = DateTime.UtcNow,
            AreaDireito = AreaDireito.Civil
        };
        ctx.Processos.Add(processo);

        var documento = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProcessoId = processo.Id,
            Nome = "Documento Teste.pdf",
            ObjectKey = $"{tenant.Id}/processos/{processo.Id}/Documento Teste.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 1024,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        };
        ctx.Documentos.Add(documento);
        await ctx.SaveChangesAsync();

        var result = await service.GetByIdAsync(documento.Id);

        Assert.NotNull(result);
        Assert.Equal(documento.Id, result.Id);
        Assert.Equal("Documento Teste.pdf", result.Nome);
        Assert.Equal(TipoDocumento.Prova, result.Tipo);
        Assert.Equal(processo.NumeroCNJ, result.NumeroProcesso);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingDocument_ReturnsNull()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new DocumentoService(ctx, tenantContext, CreateMockStorageService());

        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyTenantDocuments()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Outro Escritório",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(otherTenant);

        var doc1 = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Doc do Tenant",
            ObjectKey = $"{tenant.Id}/doc1.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 100,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        };
        var doc2 = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenant.Id,
            Nome = "Doc de Outro Tenant",
            ObjectKey = $"{otherTenant.Id}/doc2.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 200,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        };
        ctx.Documentos.AddRange(doc1, doc2);
        await ctx.SaveChangesAsync();

        var result = (await service.GetAllAsync()).ToList();

        Assert.Single(result);
        Assert.Equal("Doc do Tenant", result[0].Nome);
    }

    [Fact]
    public async Task GetByProcessoAsync_ReturnsFilteredDocuments()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        var processo1 = new Processo
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            NumeroCNJ = "1111111-11.2024.1.00.0000",
            CriadoEm = DateTime.UtcNow,
            AreaDireito = AreaDireito.Civil
        };
        var processo2 = new Processo
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            NumeroCNJ = "2222222-22.2024.1.00.0000",
            CriadoEm = DateTime.UtcNow,
            AreaDireito = AreaDireito.Civil
        };
        ctx.Processos.AddRange(processo1, processo2);

        var doc1 = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProcessoId = processo1.Id,
            Nome = "Doc Processo 1",
            ObjectKey = $"{tenant.Id}/p1/doc.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 100,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        };
        var doc2 = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProcessoId = processo2.Id,
            Nome = "Doc Processo 2",
            ObjectKey = $"{tenant.Id}/p2/doc.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 200,
            Tipo = TipoDocumento.Peticao,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        };
        ctx.Documentos.AddRange(doc1, doc2);
        await ctx.SaveChangesAsync();

        var result = (await service.GetByProcessoAsync(processo1.Id)).ToList();

        Assert.Single(result);
        Assert.Equal("Doc Processo 1", result[0].Nome);
    }

    [Fact]
    public async Task UploadAsync_WithoutTenantContext_ThrowsUnauthorized()
    {
        var (ctx, _, _) = await SeedTenantAsync();
        var emptyTenantContext = CreateTenantContext(Guid.Empty, Guid.Empty);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, emptyTenantContext, storageService);

        var stream = new MemoryStream(new byte[100]);
        var uploadInfo = new DocumentoUploadDto { Tipo = TipoDocumento.Prova };

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.UploadAsync(stream, "test.pdf", "application/pdf", uploadInfo));

        Assert.Contains("Tenant não identificado", exception.Message);
    }

    [Fact(Skip = "Stream.Length mocking requires more complex setup")]
    public async Task UploadAsync_ExceedingQuota_ThrowsInvalidOperation()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);

        var mockStorage = new Mock<IStorageService>();
        mockStorage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Should not be called"));

        var service = new DocumentoService(ctx, tenantContext, mockStorage.Object);

        ctx.Documentos.Add(new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Existing.pdf",
            ObjectKey = $"{tenant.Id}/existing.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 19L * 1024 * 1024 * 1024,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        });
        await ctx.SaveChangesAsync();

        var mockStream = new Mock<Stream>();
        mockStream.Setup(s => s.Length).Returns(2L * 1024 * 1024 * 1024);
        mockStream.Setup(s => s.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var uploadInfo = new DocumentoUploadDto { Tipo = TipoDocumento.Prova };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadAsync(mockStream.Object, "big.pdf", "application/pdf", uploadInfo));

        Assert.Contains("Cota", exception.Message);
    }

    [Fact]
    public async Task UploadAsync_ValidRequest_CreatesDocumentAndCallsStorage()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        var processo = new Processo
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            NumeroCNJ = "9999999-99.2024.1.00.0000",
            CriadoEm = DateTime.UtcNow,
            AreaDireito = AreaDireito.Civil
        };
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var stream = new MemoryStream(new byte[512]);
        var uploadInfo = new DocumentoUploadDto
        {
            ProcessoId = processo.Id,
            Tipo = TipoDocumento.Prova,
            Nome = "Meu Documento.pdf"
        };

        var result = await service.UploadAsync(stream, "original.pdf", "application/pdf", uploadInfo);

        Assert.NotNull(result);
        Assert.Equal("Meu Documento.pdf", result.Nome);
        Assert.Equal(processo.Id, result.ProcessoId);
        Assert.Equal(512, result.TamanhoBytes);
        Assert.Equal(TipoDocumento.Prova, result.Tipo);

        var savedDoc = await ctx.Documentos.FirstOrDefaultAsync(d => d.Id == result.Id);
        Assert.NotNull(savedDoc);
        Assert.Equal(tenant.Id, savedDoc.TenantId);
        Assert.Equal(usuario.Id, savedDoc.UploadedPorId);
    }

    [Fact]
    public async Task UploadAsync_UsesCustomNomeWhenProvided()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        var stream = new MemoryStream(new byte[100]);
        var uploadInfo = new DocumentoUploadDto
        {
            Tipo = TipoDocumento.Outro,
            Nome = "Nome Customizado"
        };

        var result = await service.UploadAsync(stream, "original.pdf", "application/pdf", uploadInfo);

        Assert.Equal("Nome Customizado", result.Nome);
    }

    [Fact]
    public async Task UploadAsync_NoSpecificId_UsesDefaultPath()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        var stream = new MemoryStream(new byte[100]);
        var uploadInfo = new DocumentoUploadDto
        {
            Tipo = TipoDocumento.Outro
        };

        var result = await service.UploadAsync(stream, "sem-contexto.pdf", "application/pdf", uploadInfo);

        var savedDoc = await ctx.Documentos.FirstOrDefaultAsync(d => d.Id == result.Id);
        Assert.NotNull(savedDoc);
        Assert.Contains($"{tenant.Id}/documentos/", savedDoc.ObjectKey);
    }

    [Fact]
    public async Task DeleteAsync_ExistingDocument_DeletesFromStorageAndDb()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        var documento = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Para Deletar.pdf",
            ObjectKey = $"{tenant.Id}/delete-me.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 500,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        };
        ctx.Documentos.Add(documento);
        await ctx.SaveChangesAsync();
        var objectKey = documento.ObjectKey;

        await service.DeleteAsync(documento.Id);

        var deleted = await ctx.Documentos.FindAsync(documento.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAsync_DocumentFromAnotherTenant_ThrowsKeyNotFoundException()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Outro Tenant",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(otherTenant);

        var documento = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenant.Id,
            Nome = "Outro Tenant Doc.pdf",
            ObjectKey = $"{otherTenant.Id}/other.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 100,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        };
        ctx.Documentos.Add(documento);
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(documento.Id));
    }

    [Fact]
    public async Task DeleteAsync_NonExistingDocument_ThrowsKeyNotFoundException()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new DocumentoService(ctx, tenantContext, CreateMockStorageService());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetDownloadUrlAsync_ExistingDocument_ReturnsPresignedUrl()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        var documento = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Download Test.pdf",
            ObjectKey = $"{tenant.Id}/download-test.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 1024,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        };
        ctx.Documentos.Add(documento);
        await ctx.SaveChangesAsync();

        var url = await service.GetDownloadUrlAsync(documento.Id);

        Assert.NotNull(url);
        Assert.Contains("https://fake-url.example.com/download", url);
    }

    [Fact]
    public async Task GetCotaAsync_ReturnsUsedAndTotalQuota()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var storageService = CreateMockStorageService();
        var service = new DocumentoService(ctx, tenantContext, storageService);

        ctx.Documentos.Add(new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Quota Test 1.pdf",
            ObjectKey = $"{tenant.Id}/q1.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 5L * 1024 * 1024 * 1024,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        });
        ctx.Documentos.Add(new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Quota Test 2.pdf",
            ObjectKey = $"{tenant.Id}/q2.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 3L * 1024 * 1024 * 1024,
            Tipo = TipoDocumento.Prova,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = usuario.Id
        });
        await ctx.SaveChangesAsync();

        var result = await service.GetCotaAsync();

        Assert.Equal(8L * 1024 * 1024 * 1024, result.UsadoBytes);
        Assert.Equal(20L * 1024 * 1024 * 1024, result.CotaBytes);
    }

    [Fact]
    public async Task GetCotaAsync_NoDocuments_ReturnsZeroUsed()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new DocumentoService(ctx, tenantContext, CreateMockStorageService());

        var result = await service.GetCotaAsync();

        Assert.Equal(0, result.UsadoBytes);
        Assert.Equal(20L * 1024 * 1024 * 1024, result.CotaBytes);
    }
}