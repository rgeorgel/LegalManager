using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Escavador;

// Used during development to avoid spending paid Escavador API credits.
// Activate with: Escavador__UseMock=true in environment / .env
public class EscavadorMockClient : IEscavadorService
{
    private readonly ILogger<EscavadorMockClient> _logger;

    public EscavadorMockClient(ILogger<EscavadorMockClient> logger)
    {
        _logger = logger;
    }

    public Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorOabAsync(
        string oab, string uf, int pagina = 1, CancellationToken ct = default)
    {
        _logger.LogWarning("[Escavador MOCK] BuscarPorOabAsync OAB={Oab}/{Uf} — retornando dados fictícios", oab, uf);

        var rawJson = BuildRawJson("TRT4", "Vara do Trabalho de Porto Alegre", "Porto Alegre");
        var processos = new List<EscavadorProcessoDto>
        {
            new(
                Id: 0,
                Numero: $"0005040-12.2019.5.04.0271",
                SiglaTribunal: "TRT4",
                NomeTribunal: "Tribunal Regional do Trabalho da 4ª Região",
                Vara: "4ª Vara do Trabalho de Porto Alegre",
                Comarca: "Porto Alegre",
                Classe: "AÇÃO TRABALHISTA - RITO ORDINÁRIO",
                Assuntos: "Rescisão do Contrato de Trabalho / Verbas Rescisórias",
                DataAjuizamento: new DateTime(2019, 5, 10),
                JsonBruto: rawJson
            ),
            new(
                Id: 0,
                Numero: $"0012345-67.2021.8.26.0100",
                SiglaTribunal: "TJSP",
                NomeTribunal: "Tribunal de Justiça de São Paulo",
                Vara: "1ª Vara Cível",
                Comarca: "São Paulo",
                Classe: "PROCEDIMENTO COMUM CÍVEL",
                Assuntos: "Responsabilidade Civil / Indenização por Dano Moral",
                DataAjuizamento: new DateTime(2021, 3, 22),
                JsonBruto: BuildRawJson("TJSP", "1ª Vara Cível - Foro Central Cível", "São Paulo")
            ),
            new(
                Id: 0,
                Numero: $"5001234-89.2022.4.03.6100",
                SiglaTribunal: "TRF3",
                NomeTribunal: "Tribunal Regional Federal da 3ª Região",
                Vara: "1ª Vara Federal Cível",
                Comarca: "São Paulo",
                Classe: "PROCEDIMENTO COMUM",
                Assuntos: "Benefício Previdenciário",
                DataAjuizamento: new DateTime(2022, 8, 5),
                JsonBruto: BuildRawJson("TRF3", "1ª Vara Federal Cível", "São Paulo")
            ),
        };

        var result = new EscavadorPagedResult<EscavadorProcessoDto>(processos, processos.Count, 1, 1, false);
        return Task.FromResult(result);
    }

    public Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorCpfCnpjAsync(
        string cpfCnpj, int pagina = 1, CancellationToken ct = default)
    {
        _logger.LogWarning("[Escavador MOCK] BuscarPorCpfCnpjAsync — retornando dados fictícios");

        var processos = new List<EscavadorProcessoDto>
        {
            new(
                Id: 0,
                Numero: "0099988-77.2020.5.15.0003",
                SiglaTribunal: "TRT15",
                NomeTribunal: "Tribunal Regional do Trabalho da 15ª Região",
                Vara: "3ª Vara do Trabalho de Campinas",
                Comarca: "Campinas",
                Classe: "AÇÃO TRABALHISTA - RITO SUMARÍSSIMO",
                Assuntos: "Horas Extras",
                DataAjuizamento: new DateTime(2020, 11, 3),
                JsonBruto: BuildRawJson("TRT15", "3ª Vara do Trabalho de Campinas", "Campinas")
            ),
        };

        var result = new EscavadorPagedResult<EscavadorProcessoDto>(processos, processos.Count, 1, 1, false);
        return Task.FromResult(result);
    }

