using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Processos;

public record ProcessoMonitoradoAndamentoDto(
    DateTime Data,
    string Descricao,
    int? CodigoCNJ,
    string? OrgaoJulgador);

public record ProcessoMonitoradoResponseDto(
    Guid Id,
    string NumeroCNJ,
    string? NomeExibicao,
    bool Ativo,
    DateTime CriadoEm);

public record CreateProcessoMonitoradoDto(
    [Required][MaxLength(50)] string NumeroCNJ,
    [MaxLength(200)] string? NomeExibicao,
    List<ProcessoMonitoradoAndamentoDto>? Andamentos = null);

public record ProcessoMonitoradoCreateResultDto(
    Guid Id,
    string NumeroCNJ,
    string? NomeExibicao,
    bool Ativo,
    DateTime CriadoEm,
    bool ProcessoEncontrado,
    string? Tribunal,
    string? Vara,
    string? Mensagem);