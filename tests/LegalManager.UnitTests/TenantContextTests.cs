using System.Security.Claims;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Moq;

namespace LegalManager.UnitTests;

public class TenantContextTests
{
    private static Mock<IHttpContextAccessor> CreateHttpContextAccessorMock(
        Guid? tenantId = null,
        Guid? userId = null,
        string? role = null,
        string? plano = null)
    {
        var claims = new List<Claim>();

        if (tenantId.HasValue)
            claims.Add(new Claim("tenantId", tenantId.Value.ToString()));
        if (userId.HasValue)
            claims.Add(new Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.Value.ToString()));
        if (role != null)
            claims.Add(new Claim(System.Security.Claims.ClaimTypes.Role, role));
        if (plano != null)
            claims.Add(new Claim("plano", plano));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var mock = new Mock<IHttpContextAccessor>();
        mock.Setup(h => h.HttpContext).Returns(httpContext);
        return mock;
    }

    [Fact]
    public void TenantId_ReturnsCorrectGuid_WhenClaimPresent()
    {
        var expectedTenantId = Guid.NewGuid();
        var accessor = CreateHttpContextAccessorMock(tenantId: expectedTenantId);
        var context = new TenantContext(accessor.Object);
        Assert.Equal(expectedTenantId, context.TenantId);
    }

    [Fact]
    public void TenantId_ReturnsEmptyGuid_WhenClaimMissing()
    {
        var accessor = CreateHttpContextAccessorMock();
        var context = new TenantContext(accessor.Object);
        Assert.Equal(Guid.Empty, context.TenantId);
    }

    [Fact]
    public void UserId_ReturnsCorrectGuid_WhenClaimPresent()
    {
        var expectedUserId = Guid.NewGuid();
        var accessor = CreateHttpContextAccessorMock(userId: expectedUserId);
        var context = new TenantContext(accessor.Object);
        Assert.Equal(expectedUserId, context.UserId);
    }

    [Fact]
    public void UserId_ReturnsEmptyGuid_WhenClaimMissing()
    {
        var accessor = CreateHttpContextAccessorMock();
        var context = new TenantContext(accessor.Object);
        Assert.Equal(Guid.Empty, context.UserId);
    }

    [Fact]
    public void UserRole_ReturnsCorrectRole_WhenClaimPresent()
    {
        var accessor = CreateHttpContextAccessorMock(role: "Advogado");
        var context = new TenantContext(accessor.Object);
        Assert.Equal("Advogado", context.UserRole);
    }

    [Fact]
    public void UserRole_ReturnsEmptyString_WhenClaimMissing()
    {
        var accessor = CreateHttpContextAccessorMock();
        var context = new TenantContext(accessor.Object);
        Assert.Equal(string.Empty, context.UserRole);
    }

    [Theory]
    [InlineData("Free", PlanoTipo.Free)]
    [InlineData("Pro", PlanoTipo.Pro)]
    [InlineData("Enterprise", PlanoTipo.Enterprise)]
    public void Plano_ReturnsCorrectEnum_WhenValidClaim(string planoValue, PlanoTipo expected)
    {
        var accessor = CreateHttpContextAccessorMock(plano: planoValue);
        var context = new TenantContext(accessor.Object);
        Assert.Equal(expected, context.Plano);
    }

    [Fact]
    public void Plano_ReturnsFree_WhenPlanoClaimMissing()
    {
        var accessor = CreateHttpContextAccessorMock();
        var context = new TenantContext(accessor.Object);
        Assert.Equal(PlanoTipo.Free, context.Plano);
    }

    [Fact]
    public void Plano_ReturnsFree_WhenPlanoClaimInvalid()
    {
        var accessor = CreateHttpContextAccessorMock(plano: "InvalidPlan");
        var context = new TenantContext(accessor.Object);
        Assert.Equal(PlanoTipo.Free, context.Plano);
    }

    [Fact]
    public void Constructor_HandlesNullHttpContext()
    {
        var mock = new Mock<IHttpContextAccessor>();
        mock.Setup(h => h.HttpContext).Returns((HttpContext?)null);
        var context = new TenantContext(mock.Object);
        Assert.Equal(Guid.Empty, context.TenantId);
        Assert.Equal(Guid.Empty, context.UserId);
        Assert.Equal(string.Empty, context.UserRole);
        Assert.Equal(PlanoTipo.Free, context.Plano);
    }

    [Fact]
    public void AllProperties_ReturnCorrectValues_WhenAllClaimsPresent()
    {
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var accessor = CreateHttpContextAccessorMock(
            tenantId: expectedTenantId,
            userId: expectedUserId,
            role: "Admin",
            plano: "Pro");

        var context = new TenantContext(accessor.Object);

        Assert.Equal(expectedTenantId, context.TenantId);
        Assert.Equal(expectedUserId, context.UserId);
        Assert.Equal("Admin", context.UserRole);
        Assert.Equal(PlanoTipo.Pro, context.Plano);
    }
}