    public Task<EscavadorMonitoramentoDto?> CriarMonitoramentoAsync(
        string numeroCNJ, string? frequencia = null, CancellationToken ct = default)
    {
        _logger.LogWarning("[Escavador MOCK] CriarMonitoramentoAsync {CNJ} frequencia={F} — retornando ID fictício", numeroCNJ, frequencia ?? "diaria");
        EscavadorMonitoramentoDto? dto = new(Id: 999999L, Status: "ativo");
        return Task.FromResult(dto);
    }

    public Task<bool> RemoverMonitoramentoAsync(long id, CancellationToken ct = default)
    {
        _logger.LogWarning("[Escavador MOCK] RemoverMonitoramentoAsync {Id}", id);
        return Task.FromResult(true);
    }

    public Task<EscavadorPagedResult<EscavadorCallbackDto>> ListarCallbacksPendentesAsync(
        int pagina = 1, CancellationToken ct = default)
    {
        var result = new EscavadorPagedResult<EscavadorCallbackDto>([], 0, 1, 1, false);
        return Task.FromResult(result);
    }

    public Task MarcarCallbacksRecebidosAsync(IEnumerable<long> ids, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task MarcarCallbackRecebidoAsync(string uuid, CancellationToken ct = default)
    {
        _logger.LogWarning("[Escavador MOCK] MarcarCallbackRecebidoAsync uuid={Uuid}", uuid);
        return Task.CompletedTask;
    }

    public Task<EscavadorMovimentacaoDto?> BuscarMovimentacoesPorUuidAsync(
        string uuid, CancellationToken ct = default)
    {
        _logger.LogWarning("[Escavador MOCK] BuscarMovimentacoesPorUuidAsync uuid={Uuid}", uuid);
        EscavadorMovimentacaoDto? dto = uuid.StartsWith("legacy-", StringComparison.Ordinal)
            ? null
            : new(
                Id: 1L,
                Uuid: uuid,
                Data: DateTime.UtcNow.AddDays(-1),
                ConteudoHtml: "<p>Decisão interlocutória procedente. Prazo: 15 dias para manifestação.</p>",
                Snippet: "Decisão procedente. Prazo: 15 dias.",
                Tipo: "Decisão",
                Diario: "DJe",
                DiarioId: 140L,
                DiarioSigla: "TRT-2",
                OrigemEstado: "SP",
                Link: $"https://www.escavador.com/movimentacoes/mock-{uuid}",
                LinkApi: $"https://api.escavador.com/api/v1/movimentacoes/1",
                LinkPdf: $"https://api.escavador.com/api/v1/diarios/140/pdf/pagina/1/baixar",
                LinkPdfApi: $"https://api.escavador.com/api/v1/diarios/140/pdf/pagina/1/baixar",
                NumeroCnj: "0001234-56.2024.5.02.0070",
                JsonBruto: "{}"
            );
        return Task.FromResult(dto);
    }

    public Task<EscavadorPagedResult<EscavadorMovimentacaoDto>> ListarMovimentacoesPorProcessoAsync(
        string numeroCNJ, DateTime? desde, int pagina = 1, CancellationToken ct = default)
    {
        _logger.LogWarning("[Escavador MOCK] ListarMovimentacoesPorProcessoAsync CNJ={CNJ}", numeroCNJ);
        // Deterministic 0–3 items based on CNJ hash so different processos yield different counts.
        var count = Math.Abs((numeroCNJ ?? "").GetHashCode()) % 4;
        var items = new List<EscavadorMovimentacaoDto>();
        for (var i = 0; i < count; i++)
        {
            var data = (desde ?? DateTime.UtcNow.AddDays(-7)).AddDays(i);
            items.Add(new EscavadorMovimentacaoDto(
                Id: 1000L + i,
                Uuid: $"mock-{numeroCNJ}-{i}",
                Data: data,
                ConteudoHtml: $"<p>Movimentação mock #{i + 1} do processo {numeroCNJ}.</p>",
                Snippet: $"Movimentação #{i + 1}",
                Tipo: i == 0 ? "Decisão" : "Despacho",
                Diario: "DJe",
                DiarioId: 140L,
                DiarioSigla: "TRT-2",
                OrigemEstado: "SP",
                Link: $"https://www.escavador.com/movimentacoes/mock-{i}",
                LinkApi: $"https://api.escavador.com/api/v1/movimentacoes/{1000 + i}",
                LinkPdf: $"https://api.escavador.com/api/v1/diarios/140/pdf/pagina/{i + 1}/baixar",
                LinkPdfApi: $"https://api.escavador.com/api/v1/diarios/140/pdf/pagina/{i + 1}/baixar",
                NumeroCnj: numeroCNJ,
                JsonBruto: "{}"
            ));
        }
        var result = new EscavadorPagedResult<EscavadorMovimentacaoDto>(items, items.Count, 1, 1, false);
        return Task.FromResult(result);
    }

    public Task<EscavadorPagedResult<EscavadorPublicacaoDto>> BuscarPublicacoesPorOabAsync(
        string oab, string uf, DateTime de, DateTime ate, int pagina = 1, CancellationToken ct = default)
    {
        _logger.LogWarning("[Escavador MOCK] BuscarPublicacoesPorOabAsync {Uf}/{Oab} de={De:yyyy-MM-dd} ate={Ate:yyyy-MM-dd}",
            uf, oab, de, ate);
        // Deterministic 1–5 items per OAB so the search endpoint yields something to display.
        var seed = Math.Abs((uf + oab).GetHashCode());
        var count = (seed % 5) + 1;
        var items = new List<EscavadorPublicacaoDto>();
        for (var i = 0; i < count; i++)
        {
            var data = de.AddDays(seed % Math.Max(1, (ate - de).Days));
            items.Add(new EscavadorPublicacaoDto(
                Id: 9000L + i,
                Uuid: $"mock-oab-{oab}-{uf}-{i}",
                NumeroCnj: $"0005040-{i:00}.2024.5.02.0070",
                Data: data,
                Diario: "DJe",
                Snippet: $"Publicação OAB #{i + 1}",
                LinkPdf: $"https://api.escavador.com/api/v1/diarios/140/pdf/pagina/{i + 1}/baixar",
                JsonBruto: "{}"
            ));
        }
        var result = new EscavadorPagedResult<EscavadorPublicacaoDto>(items, items.Count, 1, 1, false);
        return Task.FromResult(result);
    }

    public Task<EscavadorMonitoramentoDto?> CriarMonitoramentoOabAsync(
        string uf, string numero, string? nomeAdvogado, CancellationToken ct = default)
    {
        _logger.LogWarning("[Escavador MOCK] CriarMonitoramentoOabAsync {Uf}/{Numero} advogado={Nome} — ID fictício",
            uf, numero, nomeAdvogado ?? "(sem nome)");
        // Deterministic ID based on UF + numero so re-syncs return the same ID
        var id = 5_000_000L + Math.Abs((uf + numero).GetHashCode() % 1_000_000);
        EscavadorMonitoramentoDto? dto = new(Id: id, Status: "ativo");
        return Task.FromResult(dto);
    }

    // Builds a JSON string that matches the real Escavador v2 item shape so cache enrichment works
    private static string BuildRawJson(string tribunalSigla, string orgaoJulgador, string cidade) =>
        $$"""
        {
          "numero_cnj": "mock",
          "data_inicio": "2020-01-01T00:00:00",
          "unidade_origem": {
            "tribunal_sigla": "{{tribunalSigla}}",
            "nome": "{{orgaoJulgador}}",
            "cidade": "{{cidade}}"
          },
          "fontes": [
            {
              "capa": {
                "classe": "PROCEDIMENTO COMUM",
                "assunto": "Direito Civil",
                "orgao_julgador": "{{orgaoJulgador}}"
              }
            }
          ]
        }
        """;
}
