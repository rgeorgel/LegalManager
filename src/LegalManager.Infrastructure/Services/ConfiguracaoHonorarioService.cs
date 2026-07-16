using LegalManager.Application.DTOs.Honorarios;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class ConfiguracaoHonorarioService(AppDbContext db, IAuditService audit) : IConfiguracaoHonorarioService
{
    public async Task<ConfiguracaoHonorarioDto> ObterOuCriarPadraoAsync(Guid tenantId, CancellationToken ct = default)
    {
        var cfg = await db.ConfiguracoesHonorarios.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (cfg == null)
        {
            // Não persiste ainda — só retorna defaults para o frontend exibir
            cfg = new ConfiguracaoHonorario { Id = Guid.NewGuid(), TenantId = tenantId };
        }

        return new ConfiguracaoHonorarioDto(
            cfg.NomeEscritorio, cfg.AdvogadoResponsavel, cfg.OAB, cfg.Endereco,
            cfg.Telefone, cfg.Email, cfg.LogoUrl,
            cfg.MetaMensalPadrao, cfg.PercentualMultaDefault, cfg.PercentualJurosMensalDefault,
            cfg.DiasAvisoVencimento
        );
    }

    public async Task<ConfiguracaoHonorarioDto> SalvarAsync(Guid tenantId, ConfiguracaoHonorarioDto dto, CancellationToken ct = default)
    {
        if (dto.PercentualMultaDefault < 0 || dto.PercentualMultaDefault > 1)
            throw new ArgumentException("Multa deve estar entre 0 e 100%.");
        if (dto.PercentualJurosMensalDefault < 0 || dto.PercentualJurosMensalDefault > 1)
            throw new ArgumentException("Juros deve estar entre 0 e 100%/mês.");
        if (dto.MetaMensalPadrao < 0)
            throw new ArgumentException("Meta deve ser maior ou igual a zero.");

        var cfg = await db.ConfiguracoesHonorarios.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (cfg == null)
        {
            cfg = new ConfiguracaoHonorario
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId
            };
            db.ConfiguracoesHonorarios.Add(cfg);
        }

        cfg.NomeEscritorio = dto.NomeEscritorio;
        cfg.AdvogadoResponsavel = dto.AdvogadoResponsavel;
        cfg.OAB = dto.OAB;
        cfg.Endereco = dto.Endereco;
        cfg.Telefone = dto.Telefone;
        cfg.Email = dto.Email;
        cfg.LogoUrl = dto.LogoUrl;
        cfg.MetaMensalPadrao = dto.MetaMensalPadrao;
        cfg.PercentualMultaDefault = dto.PercentualMultaDefault;
        cfg.PercentualJurosMensalDefault = dto.PercentualJurosMensalDefault;
        cfg.DiasAvisoVencimento = dto.DiasAvisoVencimento < 0 ? 3 : dto.DiasAvisoVencimento;
        cfg.AtualizadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync(new AuditLogEntry(
            tenantId, null,
            AuditActions.Update, AuditEntities.ConfiguracaoHonorario,
            cfg.Id.ToString(), null, new { cfg.NomeEscritorio, cfg.OAB }
        ), ct);

        return new ConfiguracaoHonorarioDto(
            cfg.NomeEscritorio, cfg.AdvogadoResponsavel, cfg.OAB, cfg.Endereco,
            cfg.Telefone, cfg.Email, cfg.LogoUrl,
            cfg.MetaMensalPadrao, cfg.PercentualMultaDefault, cfg.PercentualJurosMensalDefault,
            cfg.DiasAvisoVencimento
        );
    }
}
