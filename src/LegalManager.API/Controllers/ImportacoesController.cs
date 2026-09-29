using Hangfire;
using LegalManager.Application.DTOs.Importacoes;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>
/// Importação de processos por OAB em background (onboarding do dashboard e botão
/// "Importar por OAB" da tela de processos). O frontend acompanha o progresso por polling.
/// </summary>
[ApiController]
[Route("api/importacoes")]
[Authorize]
public class ImportacoesController(
    AppDbContext db,
    ITenantContext tenantContext,
    IBackgroundJobClient jobs) : ControllerBase
{
    private static readonly StatusImportacao[] StatusAtivos =
        [StatusImportacao.Pendente, StatusImportacao.Buscando, StatusImportacao.Importando];

    [HttpPost]
    public async Task<ActionResult<ImportacaoResumoDto>> Iniciar(IniciarImportacaoDto dto, CancellationToken ct)
    {
        var numero = new string(dto.NumeroOAB.Where(char.IsDigit).ToArray());
        var uf = dto.Uf.Trim().ToUpperInvariant();
        if (numero.Length == 0 || uf.Length != 2)
            return BadRequest(new { message = "Informe o número da OAB e a UF." });

        var ativa = await BuscarAtivaAsync(ct);
        if (ativa != null)
            return Conflict(new { message = "Já existe uma importação em andamento. Acompanhe pela barra na parte inferior da tela.", id = ativa.Id });

        var agora = DateTime.UtcNow;
        var importacao = new ImportacaoProcessos
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.TenantId,
            UsuarioId = tenantContext.UserId,
            Modo = dto.Processos is { Count: > 0 } ? ModoImportacao.Selecionados : ModoImportacao.Todos,
            NumeroOab = numero,
            Uf = uf,
            Status = StatusImportacao.Pendente,
            CriadoEm = agora,
            AtualizadoEm = agora
        };

        if (importacao.Modo == ModoImportacao.Selecionados)
        {
            var selecionados = dto.Processos!
                .Where(p => !string.IsNullOrWhiteSpace(p.NumeroCNJ))
                .GroupBy(p => p.NumeroCNJ.Trim())
                .Select(g => g.First())
                .ToList();
            for (var i = 0; i < selecionados.Count; i++)
                importacao.Itens.Add(ImportacaoProcessosJob.NovoItem(importacao.Id, i, selecionados[i]));
            importacao.Total = selecionados.Count;
        }

        db.ImportacoesProcessos.Add(importacao);

        // Registrada já no início (não no fim), para a OAB não ser importada de novo
        // enquanto esta importação ainda roda.
        var usuario = await db.Users.FirstOrDefaultAsync(u => u.Id == tenantContext.UserId, ct);
        if (usuario != null && usuario.OabImportadaNumero == null)
        {
            usuario.OabImportadaNumero = numero;
            usuario.OabImportadaUf = uf;
        }

        await db.SaveChangesAsync(ct);

        jobs.Enqueue<ImportacaoProcessosJob>(j => j.ExecutarAsync(importacao.Id, CancellationToken.None));

        return Accepted(ToDto(importacao));
    }

    /// <summary>Importação em andamento do usuário (204 se não houver) — consultada pela barra de progresso.</summary>
    [HttpGet("ativa")]
    public async Task<ActionResult<ImportacaoResumoDto>> Ativa(CancellationToken ct)
    {
        var ativa = await BuscarAtivaAsync(ct);
        return ativa == null ? NoContent() : Ok(ToDto(ativa));
    }

    [HttpGet]
    public async Task<ActionResult<List<ImportacaoResumoDto>>> Listar(CancellationToken ct)
    {
        var lista = await DoUsuario()
            .OrderByDescending(i => i.CriadoEm)
            .Take(20)
            .ToListAsync(ct);
        return Ok(lista.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ImportacaoResumoDto>> Obter(Guid id, CancellationToken ct)
    {
        var importacao = await DoUsuario().FirstOrDefaultAsync(i => i.Id == id, ct);
        return importacao == null ? NotFound() : Ok(ToDto(importacao));
    }

    [HttpGet("{id:guid}/itens")]
    public async Task<ActionResult<List<ImportacaoItemDto>>> Itens(Guid id, CancellationToken ct)
    {
        if (!await DoUsuario().AnyAsync(i => i.Id == id, ct))
            return NotFound();

        var itens = await db.ImportacaoProcessoItens
            .Where(i => i.ImportacaoId == id)
            .OrderBy(i => i.Ordem)
            .Select(i => new ImportacaoItemDto(i.Id, i.NumeroCNJ, i.Tribunal, i.Status, i.Mensagem, i.ProcessoId))
            .ToListAsync(ct);
        return Ok(itens);
    }

    private IQueryable<ImportacaoProcessos> DoUsuario() =>
        db.ImportacoesProcessos.Where(i => i.TenantId == tenantContext.TenantId && i.UsuarioId == tenantContext.UserId);

    /// <summary>
    /// Importação ativa do usuário. Uma importação sem progresso há muito tempo teve o job
    /// interrompido (deploy, crash): é marcada como erro para não bloquear a barra e novas importações.
    /// </summary>
    private async Task<ImportacaoProcessos?> BuscarAtivaAsync(CancellationToken ct)
    {
        var ativa = await DoUsuario()
            .Where(i => StatusAtivos.Contains(i.Status))
            .OrderByDescending(i => i.CriadoEm)
            .FirstOrDefaultAsync(ct);
        if (ativa == null) return null;

        if (ativa.AtualizadoEm < DateTime.UtcNow - ImportacaoProcessosJob.TempoMaximoSemProgresso)
        {
            ativa.Status = StatusImportacao.Erro;
            ativa.MensagemErro = "A importação foi interrompida. Tente novamente.";
            ativa.ConcluidoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return null;
        }
        return ativa;
    }

    private static ImportacaoResumoDto ToDto(ImportacaoProcessos i) => new(
        i.Id, i.Modo, i.NumeroOab, i.Uf, i.Status,
        i.Total, i.Processados, i.Importados, i.JaCadastrados, i.Erros,
        i.MensagemErro, i.CriadoEm, i.ConcluidoEm);
}
