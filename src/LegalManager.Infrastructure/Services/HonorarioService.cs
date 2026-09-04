using System.Globalization;
using LegalManager.Application.DTOs.Honorarios;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class HonorarioService(AppDbContext db, IAuditService audit) : IHonorarioService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<DashboardHonorariosDto> GetDashboardAsync(Guid tenantId, CancellationToken ct = default)
    {
        var hoje = DateTime.UtcNow.Date;
        var contratos = await db.ContratosHonorarios
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .Include(c => c.Contato)
            .Include(c => c.Parcelas)
            .ToListAsync(ct);

        var inadimplentes = new List<InadimplenteResumoDto>();
        decimal totalPendente = 0, totalAtraso = 0, recebidoMes = 0;
        int contratosAtrasados = 0;

        var mes = hoje.Month;
        var ano = hoje.Year;

        foreach (var c in contratos)
        {
            decimal pago = 0, pendente = 0, atraso = 0;
            int atrasoCount = 0;
            foreach (var p in c.Parcelas.Where(p => p.Status != StatusParcelaHonorario.Cancelado))
            {
                if (p.Status == StatusParcelaHonorario.Pago)
                {
                    pago += p.ValorPago ?? p.ValorOriginal;
                    if (p.DataPagamento.HasValue && p.DataPagamento.Value.Month == mes && p.DataPagamento.Value.Year == ano)
                        recebidoMes += p.ValorPago ?? p.ValorOriginal;
                }
                else
                {
                    var dias = (hoje - p.Vencimento.Date).Days;
                    if (dias > 0)
                    {
                        var j = CalcularJuros(p.ValorOriginal, p.Vencimento, hoje);
                        atraso += j.total;
                        atrasoCount++;
                    }
                    else
                    {
                        pendente += p.ValorOriginal;
                    }
                }
            }

            var contratoEncerrado = c.Status == StatusContratoHonorario.Encerrado
                || c.Status == StatusContratoHonorario.Distratado;

            if (atrasoCount > 0 && !contratoEncerrado)
            {
                contratosAtrasados++;
                inadimplentes.Add(new InadimplenteResumoDto(
                    c.Id,
                    c.Contato?.Nome ?? "(sem nome)",
                    atraso,
                    atrasoCount,
                    c.Contato?.Telefone,
                    c.Contato?.Email
                ));
            }

            if (!contratoEncerrado)
            {
                totalPendente += pendente;
                totalAtraso += atraso;
            }
        }

        var evolucao = await CalcularEvolucao6MesesAsync(tenantId, ct);

        var config = await db.ConfiguracoesHonorarios.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        decimal? meta = config != null && config.MetaMensalPadrao > 0 ? config.MetaMensalPadrao : null;

        return new DashboardHonorariosDto(
            TotalAReceber: totalPendente + totalAtraso,
            TotalEmAtraso: totalAtraso,
            RecebidoNoMes: recebidoMes,
            ContratosAtrasados: contratosAtrasados,
            ContratosAtivos: contratos.Count(c => c.Status == StatusContratoHonorario.Ativo || c.Status == StatusContratoHonorario.Inadimplente),
            ContratosQuitados: contratos.Count(c => c.Status == StatusContratoHonorario.Quitado),
            Inadimplentes: inadimplentes.OrderByDescending(i => i.ValorEmAtraso).Take(10),
            Evolucao6Meses: evolucao,
            MetaMensal: meta,
            AlcancadoMes: recebidoMes
        );
    }

    private async Task<List<EvolucaoMensalDto>> CalcularEvolucao6MesesAsync(Guid tenantId, CancellationToken ct)
    {
        var nomes = new[] { "Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez" };
        var now = DateTime.UtcNow;
        var buckets = new List<(int Ano, int Mes, string Label)>();
        for (int i = 5; i >= 0; i--)
        {
            var d = new DateTime(now.Year, now.Month - i, 1);
            buckets.Add((d.Year, d.Month, nomes[d.Month - 1]));
        }

        var primeiroMes = buckets.First();
        var ultMes = buckets.Last();
        var pagos = await db.ParcelasHonorarios
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId
                && p.Status == StatusParcelaHonorario.Pago
                && p.DataPagamento != null
                && p.DataPagamento.Value >= new DateTime(primeiroMes.Ano, primeiroMes.Mes, 1)
                && p.DataPagamento.Value < new DateTime(ultMes.Ano, ultMes.Mes, 1).AddMonths(1))
            .Select(p => new { p.DataPagamento, p.ValorPago, p.ValorOriginal })
            .ToListAsync(ct);

        return buckets.Select(b => new EvolucaoMensalDto(
            b.Ano, b.Mes, b.Label,
            pagos.Where(x => x.DataPagamento!.Value.Year == b.Ano && x.DataPagamento.Value.Month == b.Mes)
                 .Sum(x => x.ValorPago ?? x.ValorOriginal)
        )).ToList();
    }

    public async Task<ContratosPagedDto> ListarAsync(Guid tenantId, FiltroContratoHonorario filtro, CancellationToken ct = default)
    {
        var q = db.ContratosHonorarios
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId);

        if (!string.IsNullOrEmpty(filtro.Status) && Enum.TryParse<StatusContratoHonorario>(filtro.Status, true, out var st))
            q = q.Where(c => c.Status == st);
        if (filtro.ContatoId.HasValue) q = q.Where(c => c.ContatoId == filtro.ContatoId.Value);
        if (filtro.ProcessoId.HasValue) q = q.Where(c => c.ProcessoId == filtro.ProcessoId.Value);

        var dados = await q
            .Include(c => c.Contato)
            .Include(c => c.Processo)
            .Include(c => c.Parcelas)
            .OrderByDescending(c => c.CriadoEm)
            .ToListAsync(ct);

        if (!string.IsNullOrEmpty(filtro.Busca))
        {
            var b = filtro.Busca.ToLower();
            dados = dados.Where(c =>
                (c.Contato != null && c.Contato.Nome.ToLower().Contains(b)) ||
                (c.NumeroContrato != null && c.NumeroContrato.ToLower().Contains(b)) ||
                (c.Objeto != null && c.Objeto.ToLower().Contains(b))
            ).ToList();
        }

        var total = dados.Count;
        var items = dados
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .Select(c => MapearContrato(c, incluirFinanceiro: true))
            .ToList();

        return new ContratosPagedDto(items, total);
    }

    public async Task<ContratoHonorarioDto?> ObterAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios
            .AsNoTracking()
            .Include(x => x.Contato)
            .Include(x => x.Processo)
            .Include(x => x.Parcelas)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        return c == null ? null : MapearContrato(c, incluirFinanceiro: true);
    }

    public async Task<ContratoHonorarioDto> CriarAsync(Guid tenantId, Guid usuarioId, CriarContratoHonorarioDto dto, CancellationToken ct = default)
    {
        await ValidarContatoAsync(tenantId, dto.ContatoId, dto.ProcessoId, ct);

        if (dto.ValorTotal <= 0)
            throw new ArgumentException("Valor total deve ser maior que zero.");

        ValidarFormaPagamento(dto);

        var numeroContrato = await GerarNumeroContratoAsync(tenantId, ct);

        var c = new ContratoHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContatoId = dto.ContatoId,
            ProcessoId = dto.ProcessoId,
            NumeroContrato = dto.NumeroContrato ?? numeroContrato,
            Objeto = dto.Objeto,
            ValorTotal = dto.ValorTotal,
            FormaPagamento = dto.FormaPagamento,
            Periodicidade = dto.Periodicidade,
            NumeroParcelas = dto.NumeroParcelas,
            DataPrimeiraParcela = dto.DataPrimeiraParcela,
            ValorEntrada = dto.ValorEntrada,
            VencimentoEntrada = dto.VencimentoEntrada,
            PercentualMulta = dto.PercentualMulta ?? 0.02m,
            PercentualJurosMensal = dto.PercentualJurosMensal ?? 0.015m,
            TipoCobranca = dto.TipoCobranca,
            Observacoes = dto.Observacoes,
            Status = StatusContratoHonorario.Ativo,
            CriadoPorId = usuarioId,
            CriadoEm = DateTime.UtcNow,
            DataInicio = dto.DataInicio,
            DataFim = dto.DataFim
        };

        c.Parcelas = GerarParcelas(c);

        db.ContratosHonorarios.Add(c);
        await SalvarComHistorico(c, EventoContratoHonorario.Criado, "Contrato criado.", usuarioId, null, SnapshotParaHistorico(c), ct);
        return (await ObterAsync(c.Id, tenantId, ct))!;
    }

    public async Task<ContratoHonorarioDto> AtualizarAsync(Guid id, Guid tenantId, Guid usuarioId, AtualizarContratoHonorarioDto dto, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios
            .Include(x => x.Parcelas)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contrato não encontrado.");

        await ValidarContatoAsync(tenantId, dto.ContatoId, dto.ProcessoId, ct);

        if (c.Status == StatusContratoHonorario.Distratado || c.Status == StatusContratoHonorario.Encerrado)
            throw new InvalidOperationException("Contrato distratado/encerrado não pode ser editado.");

        var antes = SnapshotParaHistorico(c);

        // Snapshot pagamentos preservados antes de qualquer mutação
        var pagamentosPreservados = c.Parcelas
            .Where(p => p.Status == StatusParcelaHonorario.Pago)
            .OrderBy(p => p.Numero)
            .Select(p => new
            {
                p.Numero,
                p.DataPagamento,
                p.ValorPago,
                p.LancamentoFinanceiroId,
                p.Observacao
            })
            .ToList();

        // Atualizar dados do contrato
        c.ContatoId = dto.ContatoId;
        c.ProcessoId = dto.ProcessoId;
        c.Objeto = dto.Objeto;
        c.ValorTotal = dto.ValorTotal;
        c.FormaPagamento = dto.FormaPagamento;
        c.Periodicidade = dto.Periodicidade;
        c.NumeroParcelas = dto.NumeroParcelas;
        c.DataPrimeiraParcela = dto.DataPrimeiraParcela;
        c.ValorEntrada = dto.ValorEntrada;
        c.VencimentoEntrada = dto.VencimentoEntrada;
        c.PercentualMulta = dto.PercentualMulta ?? c.PercentualMulta;
        c.PercentualJurosMensal = dto.PercentualJurosMensal ?? c.PercentualJurosMensal;
        c.TipoCobranca = dto.TipoCobranca;
        c.Observacoes = dto.Observacoes;
        c.DataInicio = dto.DataInicio;
        c.DataFim = dto.DataFim;
        c.AtualizadoEm = DateTime.UtcNow;

        // Gerar novas parcelas
        var novasParcelas = GerarParcelas(c);

        // Preservar pagamentos por número
        foreach (var p in novasParcelas.Where(p => !p.IsEntrada))
        {
            var antigo = pagamentosPreservados.FirstOrDefault(x => x.Numero == p.Numero);
            if (antigo != null)
            {
                p.DataPagamento = antigo.DataPagamento;
                p.ValorPago = antigo.ValorPago;
                p.Status = StatusParcelaHonorario.Pago;
                p.LancamentoFinanceiroId = antigo.LancamentoFinanceiroId;
                p.Observacao = antigo.Observacao;
            }
        }

        // Recalcular status baseado nas novas parcelas (calcula sem mutar o c)
        var novoStatus = CalcularStatusBaseadoEmParcelas(novasParcelas, c.Status);

        // Limpar o ChangeTracker para evitar conflitos entre remove/add de parcelas
        db.ChangeTracker.Clear();

        // Recarregar c limpo
        c = await db.ContratosHonorarios.FirstAsync(x => x.Id == id && x.TenantId == tenantId, ct);

        // Estratégia híbrida:
        // 1. SQL para remover parcelas antigas (evita bug do EF tracker)
        // 2. SQL para desvincular lançamentos financeiros (preserva histórico)
        // 3. EF para inserir novas parcelas + atualizar contrato (já carregado, sem navegação)
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"LancamentosFinanceiros\" WHERE \"ParcelaHonorarioId\" IS NOT NULL AND \"ParcelaHonorarioId\" IN (SELECT \"Id\" FROM \"ParcelasHonorarios\" WHERE \"ContratoId\" = @p0)",
            new object[] { id });
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"ParcelasHonorarios\" WHERE \"ContratoId\" = @p0", id);

        // Atualizar contrato via propriedades tracked
        c.ContatoId = dto.ContatoId;
        c.ProcessoId = dto.ProcessoId;
        c.Objeto = dto.Objeto;
        c.ValorTotal = dto.ValorTotal;
        c.FormaPagamento = dto.FormaPagamento;
        c.Periodicidade = dto.Periodicidade;
        c.NumeroParcelas = dto.NumeroParcelas;
        c.DataPrimeiraParcela = dto.DataPrimeiraParcela;
        c.ValorEntrada = dto.ValorEntrada;
        c.VencimentoEntrada = dto.VencimentoEntrada;
        c.PercentualMulta = dto.PercentualMulta ?? c.PercentualMulta;
        c.PercentualJurosMensal = dto.PercentualJurosMensal ?? c.PercentualJurosMensal;
        c.TipoCobranca = dto.TipoCobranca;
        c.Observacoes = dto.Observacoes;
        c.DataInicio = dto.DataInicio;
        c.DataFim = dto.DataFim;
        c.Status = novoStatus;
        c.AtualizadoEm = DateTime.UtcNow;

        await db.ParcelasHonorarios.AddRangeAsync(novasParcelas, ct);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(new AuditLogEntry(
            c.TenantId, usuarioId,
            AuditActions.Update, AuditEntities.ContratoHonorario,
            c.Id.ToString(), antes, SnapshotParaHistorico(c)
        ), ct);

        return (await ObterAsync(c.Id, tenantId, ct))!;
    }

    private static StatusContratoHonorario CalcularStatusBaseadoEmParcelas(IEnumerable<ParcelaHonorario> parcelas, StatusContratoHonorario statusAtual)
    {
        var lista = parcelas.Where(p => p.Status != StatusParcelaHonorario.Cancelado).ToList();
        if (lista.Count == 0) return statusAtual;

        if (statusAtual == StatusContratoHonorario.Distratado || statusAtual == StatusContratoHonorario.Encerrado)
            return statusAtual;

        var todasPagas = lista.All(p => p.Status == StatusParcelaHonorario.Pago);
        var temVencida = lista.Any(p => p.Status != StatusParcelaHonorario.Pago && p.Vencimento.Date < DateTime.UtcNow.Date);

        if (todasPagas) return StatusContratoHonorario.Quitado;
        if (temVencida) return StatusContratoHonorario.Inadimplente;
        return StatusContratoHonorario.Ativo;
    }

    public async Task ExcluirAsync(Guid id, Guid tenantId, Guid usuarioId, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios
            .Include(x => x.Parcelas)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contrato não encontrado.");

        c.Status = StatusContratoHonorario.Encerrado;
        c.AtualizadoEm = DateTime.UtcNow;
        db.HistoricosContratoHonorario.Add(new HistoricoContratoHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContratoId = c.Id,
            TipoEvento = EventoContratoHonorario.Distratado,
            Descricao = "Contrato encerrado/excluído.",
            UsuarioId = usuarioId == Guid.Empty ? null : usuarioId,
            CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ParcelasContratoDto> ListarParcelasAsync(Guid contratoId, Guid tenantId, CancellationToken ct = default)
    {
        var existe = await db.ContratosHonorarios.AnyAsync(x => x.Id == contratoId && x.TenantId == tenantId, ct);
        if (!existe) throw new KeyNotFoundException("Contrato não encontrado.");

        var parcelas = await db.ParcelasHonorarios
            .AsNoTracking()
            .Where(p => p.ContratoId == contratoId && p.TenantId == tenantId)
            .OrderBy(p => p.Numero).ThenBy(p => p.Vencimento)
            .ToListAsync(ct);

        return new ParcelasContratoDto(contratoId, parcelas.Select(MapearParcela));
    }

    public async Task<ParcelaHonorarioDto> QuitarParcelaAsync(Guid contratoId, Guid parcelaId, Guid tenantId, QuitarParcelaDto dto, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios
            .Include(x => x.Parcelas)
            .Include(x => x.Contato)
            .Include(x => x.Processo)
            .FirstOrDefaultAsync(x => x.Id == contratoId && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contrato não encontrado.");

        var p = c.Parcelas.FirstOrDefault(x => x.Id == parcelaId)
            ?? throw new KeyNotFoundException("Parcela não encontrada.");

        if (p.Status == StatusParcelaHonorario.Pago) return MapearParcela(p);
        if (p.Status == StatusParcelaHonorario.Cancelado)
            throw new InvalidOperationException("Parcela cancelada não pode ser paga.");

        p.DataPagamento = dto.DataPagamento;
        p.ValorPago = dto.ValorPago;
        p.Observacao = dto.Observacao;
        p.Status = StatusParcelaHonorario.Pago;

        // Reutiliza Lancamento pré-existente (criado manualmente via UI do Financeiro)
        // para evitar duplicação. Critérios: mesmo tenant, sem ParcelaHonorarioId,
        // Pendente, Honorário, mesmo vencimento e valor próximo (tolerância R$ 1).
        var dataVenc = p.Vencimento.Date;
        var candidatos = await db.LancamentosFinanceiros
            .Where(l => l.TenantId == tenantId
                && l.ParcelaHonorarioId == null
                && l.Status == StatusLancamento.Pendente
                && l.Categoria == CategoriaLancamento.Honorario
                && l.DataVencimento.Date == dataVenc
                && l.Valor >= dto.ValorPago - 1.0m
                && l.Valor <= dto.ValorPago + 1.0m)
            .ToListAsync(ct);
        var lancamentoExistente = candidatos
            .OrderBy(l => Math.Abs(l.Valor - dto.ValorPago))
            .FirstOrDefault();

        LancamentoFinanceiro lancamento;
        if (lancamentoExistente != null)
        {
            lancamentoExistente.Status = StatusLancamento.Pago;
            lancamentoExistente.DataPagamento = dto.DataPagamento;
            lancamentoExistente.Valor = dto.ValorPago;
            lancamentoExistente.ParcelaHonorarioId = p.Id;
            lancamentoExistente.Descricao = $"Parcela {(p.IsEntrada ? "Entrada" : $"{p.Numero}/{c.NumeroParcelas}")} - {c.NumeroContrato}" + (c.Objeto != null ? $" - {Truncar(c.Objeto, 60)}" : "");
            lancamento = lancamentoExistente;
        }
        else
        {
            lancamento = new LancamentoFinanceiro
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Tipo = TipoLancamento.Receita,
                Categoria = CategoriaLancamento.Honorario,
                Valor = dto.ValorPago,
                Descricao = $"Parcela {(p.IsEntrada ? "Entrada" : $"{p.Numero}/{c.NumeroParcelas}")} - {c.NumeroContrato}" + (c.Objeto != null ? $" - {Truncar(c.Objeto, 60)}" : ""),
                DataVencimento = p.Vencimento,
                DataPagamento = dto.DataPagamento,
                Status = StatusLancamento.Pago,
                ProcessoId = c.ProcessoId,
                ContatoId = c.ContatoId,
                ContratoHonorarioId = c.Id,
                ParcelaHonorarioId = p.Id,
                CriadoEm = DateTime.UtcNow
            };
            db.LancamentosFinanceiros.Add(lancamento);
        }
        p.LancamentoFinanceiroId = lancamento.Id;

        RecalcularStatus(c);

        await audit.LogAsync(new AuditLogEntry(
            tenantId, null, AuditActions.Update, AuditEntities.ParcelaHonorario,
            p.Id.ToString(),
            new { p.Status },
            new { p.DataPagamento, p.ValorPago, LancamentoId = lancamento.Id }
        ), ct);

        await db.SaveChangesAsync(ct);
        return MapearParcela(p);
    }

    public async Task<ParcelaHonorarioDto> CancelarParcelaAsync(Guid contratoId, Guid parcelaId, Guid tenantId, Guid usuarioId, string motivo, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios
            .Include(x => x.Parcelas)
            .FirstOrDefaultAsync(x => x.Id == contratoId && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contrato não encontrado.");

        var p = c.Parcelas.FirstOrDefault(x => x.Id == parcelaId)
            ?? throw new KeyNotFoundException("Parcela não encontrada.");

        if (p.Status == StatusParcelaHonorario.Pago)
            throw new InvalidOperationException("Parcela já paga — use estorno.");

        p.Status = StatusParcelaHonorario.Cancelado;
        p.Observacao = $"Cancelada: {motivo}".Trim();

        RecalcularStatus(c);
        db.HistoricosContratoHonorario.Add(new HistoricoContratoHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContratoId = c.Id,
            TipoEvento = EventoContratoHonorario.ParcelaCancelada,
            Descricao = $"Parcela {p.Numero} cancelada: {motivo}",
            UsuarioId = usuarioId == Guid.Empty ? null : usuarioId,
            CriadoEm = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return MapearParcela(p);
    }

    public async Task EstornarPagamentoParcelaAsync(Guid contratoId, Guid parcelaId, Guid tenantId, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios
            .Include(x => x.Parcelas)
            .FirstOrDefaultAsync(x => x.Id == contratoId && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contrato não encontrado.");

        var p = c.Parcelas.FirstOrDefault(x => x.Id == parcelaId)
            ?? throw new KeyNotFoundException("Parcela não encontrada.");

        if (p.Status != StatusParcelaHonorario.Pago) return;

        if (p.LancamentoFinanceiroId.HasValue)
        {
            var lanc = await db.LancamentosFinanceiros.FirstOrDefaultAsync(
                x => x.Id == p.LancamentoFinanceiroId.Value && x.TenantId == tenantId, ct);
            if (lanc != null) db.LancamentosFinanceiros.Remove(lanc);
        }

        p.Status = StatusParcelaHonorario.Pendente;
        p.DataPagamento = null;
        p.ValorPago = null;
        p.LancamentoFinanceiroId = null;

        RecalcularStatus(c);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<HistoricoContratoDto>> ListarHistoricoAsync(Guid contratoId, Guid tenantId, CancellationToken ct = default)
    {
        var itens = await db.HistoricosContratoHonorario
            .AsNoTracking()
            .Where(h => h.ContratoId == contratoId && h.TenantId == tenantId)
            .OrderByDescending(h => h.CriadoEm)
            .Join(db.Users, h => h.UsuarioId, u => u.Id, (h, u) => new { h, u })
            .Select(x => new HistoricoContratoDto(
                x.h.Id,
                x.h.ContratoId,
                x.h.TipoEvento,
                x.h.Descricao,
                x.u.Nome,
                x.h.CriadoEm
            ))
            .ToListAsync(ct);

        var semUsuario = await db.HistoricosContratoHonorario
            .AsNoTracking()
            .Where(h => h.ContratoId == contratoId && h.TenantId == tenantId && h.UsuarioId == null)
            .OrderByDescending(h => h.CriadoEm)
            .Select(h => new HistoricoContratoDto(
                h.Id, h.ContratoId, h.TipoEvento, h.Descricao, null, h.CriadoEm))
            .ToListAsync(ct);

        return itens.Concat(semUsuario).OrderByDescending(x => x.CriadoEm);
    }

    public async Task<ContratoHonorarioDto> SuspenderAsync(Guid id, Guid tenantId, Guid usuarioId, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contrato não encontrado.");
        c.Status = StatusContratoHonorario.Suspenso;
        c.AtualizadoEm = DateTime.UtcNow;
        await SalvarComHistorico(c, EventoContratoHonorario.Suspenso, "Contrato suspenso.", usuarioId, null, SnapshotParaHistorico(c), ct);
        return (await ObterAsync(id, tenantId, ct))!;
    }

    public async Task<ContratoHonorarioDto> ReativarAsync(Guid id, Guid tenantId, Guid usuarioId, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contrato não encontrado.");

        if (c.Status != StatusContratoHonorario.Suspenso && c.Status != StatusContratoHonorario.Distratado)
            throw new InvalidOperationException($"Só é possível reativar contratos Suspensos ou Distratados (status atual: {c.Status}).");

        var revertendoDistrato = c.Status == StatusContratoHonorario.Distratado;

        c.Status = StatusContratoHonorario.Ativo;
        c.AtualizadoEm = DateTime.UtcNow;

        // Limpar vestígios de distrato (se estiver revertendo distrato)
        if (revertendoDistrato)
        {
            c.DistratoEm = null;
            c.DistratoMotivo = null;
            c.DataFim = null;
        }

        RecalcularStatus(c);

        var descricao = revertendoDistrato
            ? "Distrato revertido. Contrato reativado."
            : "Contrato reativado.";

        await SalvarComHistorico(c, EventoContratoHonorario.Reativado, descricao, usuarioId, null, SnapshotParaHistorico(c), ct);
        return (await ObterAsync(id, tenantId, ct))!;
    }

    public async Task<ContratoHonorarioDto> DistratoAsync(Guid id, Guid tenantId, Guid usuarioId, string motivo, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contrato não encontrado.");
        c.Status = StatusContratoHonorario.Distratado;
        c.DistratoEm = DateTime.UtcNow;
        c.DistratoMotivo = motivo;
        c.DataFim = c.DataFim ?? DateTime.UtcNow;
        c.AtualizadoEm = DateTime.UtcNow;
        await SalvarComHistorico(c, EventoContratoHonorario.Distratado, $"Distrato: {motivo}", usuarioId, null, SnapshotParaHistorico(c), ct);
        return (await ObterAsync(id, tenantId, ct))!;
    }

    public async Task<ExtratoPdfDadosDto> ObterDadosExtratoAsync(Guid id, Guid tenantId, ExtratoPdfRequestDto? _, CancellationToken ct = default)
    {
        var c = await db.ContratosHonorarios
            .AsNoTracking()
            .Include(x => x.Contato)
            .Include(x => x.Processo)
            .Include(x => x.Parcelas)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Contrato não encontrado.");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        var config = await db.ConfiguracoesHonorarios.FirstOrDefaultAsync(cg => cg.TenantId == tenantId, ct);

        var nomeEscritorio = config?.NomeEscritorio ?? tenant?.Nome ?? "Escritório de Advocacia";
        var hoje = DateTime.UtcNow.Date;
        var totalParc = c.Parcelas.Count(x => !x.IsEntrada);

        var linhas = new List<ExtratoParcelaPdfDto>();
        decimal pago = 0, pendente = 0, atraso = 0;

        foreach (var p in c.Parcelas.OrderBy(x => x.IsEntrada ? 0 : 1).ThenBy(x => x.Numero))
        {
            var st = p.Status;
            decimal valorFinal = p.ValorOriginal;
            decimal? jurosMulta = null;
            if (st == StatusParcelaHonorario.Pago && p.ValorPago.HasValue)
            {
                valorFinal = p.ValorPago.Value;
                pago += valorFinal;
            }
            else if (st == StatusParcelaHonorario.Vencido)
            {
                var j = CalcularJuros(p.ValorOriginal, p.Vencimento, hoje);
                jurosMulta = Math.Round(j.multa + j.juros, 2);
                valorFinal = j.total;
                atraso += j.total;
            }
            else if (st == StatusParcelaHonorario.Pendente)
            {
                pendente += p.ValorOriginal;
            }

            var label = p.IsEntrada ? "Entrada" : $"{p.Numero}/{totalParc}";
            var statusLabel = st switch
            {
                StatusParcelaHonorario.Pago => "Pago",
                StatusParcelaHonorario.Cancelado => "Cancelado",
                StatusParcelaHonorario.Vencido => "Em Atraso",
                _ => "Pendente"
            };

            linhas.Add(new ExtratoParcelaPdfDto(
                label, p.Vencimento, p.ValorOriginal,
                jurosMulta, valorFinal, statusLabel,
                p.DataPagamento
            ));
        }

        return new ExtratoPdfDadosDto(
            nomeEscritorio,
            config?.AdvogadoResponsavel,
            config?.OAB,
            config?.Endereco ?? tenant?.Endereco,
            config?.Telefone,
            config?.Email,
            config?.LogoUrl ?? tenant?.LogoUrl,
            c.NumeroContrato,
            c.Objeto,
            c.Contato?.Nome,
            c.Contato?.CpfCnpj,
            c.Processo?.NumeroCNJ,
            c.ValorTotal,
            FormatForma(c),
            c.TipoCobranca,
            c.PercentualMulta,
            c.PercentualJurosMensal,
            c.DataInicio,
            pago, pendente, atraso,
            linhas,
            DateTime.Now
        );
    }

    // ===================== HELPERS =====================

    private static void ValidarFormaPagamento(CriarContratoHonorarioDto dto)
    {
        if (dto.FormaPagamento == FormaPagamentoContrato.AVista)
        {
            if (!dto.DataPrimeiraParcela.HasValue)
                throw new ArgumentException("À Vista exige data de vencimento.");
        }
        else if (dto.FormaPagamento == FormaPagamentoContrato.Parcelado)
        {
            if (!dto.NumeroParcelas.HasValue || dto.NumeroParcelas < 2)
                throw new ArgumentException("Parcelado exige no mínimo 2 parcelas.");
            if (!dto.Periodicidade.HasValue)
                throw new ArgumentException("Parcelado exige periodicidade.");
            if (!dto.DataPrimeiraParcela.HasValue)
                throw new ArgumentException("Parcelado exige data da 1ª parcela.");
        }
        else // EntradaParcelado
        {
            if (!dto.ValorEntrada.HasValue || dto.ValorEntrada <= 0)
                throw new ArgumentException("Entrada + Parcelado exige valor da entrada.");
            if (!dto.VencimentoEntrada.HasValue)
                throw new ArgumentException("Entrada + Parcelado exige vencimento da entrada.");
            if (dto.ValorEntrada.Value >= dto.ValorTotal)
                throw new ArgumentException("A entrada deve ser menor que o valor total.");
            if (!dto.NumeroParcelas.HasValue || dto.NumeroParcelas < 1)
                throw new ArgumentException("Entrada + Parcelado exige nº de parcelas do saldo (mínimo 1).");
            if (!dto.DataPrimeiraParcela.HasValue)
                throw new ArgumentException("Entrada + Parcelado exige data da 1ª parcela do saldo.");
        }
    }

    private static List<ParcelaHonorario> GerarParcelas(ContratoHonorario c)
    {
        var list = new List<ParcelaHonorario>();

        if (c.FormaPagamento == FormaPagamentoContrato.AVista)
        {
            list.Add(new ParcelaHonorario
            {
                Id = Guid.NewGuid(),
                TenantId = c.TenantId,
                ContratoId = c.Id,
                Numero = 1,
                IsEntrada = true,
                Vencimento = c.DataPrimeiraParcela ?? c.DataInicio,
                ValorOriginal = c.ValorTotal,
                Status = StatusParcelaHonorario.Pendente
            });
        }
        else if (c.FormaPagamento == FormaPagamentoContrato.Parcelado)
        {
            list.AddRange(GerarParcelasSaldo(c, c.ValorTotal, c.DataPrimeiraParcela!.Value));
        }
        else // EntradaParcelado
        {
            list.Add(new ParcelaHonorario
            {
                Id = Guid.NewGuid(),
                TenantId = c.TenantId,
                ContratoId = c.Id,
                Numero = 0,
                IsEntrada = true,
                Vencimento = c.VencimentoEntrada!.Value,
                ValorOriginal = c.ValorEntrada!.Value,
                Status = StatusParcelaHonorario.Pendente
            });
            var saldo = Math.Round(c.ValorTotal - c.ValorEntrada.Value, 2, MidpointRounding.AwayFromZero);
            list.AddRange(GerarParcelasSaldo(c, saldo, c.DataPrimeiraParcela!.Value));
        }

        return list;
    }

    private static IEnumerable<ParcelaHonorario> GerarParcelasSaldo(ContratoHonorario c, decimal valor, DateTime d1)
    {
        var n = c.NumeroParcelas!.Value;
        var list = new List<ParcelaHonorario>();
        var vp = Math.Round(valor / n, 2, MidpointRounding.AwayFromZero);
        decimal soma = 0;
        for (int i = 0; i < n; i++)
        {
            DateTime venc;
            if (i == 0) venc = d1;
            else
            {
                venc = c.Periodicidade switch
                {
                    PeriodicidadeParcela.Mensal => AddMonths(d1, i),
                    PeriodicidadeParcela.Quinzenal => d1.AddDays(i * 15),
                    PeriodicidadeParcela.Semanal => d1.AddDays(i * 7),
                    PeriodicidadeParcela.Semestral => AddMonths(d1, i * 6),
                    _ => AddMonths(d1, i)
                };
            }
            list.Add(new ParcelaHonorario
            {
                Id = Guid.NewGuid(),
                TenantId = c.TenantId,
                ContratoId = c.Id,
                Numero = i + 1,
                IsEntrada = false,
                Vencimento = venc,
                ValorOriginal = vp,
                Status = StatusParcelaHonorario.Pendente
            });
            soma += vp;
        }
        // Ajuste de arredondamento na última
        var diff = Math.Round(valor - soma, 2, MidpointRounding.AwayFromZero);
        if (diff != 0)
        {
            var ult = list[^1];
            ult.ValorOriginal = Math.Round(ult.ValorOriginal + diff, 2, MidpointRounding.AwayFromZero);
        }
        return list;
    }

    public static (decimal multa, decimal juros, int dias, decimal total) CalcularJuros(decimal valor, DateTime vencimento, DateTime referencia)
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

    private static DateTime AddMonths(DateTime d, int months)
    {
        var target = d.AddMonths(months);
        return target;
    }

    private void RecalcularStatus(ContratoHonorario c)
    {
        var parcelas = c.Parcelas.Where(p => p.Status != StatusParcelaHonorario.Cancelado).ToList();
        if (parcelas.Count == 0) return;

        var todasPagas = parcelas.All(p => p.Status == StatusParcelaHonorario.Pago);
        var temVencida = parcelas.Any(p => p.Status != StatusParcelaHonorario.Pago && p.Vencimento.Date < DateTime.UtcNow.Date);

        if (c.Status == StatusContratoHonorario.Distratado || c.Status == StatusContratoHonorario.Encerrado)
            return;

        if (todasPagas) c.Status = StatusContratoHonorario.Quitado;
        else if (temVencida) c.Status = StatusContratoHonorario.Inadimplente;
        else c.Status = StatusContratoHonorario.Ativo;
    }

    private async Task ValidarContatoAsync(Guid tenantId, Guid contatoId, Guid? processoId, CancellationToken ct)
    {
        var existeContato = await db.Contatos.AnyAsync(x => x.Id == contatoId && x.TenantId == tenantId, ct);
        if (!existeContato) throw new ArgumentException("Contato não encontrado.");

        if (processoId.HasValue)
        {
            var existeProcesso = await db.Processos.AnyAsync(x => x.Id == processoId.Value && x.TenantId == tenantId, ct);
            if (!existeProcesso) throw new ArgumentException("Processo não encontrado.");
        }
    }

    private async Task<string> GerarNumeroContratoAsync(Guid tenantId, CancellationToken ct)
    {
        var ano = DateTime.UtcNow.Year;
        var count = await db.ContratosHonorarios.CountAsync(c => c.TenantId == tenantId && c.CriadoEm.Year == ano, ct);
        return $"HON-{ano}/{(count + 1):D4}";
    }

    private async Task SalvarComHistorico(ContratoHonorario c, EventoContratoHonorario tipo, string descricao, Guid? usuarioId, object? antes, object? depois, CancellationToken ct)
    {
        if (c.AtualizadoEm == null) c.AtualizadoEm = DateTime.UtcNow;

        // Aguarda persistência explícita do contrato primeiro
        await db.SaveChangesAsync(ct);

        // Em seguida registra o histórico (só inserção, sem ambiguidade)
        var hist = new HistoricoContratoHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = c.TenantId,
            ContratoId = c.Id,
            TipoEvento = tipo,
            Descricao = descricao,
            UsuarioId = usuarioId,
            CriadoEm = DateTime.UtcNow
        };
        db.HistoricosContratoHonorario.Add(hist);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(new AuditLogEntry(
            c.TenantId, usuarioId,
            tipo == EventoContratoHonorario.Criado ? AuditActions.Create : AuditActions.Update,
            AuditEntities.ContratoHonorario, c.Id.ToString(), antes, depois
        ), ct);
    }

    private static object CloneParaHistorico(ContratoHonorario c) => SnapshotParaHistorico(c);

    private static object SnapshotParaHistorico(ContratoHonorario c) => new
    {
        c.Objeto,
        c.ValorTotal,
        c.FormaPagamento,
        c.Periodicidade,
        c.NumeroParcelas,
        c.ValorEntrada,
        c.VencimentoEntrada,
        c.DataPrimeiraParcela,
        c.TipoCobranca,
        c.Observacoes,
        c.PercentualMulta,
        c.PercentualJurosMensal,
        c.Status
    };

    private static ContratoHonorarioDto MapearContrato(ContratoHonorario c, bool incluirFinanceiro)
    {
        var hoje = DateTime.UtcNow.Date;
        decimal pago = 0, pendente = 0, atraso = 0;
        int total = 0, pagas = 0, vencidas = 0, pend = 0;
        if (incluirFinanceiro && c.Parcelas != null)
        {
            foreach (var p in c.Parcelas)
            {
                total++;
                if (p.Status == StatusParcelaHonorario.Pago)
                {
                    pago += p.ValorPago ?? p.ValorOriginal;
                    pagas++;
                }
                else if (p.Status == StatusParcelaHonorario.Cancelado) { /* ignore */ }
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
        }

        return new ContratoHonorarioDto(
            c.Id,
            c.NumeroContrato,
            c.ContatoId,
            c.Contato?.Nome ?? "",
            c.Contato?.CpfCnpj,
            c.ProcessoId,
            c.Processo?.NumeroCNJ,
            c.Objeto,
            c.ValorTotal,
            c.FormaPagamento,
            c.Periodicidade,
            c.NumeroParcelas,
            c.DataPrimeiraParcela,
            c.ValorEntrada,
            c.VencimentoEntrada,
            c.PercentualMulta,
            c.PercentualJurosMensal,
            c.TipoCobranca,
            c.Observacoes,
            c.Status,
            c.DataInicio,
            c.DataFim,
            c.CriadoEm,
            c.AtualizadoEm,
            c.DistratoEm,
            c.DistratoMotivo,
            pago,
            pendente,
            atraso,
            total,
            pagas,
            vencidas,
            pend
        );
    }

    private static ParcelaHonorarioDto MapearParcela(ParcelaHonorario p)
    {
        var hoje = DateTime.UtcNow.Date;
        decimal? juros = null, valorAtualizado = null;
        int? dias = null;
        if (p.Status != StatusParcelaHonorario.Pago && p.Status != StatusParcelaHonorario.Cancelado)
        {
            var d = (hoje - p.Vencimento.Date).Days;
            if (d > 0)
            {
                var j = CalcularJuros(p.ValorOriginal, p.Vencimento, hoje);
                juros = Math.Round(j.multa + j.juros, 2);
                valorAtualizado = j.total;
                dias = j.dias;
            }
        }

        return new ParcelaHonorarioDto(
            p.Id, p.ContratoId, p.Numero, p.IsEntrada,
            p.Vencimento, p.ValorOriginal,
            p.DataPagamento, p.ValorPago,
            p.Observacao, p.Status,
            p.LancamentoFinanceiroId,
            juros, valorAtualizado, dias
        );
    }

    private static string FormatBRL(decimal v) => v.ToString("C", PtBr);
    private static string FormatForma(ContratoHonorario c) => c.FormaPagamento switch
    {
        FormaPagamentoContrato.AVista => "À Vista",
        FormaPagamentoContrato.Parcelado => $"Parcelado — {c.NumeroParcelas}x {FormatPeriodicidade(c.Periodicidade)}",
        FormaPagamentoContrato.EntradaParcelado => $"Entrada + {c.NumeroParcelas}x {FormatPeriodicidade(c.Periodicidade)} (saldo)",
        _ => "—"
    };

    private static string FormatPeriodicidade(PeriodicidadeParcela? p) => p switch
    {
        PeriodicidadeParcela.Mensal => "mensal",
        PeriodicidadeParcela.Quinzenal => "quinzenal",
        PeriodicidadeParcela.Semanal => "semanal",
        PeriodicidadeParcela.Semestral => "semestral",
        _ => "—"
    };

    private static string Truncar(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
