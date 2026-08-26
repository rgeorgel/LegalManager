using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[Authorize]
[ApiController]
[Route("api/processos-monitorados")]
public class ProcessosMonitoradosController(
    DataJudAdapter dataJud,
    ILogger<ProcessosMonitoradosController> logger,
    IEscavadorService? escavador = null) : ControllerBase
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

        if (result.Encontrado)
        {
            return Ok(new
            {
                numeroCNJ = formatted,
                encontrado = true,
                fonte = "datajud",
                tribunal = result.NomeTribunal,
                vara = result.Vara,
                movimentosCount = result.Movimentos?.Count ?? 0,
                classe = result.Classe,
                assuntos = result.Assuntos,
                dataAjuizamento = result.DataAjuizamento,
                grau = result.Grau,
                valorCausa = result.ValorCaixa,
                siglaTribunal = result.SiglaTribunal,
                partes = result.Partes?.Select(p => new {
                    nome = p.Nome,
                    cpf = p.Cpf,
                    cnpj = p.Cnpj,
                    oab = p.OAB,
                    polo = p.Polo
                }),
                movimentos = result.Movimentos?.Select(m => new {
                    descricao = m.Descricao,
                    data = m.Data,
                    tipoNome = m.TipoNome,
                    codigoCNJ = m.CodigoCNJ,
                    orgaoJulgador = m.OrgaoJulgador
                })
            });
        }

        // DataJud não encontrou (ex: processo muito recente, ainda não indexado). Fallback
        // pago: Escavador, reusando a mesma chamada já usada no polling de andamentos
        // (docs/features/busca-processo-cadastro-manual.md, Fase 2). Só dispara aqui —
        // nunca quando o DataJud já encontrou, para não gastar sem necessidade.
        if (escavador != null)
        {
            var escavadorResult = await escavador.ListarMovimentacoesPorProcessoAsync(formatted, desde: null, ct: ct);
            if (escavadorResult.Data.Count > 0)
            {
                logger.LogInformation(
                    "Processo CNJ {CNJ} não encontrado no DataJud; encontrado via Escavador (fallback pago, {N} movimentações)",
                    formatted, escavadorResult.Data.Count);

                // Segunda chamada, só disparada aqui (já confirmado que o Escavador conhece o
                // processo): busca a capa (classe/vara/tribunal/valorCausa/assuntos), que o
                // endpoint de movimentações não traz. Endpoint ainda NÃO CONFIRMADO ao vivo — ver
                // doc completa em IEscavadorService.BuscarCapaPorNumeroCnjAsync. Se falhar/retornar
                // null, a resposta segue igual à de hoje (campos null) — nunca derruba o resultado
                // já obtido via movimentações.
                var capa = await escavador.BuscarCapaPorNumeroCnjAsync(formatted, ct);

                return Ok(new
                {
                    numeroCNJ = formatted,
                    encontrado = true,
                    fonte = "escavador",
                    tribunal = capa?.NomeTribunal,
                    vara = capa?.Vara,
                    movimentosCount = escavadorResult.Data.Count,
                    classe = capa?.Classe,
                    assuntos = capa?.Assuntos,
                    dataAjuizamento = (DateTime?)null,
                    grau = (string?)null,
                    valorCausa = capa?.ValorCausa,
                    siglaTribunal = capa?.SiglaTribunal,
                    partes = (object?)null,
                    movimentos = escavadorResult.Data.Select(m => new {
                        descricao = !string.IsNullOrWhiteSpace(m.Snippet) ? m.Snippet : ResumirHtml(m.ConteudoHtml),
                        data = m.Data,
                        tipoNome = m.Tipo,
                        codigoCNJ = (string?)null,
                        orgaoJulgador = (string?)null
                    })
                });
            }
        }

        return Ok(new
        {
            numeroCNJ = formatted,
            encontrado = false,
            fonte = "datajud",
            tribunal = result.NomeTribunal,
            vara = result.Vara,
            movimentosCount = result.Movimentos?.Count ?? 0,
            classe = result.Classe,
            assuntos = result.Assuntos,
            dataAjuizamento = result.DataAjuizamento,
            grau = result.Grau,
            valorCausa = result.ValorCaixa,
            siglaTribunal = result.SiglaTribunal,
            partes = result.Partes?.Select(p => new {
                nome = p.Nome,
                cpf = p.Cpf,
                cnpj = p.Cnpj,
                oab = p.OAB,
                polo = p.Polo
            }),
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

    private static string ResumirHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "Atualização via Escavador";
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var compact = System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", " ").Trim();
        return compact.Length > 500 ? compact[..500] + "…" : compact;
    }
}
