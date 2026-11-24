using LegalManager.Application.DTOs.IA;

namespace LegalManager.Application.Interfaces;

/// <summary>
/// Citações de jurisprudência: achar no texto, conferir na base oficial (tabela Precedentes) e buscar
/// precedentes para a geração de peças com fonte. Ver docs/features/jurisprudencia-verificada.md.
/// </summary>
public interface IJurisprudenciaService
{
    /// <summary>
    /// Acha as citações do texto. Com <paramref name="consultarBase"/> falso (escritório sem o recurso
    /// liberado), todas voltam como <see cref="StatusCitacao.NaoVerificada"/>.
    /// </summary>
    Task<VerificacaoCitacoesDto> VerificarTextoAsync(string texto, bool consultarBase, CancellationToken ct = default);

    /// <summary>Página pública (sem login): mesma conferência, com menos consultas ao DataJud.</summary>
    Task<VerificacaoCitacoesDto> VerificarTextoPublicoAsync(string texto, CancellationToken ct = default);

    /// <summary>
    /// Busca por significado com a IA atual: amplia a busca com termos jurídicos e escolhe/ordena os
    /// pertinentes entre os candidatos do full-text. Sem IA (ou se ela falhar), igual à busca por palavras.
    /// </summary>
    Task<IReadOnlyList<PrecedenteDto>> BuscarPrecedentesPorSignificadoAsync(string consulta, int limite, CancellationToken ct = default);

    /// <summary>Lê o texto de um documento do escritório (PDF ou texto) e confere as citações.</summary>
    Task<VerificacaoCitacoesDto?> VerificarDocumentoAsync(Guid documentoId, CancellationToken ct = default);

    /// <summary>Precedentes da base mais relevantes para o texto (busca full-text), sem os cancelados.</summary>
    Task<IReadOnlyList<PrecedenteDto>> BuscarPrecedentesAsync(string consulta, int limite, CancellationToken ct = default);
}
