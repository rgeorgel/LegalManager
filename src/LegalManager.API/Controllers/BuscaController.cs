using System.Globalization;
using LegalManager.Application.DTOs.Busca;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Busca global (Ctrl/⌘ + K em todas as telas e caixa de busca do dashboard): procura o termo em
// processos, contatos, tarefas, eventos, documentos e contratos de honorários do escritório e
// devolve poucos itens de cada tipo, já com a URL do detalhe. As páginas do menu são filtradas
// no próprio frontend (js/busca-global.js).
[ApiController]
[Route("api/busca")]
[Authorize]
public class BuscaController(AppDbContext context, ITenantContext tenantContext) : ControllerBase
{
    private const int TamanhoMinimo = 2;
    private const int LimitePadrao = 5;
    private const int LimiteMaximo = 10;

    [HttpGet]
    public async Task<ActionResult<BuscaGlobalResultadoDto>> Buscar(
        [FromQuery] string? q, [FromQuery] int? limite, CancellationToken ct)
    {
        var termo = (q ?? string.Empty).Trim();
        if (termo.Length < TamanhoMinimo)
            return Ok(new BuscaGlobalResultadoDto(termo, []));
        if (termo.Length > 100) termo = termo[..100];

        var n = Math.Clamp(limite ?? LimitePadrao, 1, LimiteMaximo);
        var busca = termo.ToLower();
        // CNJ e CPF/CNPJ costumam ser digitados sem pontuação: compara só os dígitos.
        var digitos = new string(termo.Where(char.IsDigit).ToArray());
        var porDigitos = digitos.Length >= 4;
        var tenantId = tenantContext.TenantId;

        var itens = new List<BuscaGlobalItemDto>();
        itens.AddRange(await ProcessosAsync(tenantId, busca, digitos, porDigitos, n, ct));
        itens.AddRange(await ContatosAsync(tenantId, busca, digitos, porDigitos, n, ct));
        itens.AddRange(await TarefasAsync(tenantId, busca, n, ct));
        itens.AddRange(await EventosAsync(tenantId, busca, n, ct));
        itens.AddRange(await DocumentosAsync(tenantId, busca, n, ct));
        if (PlanoRestricoes.PermiteHonorariosContratos(tenantContext.Plano))
            itens.AddRange(await ContratosAsync(tenantId, busca, n, ct));

        return Ok(new BuscaGlobalResultadoDto(termo, itens));
    }

    private async Task<IEnumerable<BuscaGlobalItemDto>> ProcessosAsync(
        Guid tenantId, string busca, string digitos, bool porDigitos, int n, CancellationToken ct)
    {
        var lista = await context.Processos
            .Where(p => p.TenantId == tenantId)
            .Where(p =>
                p.NumeroCNJ.Contains(busca) ||
                (porDigitos && p.NumeroCNJ.Replace(".", "").Replace("-", "").Contains(digitos)) ||
                (p.TipoAcao != null && p.TipoAcao.ToLower().Contains(busca)) ||
                (p.Classe != null && p.Classe.ToLower().Contains(busca)) ||
                (p.Vara != null && p.Vara.ToLower().Contains(busca)) ||
                (p.Comarca != null && p.Comarca.ToLower().Contains(busca)) ||
                p.Partes.Any(pt => pt.Contato.Nome.ToLower().Contains(busca)))
            .OrderBy(p => p.Status).ThenByDescending(p => p.CriadoEm)
            .Take(n)
            .Select(p => new
            {
                p.Id, p.NumeroCNJ, p.Status, p.TipoAcao, p.Classe, p.Vara, p.Comarca,
                Partes = p.Partes.OrderBy(pt => pt.Contato.Nome).Select(pt => pt.Contato.Nome).Take(2).ToList(),
            })
            .ToListAsync(ct);

        return lista.Select(p => new BuscaGlobalItemDto(
            "processo", p.Id, p.NumeroCNJ,
            Juntar(p.Partes.Count > 0 ? string.Join(" × ", p.Partes) : null,
                   p.TipoAcao ?? p.Classe, p.Vara ?? p.Comarca,
                   p.Status == StatusProcesso.Ativo ? null : p.Status.ToString()),
            $"/pages/processo-detalhe.html?id={p.Id}",
            Inativo: p.Status is StatusProcesso.Arquivado or StatusProcesso.Encerrado));
    }

