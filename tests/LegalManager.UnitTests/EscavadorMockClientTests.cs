using LegalManager.Infrastructure.Escavador;
using Microsoft.Extensions.Logging.Abstractions;

namespace LegalManager.UnitTests;

/// <summary>
/// O mock do Escavador (Escavador__UseMock=true) alimenta o ambiente de desenvolvimento:
/// precisa devolver dados no mesmo formato do cliente real e de forma determinística.
/// </summary>
public class EscavadorMockClientTests
{
    private readonly EscavadorMockClient _mock = new(NullLogger<EscavadorMockClient>.Instance);

    [Fact]
    public async Task BuscasPorOabECpf_RetornamProcessosComJsonBruto()
    {
        var oab = await _mock.BuscarPorOabAsync("12345", "SP");
        Assert.NotEmpty(oab.Data);
        Assert.All(oab.Data, p => Assert.False(string.IsNullOrEmpty(p.JsonBruto)));

        var pagina = await _mock.BuscarPaginaPorOabAsync("12345", "SP", null);
        Assert.Equal(oab.Data.Count, pagina.Data.Count);
        Assert.Null(pagina.ProximoCursor);

        var cpf = await _mock.BuscarPorCpfCnpjAsync("12345678900");
        Assert.NotEmpty(cpf.Data);
    }

    [Fact]
    public async Task Monitoramentos_SempreSucesso()
    {
        Assert.NotNull(await _mock.CriarMonitoramentoAsync("0001234-56.2024.8.26.0100"));
        Assert.True(await _mock.RemoverMonitoramentoAsync(1));
        Assert.True(await _mock.RemoverMonitoramentoTermoAsync(1));

        var a = await _mock.CriarMonitoramentoOabAsync("SP", "12345", null);
        var b = await _mock.CriarMonitoramentoOabAsync("SP", "12345", "Ana");
        Assert.Equal(a!.Id, b!.Id);
    }

    [Fact]
    public async Task Callbacks_VaziosENaoFalham()
    {
        var r = await _mock.ListarCallbacksAsync(DateTime.UtcNow.AddDays(-1));
        Assert.Empty(r.Data);
        await _mock.MarcarCallbacksRecebidosAsync([1, 2]);
    }

    [Fact]
    public async Task Movimentacoes_DoProcessoPedido()
    {
        var porUuid = await _mock.BuscarMovimentacoesPorUuidAsync("uuid-1");
        Assert.NotNull(porUuid);

        // 0–3 itens conforme o hash do CNJ: entre vários processos, algum traz movimentações.
        var total = 0;
        for (var i = 0; i < 20; i++)
        {
            var cnj = $"00012{i:00}-56.2024.8.26.0100";
            var lista = await _mock.ListarMovimentacoesPorProcessoAsync(cnj, i % 2 == 0 ? null : DateTime.UtcNow.AddDays(-1));
            Assert.All(lista.Data, m => Assert.Equal(cnj, m.NumeroCnj));
            total += lista.Data.Count;
        }
        Assert.True(total > 0);

        var capa = await _mock.BuscarCapaPorNumeroCnjAsync("0001234-56.2024.8.26.0100");
        Assert.Equal("0001234-56.2024.8.26.0100", capa!.Numero);
    }

    [Fact]
    public async Task PublicacoesPorOab_DeterministicasDentroDoPeriodo()
    {
        var de = new DateTime(2026, 1, 1);
        var ate = new DateTime(2026, 1, 31);

        var r1 = await _mock.BuscarPublicacoesPorOabAsync("12345", "SP", de, ate);
        var r2 = await _mock.BuscarPublicacoesPorOabAsync("12345", "SP", de, ate);

        Assert.InRange(r1.Data.Count, 1, 5);
        Assert.Equal(r1.Data.Count, r2.Data.Count);
        Assert.All(r1.Data, p => Assert.InRange(p.Data, de, ate));
    }
}
