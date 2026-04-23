using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

public class SeedService
{
    private readonly AppDbContext _context;
    private readonly ILogger<SeedService> _logger;
    private readonly Random _rnd = new(42);

    public SeedService(AppDbContext context, ILogger<SeedService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task GerarDadosDemoAsync(Guid tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation("Iniciando geração de dados demo para tenant {TenantId}", tenantId);

        var existingContatos = await _context.Contatos.AnyAsync(c => c.TenantId == tenantId, ct);
        if (existingContatos)
        {
            throw new InvalidOperationException("Dados demo já existem para este tenant. Use /seed/desfazer primeiro.");
        }

        var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId, ct);
        if (adminUser == null)
            throw new InvalidOperationException("Nenhum usuário encontrado para o tenant.");

        var clientes = await GerarClientesAsync(tenantId, ct);
        var processos = await GerarProcessosAsync(tenantId, clientes, adminUser.Id, ct);
        await GerarAndamentosAsync(tenantId, processos, adminUser.Id, ct);
        await GerarTarefasAsync(tenantId, processos, clientes, adminUser.Id, ct);
        await GerarEventosAsync(tenantId, processos, adminUser.Id, ct);
        await GerarFinanceiroAsync(tenantId, processos, clientes, ct);
        await GerarNotificacoesAsync(tenantId, adminUser.Id, ct);

        _logger.LogInformation("Dados demo gerados com sucesso para tenant {TenantId}", tenantId);
    }

    public async Task DesfazerDadosDemoAsync(Guid tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation("Removendo dados demo para tenant {TenantId}", tenantId);

        _context.LancamentosFinanceiros.RemoveRange(
            _context.LancamentosFinanceiros.Where(l => l.TenantId == tenantId));

        _context.Eventos.RemoveRange(_context.Eventos.Where(e => e.TenantId == tenantId));

        _context.TarefaTags.RemoveRange(
            _context.TarefaTags.Include(t => t.Tarefa)
                .Where(t => t.Tarefa.TenantId == tenantId)
                .ToList());
        _context.Tarefas.RemoveRange(_context.Tarefas.Where(t => t.TenantId == tenantId));

        _context.Publicacoes.RemoveRange(_context.Publicacoes.Where(p => p.TenantId == tenantId));
        _context.Prazos.RemoveRange(_context.Prazos.Where(p => p.TenantId == tenantId));

        _context.ProcessoPartes.RemoveRange(
            _context.ProcessoPartes.Include(pp => pp.Processo)
                .Where(pp => pp.Processo.TenantId == tenantId));
        _context.Andamentos.RemoveRange(
            _context.Andamentos.Include(a => a.Processo)
                .Where(a => a.Processo.TenantId == tenantId));
        _context.Processos.RemoveRange(_context.Processos.Where(p => p.TenantId == tenantId));

        _context.Atendimentos.RemoveRange(_context.Atendimentos.Where(a => a.TenantId == tenantId));
        _context.ContatoTags.RemoveRange(
            _context.ContatoTags.Include(ct => ct.Contato)
                .Where(ct => ct.Contato.TenantId == tenantId));
        _context.Contatos.RemoveRange(_context.Contatos.Where(c => c.TenantId == tenantId));

        _context.Notificacoes.RemoveRange(_context.Notificacoes.Where(n => n.TenantId == tenantId));

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Dados demo removidos com sucesso para tenant {TenantId}", tenantId);
    }

    private async Task<List<Contato>> GerarClientesAsync(Guid tenantId, CancellationToken ct)
    {
        var nomes = new[] {
            ("Maria Silva", "11122233344", "maria@email.com", "(11) 98765-4321"),
            ("João Santos", "22233344455", "joao@email.com", "(21) 99876-5432"),
            ("Ana Costa", "33344455566", "ana@email.com", "(31) 98765-1234"),
            ("Carlos Oliveira", "44455566677", "carlos@email.com", "(41) 97654-3210"),
            ("Lucia Ferreira", "55566677788", "lucia@email.com", "(51) 96543-2109"),
            ("Paulo Mendes", "66677788899", "paulo@email.com", "(61) 95432-1098"),
            ("Mariana Souza", "77788899900", "mariana@email.com", "(71) 94321-0987"),
            ("Roberto Lima", "88899900011", "roberto@email.com", "(11) 93210-9876")
        };

        var cidades = new[] { "São Paulo", "Rio de Janeiro", "Belo Horizonte", "Curitiba", "Salvador" };
        var clientes = new List<Contato>();

        for (int i = 0; i < nomes.Length; i++)
        {
            var (nome, cpf, email, tel) = nomes[i];
            var cidade = cidades[i % cidades.Length];

            var cliente = new Contato
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Tipo = i < 5 ? TipoPessoa.PF : TipoPessoa.PJ,
                TipoContato = i < 6 ? TipoContato.Cliente : TipoContato.ParteContraria,
                Nome = nome,
                CpfCnpj = cpf,
                Email = email,
                Telefone = tel,
                Cidade = cidade,
                Estado = new[] { "SP", "RJ", "MG", "PR", "BA" }[i % 5],
                DataNascimento = DateTime.Now.AddYears(-25 - (i * 3)),
                NotificacaoHabilitada = true,
                IAHabilitada = i % 2 == 0,
                CriadoEm = DateTime.UtcNow.AddDays(-90 - (i * 10))
            };
            clientes.Add(cliente);
        }

