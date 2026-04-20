using LegalManager.Application.DTOs.PortalCliente;

namespace LegalManager.Application.Interfaces;

public interface IPortalClienteService
{
    Task<PortalAuthResponseDto> LoginAsync(LoginPortalDto dto, CancellationToken ct = default);
    Task<ClientePerfilDto> GetPerfilAsync(Guid acessoId, CancellationToken ct = default);
    Task<IEnumerable<MeuProcessoDto>> GetMeusProcessosAsync(Guid contatoId, Guid tenantId, CancellationToken ct = default);
    Task<MeuProcessoDto?> GetProcessoAsync(Guid processoId, Guid contatoId, Guid tenantId, CancellationToken ct = default);
    Task<IEnumerable<MeuAndamentoDto>> GetAndamentosAsync(Guid processoId, Guid contatoId, Guid tenantId, CancellationToken ct = default);

    // Office-side management
    Task<AcessoPortalInfoDto> CriarAcessoAsync(Guid contatoId, CriarAcessoPortalDto dto, Guid tenantId, CancellationToken ct = default);
    Task<AcessoPortalInfoDto?> GetAcessoAsync(Guid contatoId, Guid tenantId, CancellationToken ct = default);
    Task RevogarAcessoAsync(Guid contatoId, Guid tenantId, CancellationToken ct = default);
}
