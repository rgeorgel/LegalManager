namespace LegalManager.Application.DTOs.Busca;

/// <summary>
/// Um resultado da busca global (Ctrl/⌘ + K e caixa do dashboard).
/// <paramref name="Tipo"/>: processo | contato | tarefa | evento | documento | contrato.
/// <paramref name="Data"/>: prazo da tarefa ou data do evento, na hora "de parede" (ler com dataParede()).
/// </summary>
public record BuscaGlobalItemDto(
    string Tipo,
    Guid Id,
    string Titulo,
    string? Subtitulo,
    string Url,
    DateTime? Data = null,
    bool Inativo = false);

public record BuscaGlobalResultadoDto(string Termo, IReadOnlyList<BuscaGlobalItemDto> Itens);
