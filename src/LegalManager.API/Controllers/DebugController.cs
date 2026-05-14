using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/debug")]
[Authorize(Roles = "Admin")]
public class DebugController : ControllerBase
{
    private readonly EsajTjspProcessosAdapter _esaj;

    public DebugController(EsajTjspProcessosAdapter esaj)
    {
        _esaj = esaj;
    }

    [HttpGet("esaj")]
    public async Task<IActionResult> DebugEsaj(
        [FromQuery] string cnj,
        [FromQuery] string grau = "G1",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cnj))
            return BadRequest(new { error = "cnj é obrigatório." });

        var numero = cnj.Replace("-", "").Replace(".", "").Replace(" ", "");
        if (numero.Length < 10)
            return BadRequest(new { error = "cnj inválido." });

        // Obter HTML cru
        string rawHtml;
        try
        {
            var endpoint = grau == "G2" ? "cposg" : "cpopg";
            var url = $"/{endpoint}/show.do?processo.codigo=&processo.foro=&processo.numero={Uri.EscapeDataString(cnj)}&uuidCaptcha=";
            var resp = await _esaj.HttpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                return Ok(new { error = $"ESAJ retornou {resp.StatusCode}", statusCode = (int)resp.StatusCode });
            rawHtml = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            return Ok(new { error = $"Erro ao acessar ESAJ: {ex.Message}" });
        }

        // Parse via adapter
        var detalhe = await _esaj.ObterDetalhesAsync(cnj, grau, ct, null, null);

        return Ok(new
        {
            cnj,
            grau,
            rawHtml = rawHtml.Length > 200000 ? rawHtml.Substring(0, 200000) + "\n... (truncado em 200k)" : rawHtml,
            detalhe
        });
    }
}