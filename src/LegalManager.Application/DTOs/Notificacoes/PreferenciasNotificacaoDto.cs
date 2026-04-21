namespace LegalManager.Application.DTOs.Notificacoes;

public record PreferenciasNotificacaoDto(
    bool TarefasInApp,
    bool TarefasEmail,
    bool EventosInApp,
    bool EventosEmail,
    bool PrazosInApp,
    bool PrazosEmail,
    bool PublicacoesInApp,
    bool PublicacoesEmail,
    bool TrialInApp,
    bool GeralInApp
);

public record AtualizarPreferenciasDto(
    bool TarefasInApp,
    bool TarefasEmail,
    bool EventosInApp,
    bool EventosEmail,
    bool PrazosInApp,
    bool PrazosEmail,
    bool PublicacoesInApp,
    bool PublicacoesEmail,
    bool TrialInApp,
    bool GeralInApp
);
