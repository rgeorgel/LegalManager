using System.Net;
using System.Text.Json;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Identity;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.IntegrationTests;

/// <summary>
/// Cobre a Fase 1 de docs/features/busca-processo-cadastro-manual.md fim-a-fim: a busca
/// (GET /api/processos-monitorados/search) enriquecida com partes/valorCausa/siglaTribunal,
/// e a regra de que a resolução/criação de Contato a partir dessas partes só acontece no
/// Salvar (POST /api/processos), nunca na busca — evitando Contatos órfãos.
///
/// Usa EF InMemory (banco real) + um HttpMessageHandler falso para o DataJud, então nenhuma
/// chamada real é feita à API pública do CNJ.
/// </summary>
public class BuscaProcessoDataJudTests
{
    private const string HitComPartesJson = """
    {
      "hits": {
        "hits": [
          {
            "_source": {
              "tribunal": "Tribunal de Justiça de São Paulo",
              "siglaTribunal": "TJSP",
              "orgaoJulgador": { "nome": "1ª Vara Cível" },
              "classe": { "nome": "Procedimento Comum Cível" },
              "grau": "G1",
              "dataAjuizamento": "2024-01-10T00:00:00.000Z",
              "assuntos": [{ "nome": "Contratos", "codigo": 1127 }],
              "valorCaixa": 15000.50,
              "partes": [
                { "nome": "Fulano DataJud Integração", "cpf": "11122233344", "polo": "AUTOR" }
              ],
              "movimentos": []
            }
          }
        ],
        "total": { "value": 1 }
      }
    }
    """;

    private sealed class FakeHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private static DataJudAdapter CreateFakeDataJudAdapter() =>
        new(new HttpClient(new FakeHandler(HitComPartesJson)) { BaseAddress = new Uri("https://api.cnj.jus.br") },
            Mock.Of<ILogger<DataJudAdapter>>());

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ITenantContext CreateTenantContext(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        mock.Setup(t => t.Plano).Returns(PlanoTipo.Pro);
        return mock.Object;
    }

    private static async Task<(AppDbContext Ctx, Guid TenantId, Guid UserId)> SeedTenantAsync()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório Busca", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Advogado Teste", Email = "adv@busca.com",
            UserName = "adv@busca.com", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();
        return (ctx, tenant.Id, usuario.Id);
    }

    private static ProcessosController CreateProcessosController(AppDbContext ctx, ITenantContext tenantContext)
    {
        var processoService = new ProcessoService(ctx, tenantContext, new Mock<IEscavadorService>().Object);
        var contatoResolver = new ContatoResolverService(new ContatoService(ctx, tenantContext));
        return new ProcessosController(
            processoService,
            new Mock<IMonitoramentoService>().Object,
            new Mock<IAuditService>().Object,
            tenantContext,
            null!,
            ctx,
            contatoResolver);
    }

    [Fact]
    public async Task Search_ProcessoEncontrado_RetornaPartesSemCriarContato()
    {
        var (ctx, tenantId, userId) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenantId, userId);
        var monitoradosController = new ProcessosMonitoradosController(CreateFakeDataJudAdapter(), Mock.Of<ILogger<ProcessosMonitoradosController>>());

        var result = await monitoradosController.Search("0000001-00.2024.8.26.0100", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        // Anonymous types are `internal` to their declaring assembly, so `dynamic` binding
        // against ok.Value fails at runtime with a RuntimeBinderException across assemblies
        // (LegalManager.API vs LegalManager.IntegrationTests). Round-trip through JsonElement
        // instead, which serializes via reflection and isn't gated by that restriction.
        var body = JsonSerializer.SerializeToElement(ok.Value!);
        Assert.True(body.GetProperty("encontrado").GetBoolean());
        Assert.Equal("datajud", body.GetProperty("fonte").GetString());
        Assert.Equal("TJSP", body.GetProperty("siglaTribunal").GetString());
        Assert.Equal(15000.50m, body.GetProperty("valorCausa").GetDecimal());

        // Load-bearing: buscar/pré-visualizar NUNCA cria Contato.
        var contatoService = new ContatoService(ctx, tenantContext);
        var contatos = await contatoService.GetAllAsync(new ContatoFiltroDto("Fulano DataJud Integração", null, null, null, null), CancellationToken.None);
        Assert.Empty(contatos.Items);
    }

    [Fact]
    public async Task Create_ComPartesDataJud_CriaContatoSomenteAoSalvar()
    {
        var (ctx, tenantId, userId) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenantId, userId);
        var controller = CreateProcessosController(ctx, tenantContext);

        var dto = new CreateProcessoDto(
            "1234567-89.2024.8.26.0100", "TJSP", "1ª Vara Cível", null, AreaDireito.Civil, null,
            FaseProcessual.Conhecimento, StatusProcesso.Ativo, 15000.50m, userId,
            PartesDataJud: [new TribunalParte("Fulano DataJud Integração", "11122233344", null, null, "AUTOR")]);

        var result = await controller.Create(dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var processo = Assert.IsType<ProcessoResponseDto>(created.Value);
        Assert.Single(processo.Partes);
        Assert.Equal(TipoParteProcesso.Autor, processo.Partes[0].TipoParte);

        var contatoService = new ContatoService(ctx, tenantContext);
        var contatos = await contatoService.GetAllAsync(new ContatoFiltroDto("Fulano DataJud Integração", null, null, null, null), CancellationToken.None);
        Assert.Single(contatos.Items);
        Assert.Equal(processo.Partes[0].ContatoId, contatos.Items.Single().Id);
    }

    [Fact]
    public async Task Create_ComPartesDataJud_ReaproveitaContatoExistentePorNome_NaoDuplica()
    {
        var (ctx, tenantId, userId) = await SeedTenantAsync();
        var tenantContext = CreateTenantContext(tenantId, userId);
        var contatoService = new ContatoService(ctx, tenantContext);
        var existente = await contatoService.CreateAsync(new CreateContatoDto(
            TipoPessoa.PF, TipoContato.Cliente, "Fulano DataJud Integração", "11122233344",
            null, null, null, null, null, null, null, null, null, false, null), CancellationToken.None);

        var controller = CreateProcessosController(ctx, tenantContext);
        var dto = new CreateProcessoDto(
            "9876543-21.2024.8.26.0100", "TJSP", null, null, AreaDireito.Civil, null,
            FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, userId,
            PartesDataJud: [new TribunalParte("Fulano DataJud Integração", "11122233344", null, null, "AUTOR")]);

        var result = await controller.Create(dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var processo = Assert.IsType<ProcessoResponseDto>(created.Value);
        Assert.Equal(existente.Id, processo.Partes[0].ContatoId);

        var contatos = await contatoService.GetAllAsync(new ContatoFiltroDto("Fulano DataJud Integração", null, null, null, null), CancellationToken.None);
        Assert.Single(contatos.Items); // não duplicou
    }
}
