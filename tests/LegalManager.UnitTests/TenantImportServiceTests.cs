using System.Text;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LegalManager.UnitTests;

public class TenantImportServiceTests
{
    private AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static Tenant SeedTenant(AppDbContext ctx, string nome = "Tenant Origem")
    {
        var t = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = nome,
            Cnpj = "11.111.111/0001-11",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(t);
        return t;
    }

    private static string BuildExportJson(Guid sourceTenantId, string sourceTenantNome, Dictionary<string, object> tables)
    {
        var envelope = new
        {
            version = 1,
            exportedAt = DateTime.UtcNow,
            sourceTenantId,
            sourceTenantNome,
            appVersion = "1.0.0",
            anonymization = new
            {
                emailsReplacedWith = "@causify-replica.com",
                phonesReplacedWith = "+5511900000000",
                passwordsResetTo = "Causify@Replic@2026!",
                fieldsScanned = new[] { "Email", "Telefone" }
            },
            tables
        };
        return System.Text.Json.JsonSerializer.Serialize(envelope,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
    }

    private IPasswordHasher<Usuario> CreatePasswordHasher()
    {
        return new PasswordHasher<Usuario>();
    }

    [Fact]
    public async Task ImportAsync_JsonValido_InsereDados()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_JsonValido_InsereDados) + "-src");
        var source = SeedTenant(sourceCtx);
        var sourceContato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Nome = "Maria Souza",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            Email = "maria@origem.com",
            Telefone = "(11) 99999-1111",
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        };
        sourceCtx.Contatos.Add(sourceContato);
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);
        var jsonBytes = exportResult.Payload;

        using var targetCtx = CreateContext(nameof(ImportAsync_JsonValido_InsereDados) + "-tgt");
        var target = SeedTenant(targetCtx, "Tenant Destino");
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(jsonBytes);
        var result = await importService.ImportAsync(
            new TenantImportRequest(target.Id, TenantImportMode.Replace, stream, "export.json"),
            CancellationToken.None);

        Assert.True(result.RowsImported > 0);
        Assert.Equal(1, result.RowsByTable.GetValueOrDefault("Contatos", 0));
    }

    [Fact]
    public async Task ImportAsync_EmailsSaoAnonimizados_AposImport()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_EmailsSaoAnonimizados_AposImport) + "-src");
        var source = SeedTenant(sourceCtx);
        sourceCtx.Contatos.Add(new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Nome = "X",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            Email = "cliente.real@empresa.com",
            Telefone = "(11) 91234-5678",
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        });
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_EmailsSaoAnonimizados_AposImport) + "-tgt");
        var target = SeedTenant(targetCtx, "Destino");
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);
        await importService.ImportAsync(
            new TenantImportRequest(target.Id, TenantImportMode.Replace, stream, "export.json"),
            CancellationToken.None);

        var importedContato = await targetCtx.Contatos.FirstAsync();
        Assert.EndsWith("@causify-replica.com", importedContato.Email);
        Assert.Equal("+5511900000000", importedContato.Telefone);
    }

    [Fact]
    public async Task ImportAsync_WipeDadosExistentes_AntesDeImportar()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_WipeDadosExistentes_AntesDeImportar) + "-src");
        var source = SeedTenant(sourceCtx);
        sourceCtx.Contatos.Add(new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Nome = "Do Source",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        });
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_WipeDadosExistentes_AntesDeImportar) + "-tgt");
        var target = SeedTenant(targetCtx, "Destino");
        var existingContato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = target.Id,
            Nome = "Contato Antigo",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            Email = "antigo@dest.com",
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        };
        targetCtx.Contatos.Add(existingContato);
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);
        await importService.ImportAsync(
            new TenantImportRequest(target.Id, TenantImportMode.Replace, stream, "export.json"),
            CancellationToken.None);

        var contatos = await targetCtx.Contatos.Where(c => c.TenantId == target.Id).ToListAsync();
        Assert.Single(contatos);
        Assert.NotEqual(existingContato.Id, contatos[0].Id);
        Assert.DoesNotContain(contatos, c => c.Nome == "Contato Antigo");
    }

    [Fact]
    public async Task ImportAsync_WipeTenantComCicloParcelaLancamento_NaoFalha()
    {
        // Regressão: ParcelaHonorario.LancamentoFinanceiroId <-> LancamentoFinanceiro.
        // ParcelaHonorarioId formam um ciclo de FK genuíno (preenchido por
        // FinanceiroService/HonorarioService ao registrar um pagamento). No Postgres real,
        // apagar o tenant destino com esse ciclo populado bloqueava o DELETE por violação
        // de FK, independente da ordem das tabelas — era preciso zerar um dos lados antes.
        // EF InMemory não valida FK, então este teste cobre a execução do código (sem
        // exceptions); a ordem de DELETE em si é coberta por TenantTableSpecsTests.
        using var sourceCtx = CreateContext(nameof(ImportAsync_WipeTenantComCicloParcelaLancamento_NaoFalha) + "-src");
        var source = SeedTenant(sourceCtx);
        sourceCtx.Contatos.Add(new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Nome = "Do Source",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        });
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_WipeTenantComCicloParcelaLancamento_NaoFalha) + "-tgt");
        var target = SeedTenant(targetCtx, "Destino");
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = target.Id,
            Nome = "Adv",
            Email = "adv@destino.com",
            UserName = "adv@destino.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        var contato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = target.Id,
            Nome = "Cliente Antigo",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        };
        var contrato = new ContratoHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = target.Id,
            ContatoId = contato.Id,
            NumeroContrato = "C-OLD",
            ValorTotal = 1000m,
            FormaPagamento = FormaPagamentoContrato.Parcelado,
            CriadoPorId = user.Id,
            DataInicio = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow
        };
        var lancamento = new LancamentoFinanceiro
        {
            Id = Guid.NewGuid(),
            TenantId = target.Id,
            ContratoHonorarioId = contrato.Id
        };
        var parcela = new ParcelaHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = target.Id,
            ContratoId = contrato.Id,
            Numero = 1,
            Vencimento = DateTime.UtcNow,
            ValorOriginal = 1000m,
            LancamentoFinanceiroId = lancamento.Id
        };
        lancamento.ParcelaHonorarioId = parcela.Id;

        targetCtx.Users.Add(user);
        targetCtx.Contatos.Add(contato);
        targetCtx.ContratosHonorarios.Add(contrato);
        targetCtx.LancamentosFinanceiros.Add(lancamento);
        targetCtx.ParcelasHonorarios.Add(parcela);
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);
        await importService.ImportAsync(
            new TenantImportRequest(target.Id, TenantImportMode.Replace, stream, "export.json"),
            CancellationToken.None);

        Assert.False(await targetCtx.ParcelasHonorarios.AnyAsync(p => p.Id == parcela.Id));
        Assert.False(await targetCtx.LancamentosFinanceiros.AnyAsync(l => l.Id == lancamento.Id));
        Assert.False(await targetCtx.ContratosHonorarios.AnyAsync(c => c.Id == contrato.Id));
    }

    [Fact]
    public async Task ImportAsync_UsuarioPasswordHashResetado()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_UsuarioPasswordHashResetado) + "-src");
        var source = SeedTenant(sourceCtx);
        var sourceUser = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Nome = "Admin",
            Email = "admin@empresa.com",
            UserName = "admin@empresa.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            PasswordHash = "AQAAAAIAAYagAAAAIFakeOldHash0000000000000="
        };
        sourceCtx.Users.Add(sourceUser);
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_UsuarioPasswordHashResetado) + "-tgt");
        var target = SeedTenant(targetCtx, "Destino");
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);
        await importService.ImportAsync(
            new TenantImportRequest(target.Id, TenantImportMode.Replace, stream, "export.json"),
            CancellationToken.None);

        var importedUser = await targetCtx.Users.FirstAsync(u => u.TenantId == target.Id);
        Assert.NotEqual(sourceUser.PasswordHash, importedUser.PasswordHash);
        Assert.NotNull(importedUser.PasswordHash);
        Assert.NotEmpty(importedUser.PasswordHash);
    }

    [Fact]
    public async Task ImportAsync_PreservaEnumsDatasEDecimais()
    {
        // Regressão: Dictionary<string, object?> desserializado do JSON entrega cada valor
        // como JsonElement, que não implementa IConvertible. Convert.ToInt32/ToDateTime/
        // ToDecimal (usados por ConvertValue) falhavam silenciosamente para esse tipo — o
        // catch em DictToEntity engolia a exceção e a propriedade ficava com o valor
        // default: status de Tarefa/Processo/ContratoHonorario resetava para o primeiro
        // valor do enum, datas (Prazo, DataInicio/Fim) viravam null, e decimais (ValorTotal)
        // viravam 0. Só Guid "funcionava", por acidente, via Guid.Parse(raw.ToString()).
        using var sourceCtx = CreateContext(nameof(ImportAsync_PreservaEnumsDatasEDecimais) + "-src");
        var source = SeedTenant(sourceCtx);
        var sourceUser = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Nome = "Advogado",
            Email = "adv@origem.com",
            UserName = "adv@origem.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        var sourceContato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Nome = "Cliente Origem",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        };
        var processo = new Processo
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            NumeroCNJ = "0001234-56.2024.8.26.0100",
            AreaDireito = AreaDireito.Civil,
            Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Encerrado,
            CriadoEm = DateTime.UtcNow
        };
        var prazo = new DateTime(2027, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var tarefa = new Tarefa
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Titulo = "Recurso",
            CriadoPorId = sourceUser.Id,
            Status = StatusTarefa.Concluida,
            Prioridade = PrioridadeTarefa.Alta,
            Tipo = TipoTarefa.Prazo,
            Prazo = prazo,
            CriadoEm = DateTime.UtcNow
        };
        var dataInicioContrato = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var contrato = new ContratoHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            ContatoId = sourceContato.Id,
            NumeroContrato = "C-001",
            ValorTotal = 12345.67m,
            FormaPagamento = FormaPagamentoContrato.Parcelado,
            Status = StatusContratoHonorario.Quitado,
            CriadoPorId = sourceUser.Id,
            DataInicio = dataInicioContrato,
            CriadoEm = DateTime.UtcNow
        };
        sourceCtx.Users.Add(sourceUser);
        sourceCtx.Contatos.Add(sourceContato);
        sourceCtx.Processos.Add(processo);
        sourceCtx.Tarefas.Add(tarefa);
        sourceCtx.ContratosHonorarios.Add(contrato);
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_PreservaEnumsDatasEDecimais) + "-tgt");
        var target = SeedTenant(targetCtx, "Destino");
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);
        await importService.ImportAsync(
            new TenantImportRequest(target.Id, TenantImportMode.Replace, stream, "export.json"),
            CancellationToken.None);

        var importedProcesso = await targetCtx.Processos.FirstAsync(p => p.TenantId == target.Id);
        Assert.Equal(StatusProcesso.Encerrado, importedProcesso.Status);

        var importedTarefa = await targetCtx.Tarefas.FirstAsync(t => t.TenantId == target.Id);
        Assert.Equal(StatusTarefa.Concluida, importedTarefa.Status);
        Assert.Equal(prazo, importedTarefa.Prazo);

        var importedContrato = await targetCtx.ContratosHonorarios.FirstAsync(c => c.TenantId == target.Id);
        Assert.Equal(StatusContratoHonorario.Quitado, importedContrato.Status);
        Assert.Equal(12345.67m, importedContrato.ValorTotal);
        Assert.Equal(dataInicioContrato, importedContrato.DataInicio);
    }

    [Fact]
    public async Task ImportAsync_TenantInexistente_RetornaErro()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_TenantInexistente_RetornaErro));
        var exportService = new TenantImportService(sourceCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);

        var fakeJson = BuildExportJson(Guid.NewGuid(), "X", new Dictionary<string, object>());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fakeJson));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => exportService.ImportAsync(
                new TenantImportRequest(Guid.NewGuid(), TenantImportMode.Replace, stream, "x.json"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_TenantSistema_RetornaErro()
    {
        using var ctx = CreateContext(nameof(ImportAsync_TenantSistema_RetornaErro));
        var service = new TenantImportService(ctx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);

        var fakeJson = BuildExportJson(Guid.NewGuid(), "X", new Dictionary<string, object>());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fakeJson));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(
                new TenantImportRequest(Domain.TenantConstants.SystemTenantId, TenantImportMode.Replace, stream, "x.json"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_JsonInvalido_RetornaErro()
    {
        using var ctx = CreateContext(nameof(ImportAsync_JsonInvalido_RetornaErro));
        var target = SeedTenant(ctx);
        await ctx.SaveChangesAsync();

        var service = new TenantImportService(ctx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not-valid-json{"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(
                new TenantImportRequest(target.Id, TenantImportMode.Replace, stream, "bad.json"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_VersaoIncompativel_RetornaErro()
    {
        using var ctx = CreateContext(nameof(ImportAsync_VersaoIncompativel_RetornaErro));
        var target = SeedTenant(ctx);
        await ctx.SaveChangesAsync();

        var service = new TenantImportService(ctx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);

        var json = $"{{\"version\":999,\"tables\":{{}}}}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(
                new TenantImportRequest(target.Id, TenantImportMode.Replace, stream, "future.json"),
                CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_IdsDiferentes_DoSource()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_IdsDiferentes_DoSource) + "-src");
        var source = SeedTenant(sourceCtx);
        var sourceContato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Nome = "Maria",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        };
        sourceCtx.Contatos.Add(sourceContato);
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_IdsDiferentes_DoSource) + "-tgt");
        var target = SeedTenant(targetCtx, "Destino");
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);
        await importService.ImportAsync(
            new TenantImportRequest(target.Id, TenantImportMode.Replace, stream, "export.json"),
            CancellationToken.None);

        var importedContato = await targetCtx.Contatos.FirstAsync(c => c.TenantId == target.Id);
        Assert.NotEqual(sourceContato.Id, importedContato.Id);
    }

    [Fact]
    public async Task ImportAsync_ModoCreateNew_CriaTenantENaoTocaExistente()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_ModoCreateNew_CriaTenantENaoTocaExistente) + "-src");
        var source = SeedTenant(sourceCtx);
        sourceCtx.Contatos.Add(new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = source.Id,
            Nome = "Contato Do Source",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            Email = "src@empresa.com",
            Telefone = "(11) 99999-1111",
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        });
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_ModoCreateNew_CriaTenantENaoTocaExistente) + "-tgt");
        var existingTenant = SeedTenant(targetCtx, "Tenant Que Nao Deve Ser Tocado");
        var existingContato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = existingTenant.Id,
            Nome = "Nao Mexer Aqui",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        };
        targetCtx.Contatos.Add(existingContato);
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);
        var result = await importService.ImportAsync(
            new TenantImportRequest(
                TargetTenantId: existingTenant.Id,
                Mode: TenantImportMode.CreateNew,
                Payload: stream,
                FileName: "export.json",
                NewTenantName: "Tenant Replica"),
            CancellationToken.None);

        Assert.Equal(TenantImportMode.CreateNew, result.Mode);
        Assert.Equal("Tenant Replica", result.TenantNome);
        Assert.NotEqual(existingTenant.Id, result.TargetTenantId);

        var newTenant = await targetCtx.Tenants.FindAsync(result.TargetTenantId);
        Assert.NotNull(newTenant);
        Assert.Equal("Tenant Replica", newTenant!.Nome);
        Assert.Equal(PlanoTipo.Free, newTenant.Plano);
        Assert.Equal(StatusTenant.Ativo, newTenant.Status);
        Assert.Null(newTenant.Cnpj);
        Assert.Null(newTenant.StripeCustomerId);
        Assert.Null(newTenant.StripeSubscriptionId);
        Assert.Null(newTenant.AbacatePayBillingId);
        Assert.Null(newTenant.PlanoExpiraEm);
        Assert.Null(newTenant.TrialExpiraEm);

        var existingStillThere = await targetCtx.Contatos.FirstAsync(c => c.Id == existingContato.Id);
        Assert.NotNull(existingStillThere);
        Assert.Equal(existingContato.Nome, existingStillThere.Nome);
        Assert.Equal(existingTenant.Id, existingStillThere.TenantId);

        var newTenantContatos = await targetCtx.Contatos.Where(c => c.TenantId == result.TargetTenantId).ToListAsync();
        Assert.Single(newTenantContatos);
        Assert.Equal("Contato Do Source", newTenantContatos[0].Nome);
    }

    [Fact]
    public async Task ImportAsync_ModoCreateNew_NomeDuplicado_RetornaErro()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_ModoCreateNew_NomeDuplicado_RetornaErro) + "-src");
        var source = SeedTenant(sourceCtx);
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_ModoCreateNew_NomeDuplicado_RetornaErro) + "-tgt");
        var existing = SeedTenant(targetCtx, "Nome Ja Existe");
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importService.ImportAsync(
                new TenantImportRequest(
                    TargetTenantId: existing.Id,
                    Mode: TenantImportMode.CreateNew,
                    Payload: stream,
                    FileName: "export.json",
                    NewTenantName: "Nome Já Existe"),
                CancellationToken.None));

        Assert.Contains("Já existe um tenant", ex.Message);
    }

    [Fact]
    public async Task ImportAsync_ModoCreateNew_NomeVazio_RetornaErro()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_ModoCreateNew_NomeVazio_RetornaErro));
        var source = SeedTenant(sourceCtx);
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_ModoCreateNew_NomeVazio_RetornaErro) + "-tgt");
        SeedTenant(targetCtx, "Outro");
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importService.ImportAsync(
                new TenantImportRequest(
                    TargetTenantId: Guid.NewGuid(),
                    Mode: TenantImportMode.CreateNew,
                    Payload: stream,
                    FileName: "export.json",
                    NewTenantName: ""),
                CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_ModoCreateNew_NomeAcima200Caracteres_RetornaErro()
    {
        using var sourceCtx = CreateContext(nameof(ImportAsync_ModoCreateNew_NomeAcima200Caracteres_RetornaErro));
        var source = SeedTenant(sourceCtx);
        await sourceCtx.SaveChangesAsync();

        var exportService = new TenantExportService(sourceCtx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var exportResult = await exportService.ExportAsync(source.Id);

        using var targetCtx = CreateContext(nameof(ImportAsync_ModoCreateNew_NomeAcima200Caracteres_RetornaErro) + "-tgt");
        SeedTenant(targetCtx, "Outro");
        await targetCtx.SaveChangesAsync();

        var importService = new TenantImportService(targetCtx, new TenantAnonymizer(), CreatePasswordHasher(), NullLogger<TenantImportService>.Instance);
        using var stream = new MemoryStream(exportResult.Payload);

        var longName = new string('a', 201);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importService.ImportAsync(
                new TenantImportRequest(
                    TargetTenantId: Guid.NewGuid(),
                    Mode: TenantImportMode.CreateNew,
                    Payload: stream,
                    FileName: "export.json",
                    NewTenantName: longName),
                CancellationToken.None));

        Assert.Contains("200", ex.Message);
    }
}