        await _context.Contatos.AddRangeAsync(clientes, ct);
        await _context.SaveChangesAsync(ct);

        for (int i = 0; i < clientes.Count; i++)
        {
            var tag = new ContatoTag
            {
                Id = Guid.NewGuid(),
                ContatoId = clientes[i].Id,
                Tag = i < 3 ? "VIP" : (i < 6 ? "Recorrente" : "Novo")
            };
            _context.ContatoTags.Add(tag);
        }
        await _context.SaveChangesAsync(ct);

        return clientes;
    }

    private async Task<List<Processo>> GerarProcessosAsync(Guid tenantId, List<Contato> clientes, Guid advogadoId, CancellationToken ct)
    {
        var tribunais = new[] { "TJSP", "TJMG", "TJRJ", "TRF1", "TRF3" };
        var areas = new[] { AreaDireito.Civil, AreaDireito.Trabalhista, AreaDireito.Consumidor, AreaDireito.Familia, AreaDireito.Previdenciario };
        var fases = new[] { FaseProcessual.Conhecimento, FaseProcessual.Recursal, FaseProcessual.Cumprimento };
        var status = new[] { StatusProcesso.Ativo, StatusProcesso.Ativo, StatusProcesso.Ativo, StatusProcesso.Suspenso, StatusProcesso.Encerrado };

        var processos = new List<Processo>();

        for (int i = 0; i < 12; i++)
        {
            var num = $"{_rnd.Next(1000000, 9999999)}-{_rnd.Next(10, 99)}.{_rnd.Next(2018, 2026)}.{_rnd.Next(1, 26)}.{_rnd.Next(1000, 9999)}";
            var tribunal = tribunais[_rnd.Next(tribunais.Length)];
            var processo = new Processo
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                NumeroCNJ = num,
                Tribunal = tribunal,
                Vara = $" {_rnd.Next(1, 20)}ª Vara {areas[i % areas.Length]}",
                Comarca = new[] { "São Paulo", "Rio de Janeiro", "Belo Horizonte" }[_rnd.Next(3)],
                AreaDireito = areas[i % areas.Length],
                TipoAcao = new[] { "Indenização", "Cobrança", "Despejo", "Reintegração", "Divórcio", "Inventário" }[_rnd.Next(6)],
                Fase = fases[_rnd.Next(fases.Length)],
                Status = status[i % status.Length],
                ValorCausa = _rnd.Next(10000, 500000),
                AdvogadoResponsavelId = advogadoId,
                Monitorado = i < 8,
                Observacoes = i % 3 == 0 ? "Caso requiere atención especial" : null,
                CriadoEm = DateTime.UtcNow.AddDays(-_rnd.Next(30, 180))
            };

            if (processo.Status == StatusProcesso.Encerrado)
            {
                processo.EncerradoEm = DateTime.UtcNow.AddDays(-_rnd.Next(5, 30));
                processo.Decisao = "Procedente";
                processo.Resultado = _rnd.Next(2) == 0 ? "Ganho" : "Acordo";
            }

            processos.Add(processo);

            var autor = clientes.First(c => c.TipoContato == TipoContato.Cliente);
            var reu = clientes.First(c => c.TipoContato == TipoContato.ParteContraria);

            _context.ProcessoPartes.Add(new ProcessoParte
            {
                Id = Guid.NewGuid(),
                ProcessoId = processo.Id,
                ContatoId = autor.Id,
                TipoParte = TipoParteProcesso.Autor
            });
            _context.ProcessoPartes.Add(new ProcessoParte
            {
                Id = Guid.NewGuid(),
                ProcessoId = processo.Id,
                ContatoId = reu.Id,
                TipoParte = TipoParteProcesso.Reu
            });
        }

        await _context.Processos.AddRangeAsync(processos, ct);
        await _context.SaveChangesAsync(ct);
        return processos;
    }

    private async Task GerarAndamentosAsync(Guid tenantId, List<Processo> processos, Guid usuarioId, CancellationToken ct)
    {
        var tipos = new[] { TipoAndamento.Despacho, TipoAndamento.Decisao, TipoAndamento.Intimacao, TipoAndamento.Publicacao };

        foreach (var processo in processos)
        {
            var numAndamentos = _rnd.Next(3, 10);
            for (int i = 0; i < numAndamentos; i++)
            {
                var andamento = new Andamento
                {
                    Id = Guid.NewGuid(),
                    ProcessoId = processo.Id,
                    TenantId = tenantId,
                    Data = processo.CriadoEm.AddDays(i * _rnd.Next(2, 15)),
                    Tipo = tipos[_rnd.Next(tipos.Length)],
                    Descricao = GenerateDescricaoAndamento(i),
                    Fonte = FonteAndamento.Manual,
                    RegistradoPorId = usuarioId,
                    CriadoEm = DateTime.UtcNow.AddDays(-_rnd.Next(1, 30))
                };
                _context.Andamentos.Add(andamento);
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    private string GenerateDescricaoAndamento(int index)
    {
        var descricoes = new[] {
            "Recebido(s) em 01/02/2026: PETIÇÃO DE ID 12345678.",
            "Juntada de contestação apresentada pelo réu.",
            "Designada audiência de instrução para o dia 15/03/2026 às 14h.",
            "Decisão interlocutória: intime-se a parte autora para manifestação em 15 dias.",
            "Publicação de sentença: procedente em parte o pedido.",
            "Embargos de declaração opostos pela parte autora.",
            "Remessa dos autos ao contador para cálculo.",
            "Expedição de ofício para penhora online.",
            "Ato ordinatório - credor deve atualizar planilha de débitos.",
            "Conclusão ao(à) MM. Juiz(a) para sentença."
        };
        return descricoes[index % descricoes.Length];
    }

    private async Task GerarTarefasAsync(Guid tenantId, List<Processo> processos, List<Contato> clientes, Guid criadoPorId, CancellationToken ct)
    {
        var responsaveis = await _context.Users.Where(u => u.TenantId == tenantId).ToListAsync(ct);
        if (!responsaveis.Any()) responsaveis.Add(new Usuario { Id = criadoPorId, Nome = "Admin" });

        var titulos = new[] {
            "Revisar petição inicial", "Preparar contestação", "Emitir parecer",
            "Elaborar recurso", "Acompanhar audiência", "Contatar cliente",
            "Atualizar planilha de honorários", "Verificar prazos", "Redigirimpugnação"
        };

        var statusList = Enum.GetValues<StatusTarefa>();
        var prioridades = Enum.GetValues<PrioridadeTarefa>();

        foreach (var processo in processos.Take(8))
        {
            var numTarefas = _rnd.Next(2, 5);
            for (int i = 0; i < numTarefas; i++)
            {
                var prazo = DateTime.UtcNow.AddDays(_rnd.Next(-5, 15));
                var status = prazo < DateTime.UtcNow
                    ? StatusTarefa.Concluida
                    : statusList[_rnd.Next(statusList.Length)];

                var tarefa = new Tarefa
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProcessoId = processo.Id,
                    ContatoId = clientes.First().Id,
                    Titulo = titulos[_rnd.Next(titulos.Length)],
                    Descricao = $"Tarefa relacionada ao processo {processo.NumeroCNJ}",
                    ResponsavelId = responsaveis[_rnd.Next(responsaveis.Count)].Id,
                    CriadoPorId = criadoPorId,
                    Prazo = prazo,
                    Prioridade = prioridades[_rnd.Next(prioridades.Length)],
                    Status = status,
                    CriadoEm = DateTime.UtcNow.AddDays(-_rnd.Next(1, 20))
                };

                if (status == StatusTarefa.Concluida)
                    tarefa.ConcluidaEm = prazo.AddDays(_rnd.Next(1, 3));

                _context.Tarefas.Add(tarefa);
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    private async Task GerarEventosAsync(Guid tenantId, List<Processo> processos, Guid usuarioId, CancellationToken ct)
    {
        var tipos = Enum.GetValues<TipoEvento>();

        for (int i = 0; i < 10; i++)
        {
            var dataEvento = DateTime.UtcNow.AddDays(_rnd.Next(-10, 30));
            var tipo = tipos[_rnd.Next(tipos.Length)];

            var evento = new Evento
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcessoId = processos.ElementAtOrDefault(i)?.Id,
                Tipo = tipo,
                Titulo = GetTituloEvento(tipo, i),
                DataHora = dataEvento,
                DataHoraFim = dataEvento.AddHours(1),
                Local = tipo == TipoEvento.Audiencia ? $" {_rnd.Next(1, 10)}ª Vara - Foro Central" : null,
                ResponsavelId = usuarioId,
                Observacoes = "Evento gerado automaticamente para demonstração",
                CriadoEm = DateTime.UtcNow.AddDays(-_rnd.Next(1, 10))
            };

            _context.Eventos.Add(evento);
        }

        await _context.SaveChangesAsync(ct);
    }

    private string GetTituloEvento(TipoEvento tipo, int index)
    {
        return tipo switch
        {
            TipoEvento.Audiencia => $"Audiência de Instrução #{index + 1}",
            TipoEvento.Reuniao => $"Reunião com cliente #{index + 1}",
            TipoEvento.Pericia => $"Perícia médica #{index + 1}",
            TipoEvento.Prazo => $"Prazo: alegações finais #{index + 1}",
            TipoEvento.Despacho => $"Despacho de mérito #{index + 1}",
            _ => $"Compromisso #{index + 1}"
        };
    }

    private async Task GerarFinanceiroAsync(Guid tenantId, List<Processo> processos, List<Contato> clientes, CancellationToken ct)
    {
        var categoriasReceita = new[] { "Honorario", "Honorario", "Honorario", "Custas", "Reembolso" };
        var categoriasDespesa = new[] { "Custas", "Pericia", "Software", "Software", "AluguelEscritorio" };

        foreach (var cliente in clientes.Take(5))
        {
            for (int m = 0; m < 3; m++)
            {
                var dataBase = DateTime.UtcNow.AddMonths(-m);

                var receita = new LancamentoFinanceiro
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ContatoId = cliente.Id,
                    Tipo = TipoLancamento.Receita,
                    Categoria = categoriasReceita[_rnd.Next(categoriasReceita.Length)],
                    Valor = _rnd.Next(2000, 15000),
                    Descricao = $"Honorários do processo {cliente.Nome}",
                    DataVencimento = new DateTime(dataBase.Year, dataBase.Month, 10),
                    Status = m == 0 ? StatusLancamento.Pendente : StatusLancamento.Pago,
                    DataPagamento = m == 0 ? null : new DateTime(dataBase.Year, dataBase.Month, 8),
                    CriadoEm = dataBase.AddDays(-20)
                };
                _context.LancamentosFinanceiros.Add(receita);
            }
        }

        foreach (var processo in processos.Take(5))
        {
            var dataBase = DateTime.UtcNow.AddMonths(-_rnd.Next(0, 3));

            var despesa = new LancamentoFinanceiro
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcessoId = processo.Id,
                Tipo = TipoLancamento.Despesa,
                Categoria = categoriasDespesa[_rnd.Next(categoriasDespesa.Length)],
                Valor = _rnd.Next(200, 2000),
                Descricao = $"Despesa processual - {processo.NumeroCNJ}",
                DataVencimento = new DateTime(dataBase.Year, dataBase.Month, 20),
                Status = _rnd.Next(2) == 0 ? StatusLancamento.Pago : StatusLancamento.Pendente,
                DataPagamento = _rnd.Next(2) == 0 ? new DateTime(dataBase.Year, dataBase.Month, 18) : null,
                CriadoEm = dataBase.AddDays(-10)
            };
            _context.LancamentosFinanceiros.Add(despesa);
        }

        await _context.SaveChangesAsync(ct);
    }

    private async Task GerarNotificacoesAsync(Guid tenantId, Guid usuarioId, CancellationToken ct)
    {
        var tipos = Enum.GetValues<TipoNotificacao>();

        for (int i = 0; i < 5; i++)
        {
            var notificacao = new Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UsuarioId = usuarioId,
                Tipo = tipos[_rnd.Next(tipos.Length)],
                Titulo = GetTituloNotificacao(i),
                Mensagem = "Esta é uma notificação de demonstração.",
                Lida = i > 2,
                CriadaEm = DateTime.UtcNow.AddHours(-i * 6)
            };
            _context.Notificacoes.Add(notificacao);
        }

        await _context.SaveChangesAsync(ct);
    }

    private string GetTituloNotificacao(int index)
    {
        return index switch
        {
            0 => "Novo andamento registrado",
            1 => "Prazo vencendo em 3 dias",
            2 => "Audiência amanhã",
            3 => "Lembrete: reunião com cliente",
            _ => "Atualização do sistema"
        };
    }
}