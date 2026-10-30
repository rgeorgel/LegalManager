using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Honorarios;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LegalManager.UnitTests;

public class PortalClienteHonorariosControllerTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _contatoId = Guid.NewGuid();
    private readonly Guid _contratoId = Guid.NewGuid();
    private readonly DateTime _hoje = DateTime.UtcNow.Date;

    public PortalClienteHonorariosControllerTests()
    {
        _db.Tenants.Add(new Tenant { Id = _tenantId, Nome = "Escritório" });
        _db.Contatos.Add(new Contato { Id = _contatoId, TenantId = _tenantId, Nome = "Maria", CpfCnpj = "123" });

        var contrato = new ContratoHonorario
        {
            Id = _contratoId, TenantId = _tenantId, ContatoId = _contatoId, NumeroContrato = "HON-1", ValorTotal = 3000,
            FormaPagamento = FormaPagamentoContrato.Parcelado, Status = StatusContratoHonorario.Ativo, DataInicio = _hoje.AddMonths(-2)
        };
        contrato.Parcelas.Add(new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContratoId = _contratoId, Numero = 1, Vencimento = _hoje.AddMonths(-2), ValorOriginal = 1000, ValorPago = 1000, Status = StatusParcelaHonorario.Pago });
        contrato.Parcelas.Add(new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContratoId = _contratoId, Numero = 2, Vencimento = _hoje.AddDays(-60), ValorOriginal = 1000, Status = StatusParcelaHonorario.Vencido });
        contrato.Parcelas.Add(new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContratoId = _contratoId, Numero = 3, Vencimento = _hoje.AddMonths(1), ValorOriginal = 1000, Status = StatusParcelaHonorario.Pendente });
        contrato.Parcelas.Add(new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContratoId = _contratoId, Numero = 4, Vencimento = _hoje.AddDays(-5), ValorOriginal = 500, Status = StatusParcelaHonorario.Cancelado });

        _db.ContratosHonorarios.AddRange(contrato,
            new ContratoHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContatoId = _contatoId, NumeroContrato = "HON-DIST", Status = StatusContratoHonorario.Distratado, DataInicio = _hoje },
            new ContratoHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContatoId = Guid.NewGuid(), NumeroContrato = "HON-OUTRO", Status = StatusContratoHonorario.Ativo, DataInicio = _hoje });
        _db.SaveChanges();
    }

    private PortalClienteHonorariosController Controller(Guid? contatoId = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHonorarioService>(new HonorarioService(_db, Mock.Of<IAuditService>()))
            .AddSingleton(Mock.Of<IWebHostEnvironment>(e => e.WebRootPath == "/inexistente"))
            .BuildServiceProvider();
        return new PortalClienteHonorariosController(_db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("contatoId", (contatoId ?? _contatoId).ToString()),
                        new Claim("tenantId", _tenantId.ToString())
                    ], "test"))
                }
            }
        };
    }

    [Fact]
    public async Task MeusContratos_SoAtivosDoCliente_ComTotais()
    {
        var r = Assert.IsType<OkObjectResult>((await Controller().GetMeusContratos(default)).Result);
        var c = Assert.Single(((IEnumerable<ContratoHonorarioDto>)r.Value!).ToList());

        Assert.Equal("HON-1", c.NumeroContrato);
        Assert.Equal(1000m, c.ValorPago);
        Assert.True(c.ValorEmAtraso > 1000m);
        Assert.Equal(1, c.ParcelasPagas);
        Assert.Equal(1, c.ParcelasPendentes);
    }

    [Fact]
    public async Task Parcelas_CalculaJurosSoDasEmAberto()
    {
        var r = Assert.IsType<OkObjectResult>((await Controller().GetParcelas(_contratoId, default)).Result);
        var parcelas = ((ParcelasContratoDto)r.Value!).Parcelas.ToList();

        Assert.Equal(4, parcelas.Count);
        Assert.Null(parcelas[0].JurosMulta);
        Assert.NotNull(parcelas[1].JurosMulta);
        Assert.Equal(60, parcelas[1].DiasAtraso);
        Assert.Null(parcelas[2].JurosMulta);
        Assert.Null(parcelas[3].JurosMulta);
    }

    [Fact]
    public async Task Parcelas_DeOutroCliente_NotFound()
    {
        Assert.IsType<NotFoundResult>((await Controller(Guid.NewGuid()).GetParcelas(_contratoId, default)).Result);
    }

    [Fact]
    public async Task Extrato_GeraPdf()
    {
        var r = Assert.IsType<FileContentResult>(await Controller().GetExtrato(_contratoId, null, default));
        Assert.Equal("application/pdf", r.ContentType);
        Assert.NotEmpty(r.FileContents);

        Assert.IsType<NotFoundResult>(await Controller(Guid.NewGuid()).GetExtrato(_contratoId, null, default));
    }
}
