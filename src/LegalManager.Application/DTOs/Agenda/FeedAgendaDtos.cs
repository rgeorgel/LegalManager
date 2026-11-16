using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Agenda;

/// <summary>Estado do link do calendário do usuário (sem o token, que só aparece ao ser gerado).</summary>
public record FeedAgendaDto(
    bool Ativo,
    EscopoFeedAgenda Escopo,
    bool OcultarClientes,
    DateTime? CriadoEm,
    DateTime? UltimoAcessoEm,
    bool PodeEscritorio);

/// <summary>Resposta de "gerar link": o caminho com o token, mostrado uma única vez.</summary>
public record FeedAgendaGeradoDto(string Caminho, FeedAgendaDto Feed);

/// <summary>OcultarClientes ausente = padrão do escopo (oculto no do escritório, visível no "Meus").</summary>
public record ConfigurarFeedAgendaDto(EscopoFeedAgenda Escopo = EscopoFeedAgenda.Meus, bool? OcultarClientes = null);
