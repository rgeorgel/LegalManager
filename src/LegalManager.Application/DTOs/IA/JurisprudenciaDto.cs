namespace LegalManager.Application.DTOs.IA;

public enum TipoCitacao
{
    /// <summary>Recurso ou ação de tribunal superior (REsp, AgInt no AREsp, RE, HC...).</summary>
    Acordao,
    Sumula,
    SumulaVinculante,
    Tema,
    OrientacaoJurisprudencial,
    /// <summary>Acórdão de TJ/TRF/TRT citado pelo número CNJ.</summary>
    AcordaoTribunalLocal,
}

public enum StatusCitacao
{
    /// <summary>Encontrada na base oficial.</summary>
    Verificada,
    /// <summary>Existe, mas o texto atribui dados diferentes (ex.: outro relator).</summary>
    Divergente,
    /// <summary>Súmula ou tema cancelado/superado.</summary>
    Cancelada,
    /// <summary>Lista fechada carregada por inteiro e o número não existe nela.</summary>
    Inexistente,
    /// <summary>Tribunal coberto, mas a decisão não está na base (pode existir).</summary>
    NaoEncontrada,
    /// <summary>Fora do que a base cobre — conferir na fonte.</summary>
    NaoVerificada,
}

/// <summary>Uma citação encontrada no texto. <paramref name="Inicio"/>/<paramref name="Fim"/> são posições no texto.</summary>
public record CitacaoDto(
    TipoCitacao Tipo,
    string? Tribunal,
    string? Classe,
    string Numero,
    string Trecho,
    int Inicio,
    int Fim,
    string? Relator = null);

public record PrecedenteDto(
    Guid Id,
    string Tipo,
    string Tribunal,
    string? Classe,
    string Numero,
    string Identificacao,
    string? Relator,
    string? OrgaoJulgador,
    DateTime? DataJulgamento,
    string Texto,
    string? Situacao,
    string UrlFonte);

public record CitacaoVerificadaDto(
    TipoCitacao Tipo,
    string? Tribunal,
    string? Classe,
    string Numero,
    string Trecho,
    int Inicio,
    int Fim,
    StatusCitacao Status,
    string Mensagem,
    PrecedenteDto? Precedente = null);

public record VerificacaoCitacoesDto(
    IReadOnlyList<CitacaoVerificadaDto> Citacoes,
    int Verificadas,
    int Alertas,
    int Inexistentes,
    /// <summary>O que a base cobre hoje, para mostrar junto do resultado.</summary>
    string Cobertura);

/// <summary>Verificação avulsa: texto colado ou um documento (PDF/TXT) do escritório.</summary>
public record VerificarCitacoesDto(string? Texto, Guid? DocumentoId);