    private async Task<IEnumerable<BuscaGlobalItemDto>> ContatosAsync(
        Guid tenantId, string busca, string digitos, bool porDigitos, int n, CancellationToken ct)
    {
        var lista = await context.Contatos
            .Where(c => c.TenantId == tenantId)
            .Where(c =>
                c.Nome.ToLower().Contains(busca) ||
                (c.Email != null && c.Email.ToLower().Contains(busca)) ||
                (c.Oab != null && c.Oab.ToLower().Contains(busca)) ||
                (porDigitos && c.CpfCnpj != null &&
                    c.CpfCnpj.Replace(".", "").Replace("-", "").Replace("/", "").Contains(digitos)))
            .OrderByDescending(c => c.Ativo).ThenBy(c => c.Nome)
            .Take(n)
            .Select(c => new { c.Id, c.Nome, c.TipoContato, c.CpfCnpj, c.Email, c.Telefone, c.Ativo })
            .ToListAsync(ct);

        return lista.Select(c => new BuscaGlobalItemDto(
            "contato", c.Id, c.Nome,
            Juntar(RotuloTipoContato(c.TipoContato), c.CpfCnpj, c.Email ?? c.Telefone, c.Ativo ? null : "Inativo"),
            $"/pages/contato-detalhe.html?id={c.Id}",
            Inativo: !c.Ativo));
    }

    private async Task<IEnumerable<BuscaGlobalItemDto>> TarefasAsync(Guid tenantId, string busca, int n, CancellationToken ct)
    {
        var lista = await context.Tarefas
            .Where(t => t.TenantId == tenantId)
            .Where(t => t.Titulo.ToLower().Contains(busca))
            // Abertas primeiro (pendente/em andamento), e dentro delas o prazo mais próximo.
            .OrderBy(t => t.Status == StatusTarefa.Pendente || t.Status == StatusTarefa.EmAndamento ? 0 : 1)
            .ThenBy(t => t.Prazo == null).ThenBy(t => t.Prazo)
            .Take(n)
            .Select(t => new
            {
                t.Id, t.Titulo, t.Tipo, t.Status, t.Prazo,
                Processo = t.Processo != null ? t.Processo.NumeroCNJ : null,
                Contato = t.Contato != null ? t.Contato.Nome : null,
            })
            .ToListAsync(ct);

        return lista.Select(t => new BuscaGlobalItemDto(
            "tarefa", t.Id, t.Titulo,
            Juntar(t.Tipo == TipoTarefa.Prazo ? "Prazo" : "Tarefa", RotuloStatusTarefa(t.Status), t.Processo ?? t.Contato),
            $"/pages/tarefas.html?abrirId={t.Id}",
            t.Prazo,
            Inativo: t.Status is not (StatusTarefa.Pendente or StatusTarefa.EmAndamento)));
    }

    private async Task<IEnumerable<BuscaGlobalItemDto>> EventosAsync(Guid tenantId, string busca, int n, CancellationToken ct)
    {
        var lista = await context.Eventos
            .Where(e => e.TenantId == tenantId)
            .Where(e => e.Titulo.ToLower().Contains(busca) || (e.Local != null && e.Local.ToLower().Contains(busca)))
            .OrderByDescending(e => e.DataHora)
            .Take(n)
            .Select(e => new { e.Id, e.Titulo, e.Tipo, e.DataHora, e.Local, Processo = e.Processo != null ? e.Processo.NumeroCNJ : null })
            .ToListAsync(ct);

        // A agenda só acha o item na visão carregada: abre o dia do evento.
        return lista.Select(e => new BuscaGlobalItemDto(
            "evento", e.Id, e.Titulo,
            Juntar(RotuloTipoEvento(e.Tipo), e.Local, e.Processo),
            $"/pages/agenda.html?view=dia&date={e.DataHora.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&abrirId={e.Id}",
            e.DataHora));
    }

