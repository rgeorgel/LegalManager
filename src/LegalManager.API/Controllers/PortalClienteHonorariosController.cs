using System.Security.Claims;
using LegalManager.Application.DTOs.Honorarios;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/portal/meus-honorarios")]
[Authorize(Roles = "Cliente")]
public class PortalClienteHonorariosController(AppDbContext db) : ControllerBase
{
    private static (decimal multa, decimal juros, int dias, decimal total) CalcularJuros(decimal valor, DateTime vencimento, DateTime referencia)
    {
        var dv = vencimento.Date;
        var dr = referencia.Date;
        if (dr <= dv) return (0m, 0m, 0, valor);
        var dias = (int)(dr - dv).TotalDays;
        var meses = dias / 30m;
        var multa = Math.Round(valor * 0.02m, 2, MidpointRounding.AwayFromZero);
        var juros = Math.Round(valor * 0.015m * meses, 2, MidpointRounding.AwayFromZero);
        return (multa, juros, dias, Math.Round(valor + multa + juros, 2, MidpointRounding.AwayFromZero));
    }

    private (Guid contatoId, Guid tenantId) GetContatoTenant()
    {
        var contatoId = Guid.Parse(User.FindFirstValue("contatoId") ?? Guid.Empty.ToString());
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId") ?? Guid.Empty.ToString());
        return (contatoId, tenantId);
    }

    [HttpGet("contratos")]
    public async Task<ActionResult<IEnumerable<ContratoHonorarioDto>>> GetMeusContratos(CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var contratos = await db.ContratosHonorarios
            .AsNoTracking()
            .Include(c => c.Contato)
            .Include(c => c.Processo)
            .Include(c => c.Parcelas)
            .Where(c => c.TenantId == tenantId && c.ContatoId == contatoId
                && c.Status != StatusContratoHonorario.Distratado
                && c.Status != StatusContratoHonorario.Encerrado)
            .OrderByDescending(c => c.CriadoEm)
            .ToListAsync(ct);

        var hoje = DateTime.UtcNow.Date;
        var resultado = contratos.Select(c =>
        {
            decimal pago = 0, pendente = 0, atraso = 0;
            int total = 0, pagas = 0, vencidas = 0, pend = 0;
            foreach (var p in c.Parcelas)
            {
                total++;
                if (p.Status == StatusParcelaHonorario.Pago)
                {
                    pago += p.ValorPago ?? p.ValorOriginal;
                    pagas++;
                }
                else
                {
                    var dias = (hoje - p.Vencimento.Date).Days;
                    if (dias > 0)
                    {
                        var j = CalcularJuros(p.ValorOriginal, p.Vencimento, hoje);
                        atraso += j.total;
                        vencidas++;
                    }
                    else
                    {
                        pendente += p.ValorOriginal;
                        pend++;
                    }
                }
            }
            return new ContratoHonorarioDto(
                c.Id, c.NumeroContrato, c.ContatoId, c.Contato?.Nome ?? "", c.Contato?.CpfCnpj,
                c.ProcessoId, c.Processo?.NumeroCNJ,
                c.Objeto, c.ValorTotal, c.FormaPagamento, c.Periodicidade,
                c.NumeroParcelas, c.DataPrimeiraParcela, c.ValorEntrada, c.VencimentoEntrada,
                c.PercentualMulta, c.PercentualJurosMensal, c.TipoCobranca,
                c.Observacoes, c.Status, c.DataInicio, c.DataFim,
                c.CriadoEm, c.AtualizadoEm, c.DistratoEm, c.DistratoMotivo,
                pago, pendente, atraso, total, pagas, vencidas, pend
            );
        });

        return Ok(resultado);
    }

    [HttpGet("contratos/{id:guid}/parcelas")]
    public async Task<ActionResult<ParcelasContratoDto>> GetParcelas(Guid id, CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var contratoExiste = await db.ContratosHonorarios
            .AnyAsync(x => x.Id == id && x.TenantId == tenantId && x.ContatoId == contatoId, ct);
        if (!contratoExiste) return NotFound();

        var parcelas = await db.ParcelasHonorarios
            .AsNoTracking()
            .Where(p => p.ContratoId == id && p.TenantId == tenantId)
            .OrderBy(p => p.Numero).ThenBy(p => p.Vencimento)
            .ToListAsync(ct);

        var hoje = DateTime.UtcNow.Date;
        var items = parcelas.Select(p =>
        {
            decimal? juros = null, valorAtual = null;
            int? dias = null;
            if (p.Status != StatusParcelaHonorario.Pago && p.Status != StatusParcelaHonorario.Cancelado)
            {
                var d = (hoje - p.Vencimento.Date).Days;
                if (d > 0)
                {
                    var j = CalcularJuros(p.ValorOriginal, p.Vencimento, hoje);
                    juros = Math.Round(j.multa + j.juros, 2);
                    valorAtual = j.total;
                    dias = j.dias;
                }
            }
            return new ParcelaHonorarioDto(
                p.Id, p.ContratoId, p.Numero, p.IsEntrada,
                p.Vencimento, p.ValorOriginal,
                p.DataPagamento, p.ValorPago, p.Observacao, p.Status,
                p.LancamentoFinanceiroId,
                juros, valorAtual, dias
            );
        });

        return Ok(new ParcelasContratoDto(id, items));
    }

    [HttpPost("contratos/{id:guid}/extrato/pdf")]
    public async Task<IActionResult> GetExtrato(Guid id, [FromBody] ExtratoPdfRequestDto? dto, CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var contratoExiste = await db.ContratosHonorarios
            .AnyAsync(x => x.Id == id && x.TenantId == tenantId && x.ContatoId == contatoId, ct);
        if (!contratoExiste) return NotFound();

        try
        {
            var svc = HttpContext.RequestServices.GetRequiredService<IHonorarioService>();
            var dados = await svc.ObterDadosExtratoAsync(id, tenantId, dto, ct);
            var bytes = LegalManager.API.Reports.ExtratoHonorarioPdfRenderer.Renderizar(dados);
            return File(bytes, "application/pdf", $"extrato-honorarios-{DateTime.Now:yyyyMMdd}.pdf");
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
