using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LegalManager.API.Controllers;

[Authorize]
[ApiController]
[Route("api/processos-monitorados")]
public class ProcessosMonitoradosController : ControllerBase
{
    private readonly IProcessoMonitoradoService _service;
    private readonly DataJudAdapter _dataJud;
    private readonly ILogger<ProcessosMonitoradosController> _logger;

    public ProcessosMonitoradosController(
        IProcessoMonitoradoService service,
        DataJudAdapter dataJud,
        ILogger<ProcessosMonitoradosController> logger)
    {
        _service = service;
        _dataJud = dataJud;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string cnj, [FromQuery] string? tribunal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cnj))
            return BadRequest(new { message = "CNJ é obrigatório." });

        var digitsOnly = new string(cnj.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length < 7)
            return BadRequest(new { message = "CNJ inválido." });

        var formatted = FormatCNJ(cnj);
        _logger.LogInformation("Buscando processo CNJ: {CNJ}, Tribunal: {Tribunal}", formatted, tribunal ?? "inferido");

        TribunalConsultaResult result;
        if (!string.IsNullOrWhiteSpace(tribunal))
            result = await _dataJud.ConsultarPorTribunalAsync(digitsOnly, tribunal, ct);
        else
            result = await _dataJud.ConsultarAsync(digitsOnly, ct);

        _logger.LogInformation(
            "Resultado DataJud para {CNJ}: Encontrado={Encontrado}, Tribunal={Tribunal}, Vara={Vara}, Movimentacoes={Movimentacoes}",
            formatted, result.Encontrado, result.NomeTribunal, result.Vara, result.Movimentos?.Count ?? 0);

        return Ok(new
        {
            numeroCNJ = formatted,
            encontrado = result.Encontrado,
            tribunal = result.NomeTribunal,
            vara = result.Vara,
            movimentosCount = result.Movimentos?.Count ?? 0
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProcessoMonitoradoDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetAll), result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.ToggleAtivoAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static string FormatCNJ(string numero)
    {
        var digits = new string(numero.Where(char.IsDigit).ToArray());
        if (digits.Length < 7) return numero;
        return $"{digits[..7]}-{digits[7..9]}.{digits[9..13]}.{digits[13]}.{digits[14..16]}.{digits[16..20]}";
    }
}