    private async Task<IEnumerable<BuscaGlobalItemDto>> DocumentosAsync(Guid tenantId, string busca, int n, CancellationToken ct)
    {
        var lista = await context.Documentos
            .Where(d => d.TenantId == tenantId)
            .Where(d => d.Nome.ToLower().Contains(busca) || (d.Observacoes != null && d.Observacoes.ToLower().Contains(busca)))
            .OrderByDescending(d => d.CriadoEm)
            .Take(n)
            .Select(d => new { d.Id, d.Nome, d.Tipo, d.PastaId, Processo = d.Processo != null ? d.Processo.NumeroCNJ : null })
            .ToListAsync(ct);

        return lista.Select(d => new BuscaGlobalItemDto(
            "documento", d.Id, d.Nome,
            Juntar(RotuloTipoDocumento(d.Tipo), d.Processo),
            d.PastaId is { } pasta
                ? $"/pages/documentos.html?doc={d.Id}&pasta={pasta}"
                : $"/pages/documentos.html?doc={d.Id}"));
    }

    private async Task<IEnumerable<BuscaGlobalItemDto>> ContratosAsync(Guid tenantId, string busca, int n, CancellationToken ct)
    {
        var lista = await context.ContratosHonorarios
            .Where(c => c.TenantId == tenantId)
            .Where(c =>
                c.NumeroContrato.ToLower().Contains(busca) ||
                (c.Objeto != null && c.Objeto.ToLower().Contains(busca)) ||
                c.Contato.Nome.ToLower().Contains(busca))
            .OrderBy(c => c.Status).ThenByDescending(c => c.DataInicio)
            .Take(n)
            .Select(c => new { c.Id, c.NumeroContrato, c.Objeto, c.Status, Cliente = c.Contato.Nome })
            .ToListAsync(ct);

        return lista.Select(c => new BuscaGlobalItemDto(
            "contrato", c.Id,
            string.IsNullOrWhiteSpace(c.Objeto) ? $"Contrato {c.NumeroContrato}" : c.Objeto!,
            Juntar(c.Cliente, $"nº {c.NumeroContrato}", c.Status == StatusContratoHonorario.Ativo ? null : c.Status.ToString()),
            $"/pages/honorarios-contrato-detalhe.html?id={c.Id}",
            Inativo: c.Status is StatusContratoHonorario.Encerrado or StatusContratoHonorario.Distratado
                or StatusContratoHonorario.Quitado));
    }

    private static string? Juntar(params string?[] partes)
    {
        var texto = string.Join(" · ", partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return texto.Length == 0 ? null : texto;
    }

    private static string RotuloTipoContato(TipoContato tipo) => tipo switch
    {
        TipoContato.Cliente => "Cliente",
        TipoContato.ParteContraria => "Parte contrária",
        TipoContato.Testemunha => "Testemunha",
        TipoContato.Perito => "Perito",
        _ => "Contato",
    };

    private static string RotuloStatusTarefa(StatusTarefa status) => status switch
    {
        StatusTarefa.Pendente => "Pendente",
        StatusTarefa.EmAndamento => "Em andamento",
        StatusTarefa.Concluida => "Concluída",
        StatusTarefa.Cancelada => "Cancelada",
        StatusTarefa.Perdida => "Perdida",
        StatusTarefa.Suspensa => "Suspensa",
        _ => status.ToString(),
    };

    private static string RotuloTipoEvento(TipoEvento tipo) => tipo switch
    {
        TipoEvento.Audiencia => "Audiência",
        TipoEvento.Reuniao => "Reunião",
        TipoEvento.Pericia => "Perícia",
        TipoEvento.Prazo => "Prazo",
        TipoEvento.Despacho => "Despacho",
        _ => "Evento",
    };

    private static string RotuloTipoDocumento(TipoDocumento tipo) => tipo switch
    {
        TipoDocumento.Peticao => "Petição",
        TipoDocumento.Decisao => "Decisão",
        TipoDocumento.Contrato => "Contrato",
        TipoDocumento.Prova => "Prova",
        TipoDocumento.Modelo => "Modelo",
        _ => "Documento",
    };
}
