using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Notificacoes;

public record NotificacaoDto(
    Guid Id,
    TipoNotificacao Tipo,
    string Titulo,
    string Mensagem,
    bool Lida,
    string? Url,
    DateTime CriadaEm
);
