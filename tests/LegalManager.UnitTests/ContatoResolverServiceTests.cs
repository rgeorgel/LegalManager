using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Services;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Cobre o ContatoResolverService, extraído de OnboardingController.ResolverPartesDataJudAsync
/// (docs/features/busca-processo-cadastro-manual.md, Fase 1, item 2).
/// </summary>
public class ContatoResolverServiceTests
{
    private static ContatoResponseDto MakeContato(Guid id, string nome) => new(
        id, TipoPessoa.PF, TipoContato.Cliente, nome, null, null, null, null, null, null, null, null,
        null, null, false, true, new List<string>(), DateTime.UtcNow);

    [Fact]
    public async Task ResolverPartesDataJudAsync_ContatoExistente_ReaproveitaSemCriar()
    {
        var contatoService = new Mock<IContatoService>();
        var existente = MakeContato(Guid.NewGuid(), "João Silva");
        contatoService.Setup(s => s.GetByNomeAsync("João Silva", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existente);
        var service = new ContatoResolverService(contatoService.Object);

        var resultado = await service.ResolverPartesDataJudAsync(
            [new TribunalParte("João Silva", "12345678900", null, null, "AUTOR")]);

        Assert.Single(resultado);
        Assert.Equal(existente.Id, resultado[0].ContatoId);
        Assert.Equal(TipoParteProcesso.Autor, resultado[0].TipoParte);
        contatoService.Verify(s => s.CreateAsync(It.IsAny<CreateContatoDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolverPartesDataJudAsync_ContatoInexistente_CriaComoPF_QuandoTemCpf()
    {
        var contatoService = new Mock<IContatoService>();
        contatoService.Setup(s => s.GetByNomeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContatoResponseDto?)null);
        CreateContatoDto? dtoCapturado = null;
        var criado = MakeContato(Guid.NewGuid(), "Maria Souza");
        contatoService.Setup(s => s.CreateAsync(It.IsAny<CreateContatoDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateContatoDto, CancellationToken>((dto, _) => dtoCapturado = dto)
            .ReturnsAsync(criado);
        var service = new ContatoResolverService(contatoService.Object);

        var resultado = await service.ResolverPartesDataJudAsync(
            [new TribunalParte("Maria Souza", "98765432100", null, "OAB/SP 123", "RÉU")]);

        Assert.Single(resultado);
        Assert.Equal(criado.Id, resultado[0].ContatoId);
        Assert.Equal(TipoParteProcesso.Reu, resultado[0].TipoParte);
        Assert.Equal(TipoPessoa.PF, dtoCapturado!.Tipo);
        Assert.Equal("98765432100", dtoCapturado.CpfCnpj);
        Assert.Equal("OAB/SP 123", dtoCapturado.Oab);
    }

    [Fact]
    public async Task ResolverPartesDataJudAsync_ContatoInexistente_CriaComoPJ_QuandoSoTemCnpj()
    {
        var contatoService = new Mock<IContatoService>();
        contatoService.Setup(s => s.GetByNomeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContatoResponseDto?)null);
        CreateContatoDto? dtoCapturado = null;
        var criado = MakeContato(Guid.NewGuid(), "Empresa X Ltda");
        contatoService.Setup(s => s.CreateAsync(It.IsAny<CreateContatoDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateContatoDto, CancellationToken>((dto, _) => dtoCapturado = dto)
            .ReturnsAsync(criado);
        var service = new ContatoResolverService(contatoService.Object);

        var resultado = await service.ResolverPartesDataJudAsync(
            [new TribunalParte("Empresa X Ltda", null, "11222333000144", null, "EXECUTADO")]);

        Assert.Equal(TipoPessoa.PJ, dtoCapturado!.Tipo);
        Assert.Equal("11222333000144", dtoCapturado.CpfCnpj);
        Assert.Equal(TipoParteProcesso.Reu, resultado[0].TipoParte);
    }

    [Theory]
    [InlineData("AUTOR", TipoParteProcesso.Autor)]
    [InlineData("RECLAMANTE", TipoParteProcesso.Autor)]
    [InlineData("EXEQUENTE", TipoParteProcesso.Autor)]
    [InlineData("RÉU", TipoParteProcesso.Reu)]
    [InlineData("REU", TipoParteProcesso.Reu)]
    [InlineData("EXECUTADO", TipoParteProcesso.Reu)]
    [InlineData("INTERESSADO", TipoParteProcesso.Interessado)]
    [InlineData("TESTEMUNHA", TipoParteProcesso.Terceiro)]
    [InlineData(null, TipoParteProcesso.Terceiro)]
    public async Task ResolverPartesDataJudAsync_MapeiaPoloParaTipoParte(string? polo, TipoParteProcesso esperado)
    {
        var contatoService = new Mock<IContatoService>();
        contatoService.Setup(s => s.GetByNomeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeContato(Guid.NewGuid(), "Fulano"));
        var service = new ContatoResolverService(contatoService.Object);

        var resultado = await service.ResolverPartesDataJudAsync(
            [new TribunalParte("Fulano", null, null, null, polo)]);

        Assert.Equal(esperado, resultado[0].TipoParte);
    }

    [Fact]
    public async Task ResolverPartesDataJudAsync_IgnoraPartesSemNome()
    {
        var contatoService = new Mock<IContatoService>();
        var service = new ContatoResolverService(contatoService.Object);

        var resultado = await service.ResolverPartesDataJudAsync(
            [new TribunalParte("", null, null, null, "AUTOR"), new TribunalParte("   ", null, null, null, "AUTOR")]);

        Assert.Empty(resultado);
        contatoService.Verify(s => s.GetByNomeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolverPartesDataJudAsync_ListaVazia_RetornaListaVazia()
    {
        var contatoService = new Mock<IContatoService>();
        var service = new ContatoResolverService(contatoService.Object);

        var resultado = await service.ResolverPartesDataJudAsync([]);

        Assert.Empty(resultado);
    }
}
