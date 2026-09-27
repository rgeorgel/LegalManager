namespace LegalManager.Application.DTOs.Modelos;

public class ModeloDocumentoDto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string Conteudo { get; set; } = string.Empty;
    public List<string> Variaveis { get; set; } = new();
    public bool UsarTimbrado { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public Guid CriadoPorId { get; set; }
    public string? NomeCriadoPor { get; set; }
}

public class CreateModeloDocumentoDto
{
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string Conteudo { get; set; } = string.Empty;
    public List<string> Variaveis { get; set; } = new();
    public bool UsarTimbrado { get; set; } = true;
}

public class UpdateModeloDocumentoDto
{
    public string? Nome { get; set; }
    public string? Descricao { get; set; }
    public string? Conteudo { get; set; }
    public List<string>? Variaveis { get; set; }
    public bool? UsarTimbrado { get; set; }
}

/// <summary>Dados do papel timbrado aplicado aos documentos gerados a partir de modelos.</summary>
public record TimbradoDto(
    string Nome,
    string? Cnpj,
    string? Endereco,
    string? Telefone,
    string? Email,
    string? Complemento,
    string? LogoUrl);

public class AplicarVariaveisDto
{
    public Dictionary<string, string> Variaveis { get; set; } = new();
}

public class GerarModeloComIADto
{
    public string Descricao { get; set; } = string.Empty;
}

public class GerarModeloComIAResultDto
{
    public string Conteudo { get; set; } = string.Empty;
    public List<string> Variaveis { get; set; } = new();
}
