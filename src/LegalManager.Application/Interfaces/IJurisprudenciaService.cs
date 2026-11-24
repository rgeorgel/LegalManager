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

    /// <summary>Lê o texto de um documento do escritório (PDF ou texto) e confere as citações.</summary>
    Task<VerificacaoCitacoesDto?> VerificarDocumentoAsync(Guid documentoId, CancellationToken ct = default);

    /// <summary>Precedentes da base mais relevantes para o texto (busca full-text), sem os cancelados.</summary>
    Task<IReadOnlyList<PrecedenteDto>> BuscarPrecedentesAsync(string consulta, int limite, CancellationToken ct = default);
}