public class PlanoRestricoesTests
{
    [Theory]
    [InlineData(PlanoTipo.Free, 1)]
    [InlineData(PlanoTipo.Plus, 1)]
    [InlineData(PlanoTipo.Pro, 2)]
    [InlineData(PlanoTipo.Enterprise, 5)]
    public void MaxUsuarios_ReturnsCorrectValue(PlanoTipo plano, int expected)
    {
        Assert.Equal(expected, PlanoRestricoes.MaxUsuarios(plano));
    }

    [Theory]
    [InlineData(PlanoTipo.Free, 20)]
    [InlineData(PlanoTipo.Plus, 20)]
    [InlineData(PlanoTipo.Pro, 100)]
    [InlineData(PlanoTipo.Max, 250)]
    [InlineData(PlanoTipo.Enterprise, 500)]
    public void MaxProcessosMonitorados_ReturnsCorrectValue(PlanoTipo plano, int expected)
    {
        Assert.Equal(expected, PlanoRestricoes.MaxProcessosMonitorados(plano));
    }

    [Theory]
    [InlineData(PlanoTipo.Free, 1024)]
    [InlineData(PlanoTipo.Plus, 2 * 1024)]
    [InlineData(PlanoTipo.Pro, 5 * 1024)]
    [InlineData(PlanoTipo.Max, 10 * 1024)]
    [InlineData(PlanoTipo.Enterprise, 20 * 1024)]
    public void ArmazenamentoLimiteMB_ReturnsCorrectValue(PlanoTipo plano, int expected)
    {
        Assert.Equal(expected, PlanoRestricoes.ArmazenamentoLimiteMB(plano));
    }

    [Theory]
    [InlineData(PlanoTipo.Free, false)]
    [InlineData(PlanoTipo.Pro, true)]
    [InlineData(PlanoTipo.Enterprise, true)]
    public void PermiteFinanceiro_ReturnsCorrectValue(PlanoTipo plano, bool expected)
    {
        Assert.Equal(expected, PlanoRestricoes.PermiteFinanceiro(plano));
    }

    [Theory]
    [InlineData(PlanoTipo.Free, false)]
    [InlineData(PlanoTipo.Pro, true)]
    [InlineData(PlanoTipo.Enterprise, true)]
    public void PermiteIndicadores_ReturnsCorrectValue(PlanoTipo plano, bool expected)
    {
        Assert.Equal(expected, PlanoRestricoes.PermiteIndicadores(plano));
    }

    [Theory]
    [InlineData(PlanoTipo.Free, false)]
    [InlineData(PlanoTipo.Pro, true)]
    [InlineData(PlanoTipo.Enterprise, true)]
    public void PermiteCalculadoraPrazos_ReturnsCorrectValue(PlanoTipo plano, bool expected)
    {
        Assert.Equal(expected, PlanoRestricoes.PermiteCalculadoraPrazos(plano));
    }

    [Theory]
    [InlineData(PlanoTipo.Free, false)]
    [InlineData(PlanoTipo.Pro, true)]
    [InlineData(PlanoTipo.Enterprise, true)]
    public void PermitePortalCliente_ReturnsCorrectValue(PlanoTipo plano, bool expected)
    {
        Assert.Equal(expected, PlanoRestricoes.PermitePortalCliente(plano));
    }
}