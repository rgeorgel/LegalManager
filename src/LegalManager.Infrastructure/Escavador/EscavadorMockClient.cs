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
