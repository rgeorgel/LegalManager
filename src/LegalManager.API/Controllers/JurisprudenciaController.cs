using LegalManager.API.Filters;
using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegalManager.API.Controllers;

// Jurisprudência verificada (recurso em teste, liberado pelo super admin por escritório):
// conferência avulsa das citações de um texto ou documento na base oficial. A conferência das peças
// geradas acontece no próprio PecaJuridicaService. Ver docs/features/jurisprudencia-verificada.md.
[ApiController]
[Route("api/jurisprudencia")]
[Authorize]
[ExigeRecurso(RecursosEmTeste.JurisprudenciaVerificada)]
public class JurisprudenciaController(IJurisprudenciaService jurisprudencia) : ControllerBase
{
    public const int TamanhoMaximoTexto = 500_000;
    public const string PoliticaRateLimitPublico = "verificador-citacoes-publico";
    public const int VerificacoesPublicasPorHora = 20;
    public const int TamanhoMaximoTextoPublico = 20_000;

    /// <summary>
    /// Página pública /verificador-de-citacoes.html (sem login, para captação): só texto colado, até
    /// 20 mil caracteres, com limite por IP. [AllowAnonymous] passa pelo [ExigeRecurso] da classe.
    /// </summary>
    [HttpPost("publico/verificar")]
    [AllowAnonymous]
    [EnableRateLimiting(PoliticaRateLimitPublico)]
    public async Task<ActionResult<VerificacaoCitacoesDto>> VerificarPublico([FromBody] VerificarCitacoesDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Texto))
            return BadRequest(new { message = "Cole o trecho da peça com as citações." });
        if (dto.Texto.Length > TamanhoMaximoTextoPublico)
            return BadRequest(new { message = $"Texto grande demais para a verificação gratuita (até {TamanhoMaximoTextoPublico / 1000} mil caracteres). Crie uma conta para verificar peças inteiras." });

        return Ok(await jurisprudencia.VerificarTextoPublicoAsync(dto.Texto, ct));
    }

    [HttpPost("verificar")]
    public async Task<ActionResult<VerificacaoCitacoesDto>> Verificar([FromBody] VerificarCitacoesDto dto, CancellationToken ct)
    {
        if (dto.DocumentoId is { } documentoId)
        {
            try
            {
                var resultado = await jurisprudencia.VerificarDocumentoAsync(documentoId, ct);
                return resultado is null ? NotFound(new { message = "Documento não encontrado." }) : Ok(resultado);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        if (string.IsNullOrWhiteSpace(dto.Texto))
            return BadRequest(new { message = "Cole um texto ou escolha um documento." });
        if (dto.Texto.Length > TamanhoMaximoTexto)
            return BadRequest(new { message = "Texto grande demais. Verifique por partes." });

        return Ok(await jurisprudencia.VerificarTextoAsync(dto.Texto, consultarBase: true, ct));
    }
}
