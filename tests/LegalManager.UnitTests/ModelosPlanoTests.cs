using LegalManager.API.Controllers;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;

namespace LegalManager.UnitTests;

// A aba Modelos só aparece a partir do Plus; a API de modelos barra o Free em todas as rotas.
public class ModelosPlanoTests
{
    private static (ModelosController Controller, ActionExecutingContext Contexto) Montar(PlanoTipo plano)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.Plano).Returns(plano);
        var controller = new ModelosController(new Mock<IModeloDocumentoService>().Object, tenant.Object);
        var contexto = new ActionExecutingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [], new Dictionary<string, object?>(), controller);
        return (controller, contexto);
    }

    [Fact]
    public void Free_RecebePagamentoNecessario()
    {
        var (controller, contexto) = Montar(PlanoTipo.Free);

        controller.OnActionExecuting(contexto);

        var resultado = Assert.IsType<ObjectResult>(contexto.Result);
        Assert.Equal(StatusCodes.Status402PaymentRequired, resultado.StatusCode);
    }

    [Theory]
    [InlineData(PlanoTipo.Plus)]
    [InlineData(PlanoTipo.Pro)]
    [InlineData(PlanoTipo.Max)]
    [InlineData(PlanoTipo.Enterprise)]
    public void PlanosPagos_Seguem(PlanoTipo plano)
    {
        var (controller, contexto) = Montar(plano);

        controller.OnActionExecuting(contexto);
        controller.OnActionExecuted(new ActionExecutedContext(contexto, [], controller));

        Assert.Null(contexto.Result);
    }

    [Theory]
    [InlineData(PlanoTipo.Free, false)]
    [InlineData(PlanoTipo.Plus, true)]
    [InlineData(PlanoTipo.Pro, true)]
    [InlineData(PlanoTipo.Max, true)]
    [InlineData(PlanoTipo.Enterprise, true)]
    public void PermiteModelosDocumentos_APartirDoPlus(PlanoTipo plano, bool esperado) =>
        Assert.Equal(esperado, PlanoRestricoes.PermiteModelosDocumentos(plano));
}
