using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[Authorize]
[ApiController]
[Route("api/processos-monitorados")]
public class ProcessosMonitoradosController(
    DataJudAdapter dataJud,
    ILogger<ProcessosMonitoradosController> logger) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string cnj, [FromQuery] string? tribunal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cnj))
            return BadRequest(new { message = "CNJ é obrigatório." });

        var digitsOnly = new string(cnj.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length < 7)
            return BadRequest(new { message = "CNJ inválido." });

        var formatted = FormatCNJ(cnj);
        logger.LogInformation("Buscando processo CNJ: {CNJ}, Tribunal: {Tribunal}",
            formatted, tribunal ?? "inferido");

        var result = !string.IsNullOrWhiteSpace(tribunal)
            ? await dataJud.ConsultarPorTribunalAsync(digitsOnly, tribunal, ct)
            : await dataJud.ConsultarAsync(digitsOnly, ct);

        return Ok(new
        {
            numeroCNJ = formatted,
            encontrado = result.Encontrado,
            tribunal = result.NomeTribunal,
            vara = result.Vara,
            movimentosCount = result.Movimentos?.Count ?? 0,
            classe = result.Classe,
            assuntos = result.Assuntos,
            dataAjuizamento = result.DataAjuizamento,
            grau = result.Grau,
            movimentos = result.Movimentos?.Select(m => new {
                descricao = m.Descricao,
                data = m.Data,
                tipoNome = m.TipoNome,
                codigoCNJ = m.CodigoCNJ,
                orgaoJulgador = m.OrgaoJulgador
            })
        });
    }

    private static string FormatCNJ(string numero)
    {
        var digits = new string(numero.Where(char.IsDigit).ToArray());
        if (digits.Length < 7) return numero;
        return $"{digits[..7]}-{digits[7..9]}.{digits[9..13]}.{digits[13]}.{digits[14..16]}.{digits[16..]}";
    }
}
