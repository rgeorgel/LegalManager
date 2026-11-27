using LegalManager.Application.DTOs.Processos;

namespace LegalManager.Application.Interfaces;

public interface IRequerimentoInssService
{
    /// <summary>Null quando o processo não tem dados do INSS. KeyNotFound se o processo não é do escritório.</summary>
    Task<RequerimentoInssResponseDto?> ObterAsync(Guid processoId, CancellationToken ct = default);

    /// <summary>Cria ou atualiza. Só para processo administrativo (InvalidOperationException nos demais).</summary>
    Task<RequerimentoInssResponseDto> SalvarAsync(Guid processoId, RequerimentoInssDto dto, CancellationToken ct = default);

    /// <summary>Cria a tarefa/evento de uma sugestão ainda pendente (KeyNotFound se não houver).</summary>
    Task<SugestaoInssCriadaDto> CriarSugestaoAsync(Guid processoId, string chave, CancellationToken ct = default);
}
