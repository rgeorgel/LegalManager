namespace LegalManager.Application.DTOs.Modelos;

public class ModeloDocumentoDto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string Conteudo { get; set; } = string.Empty;
    public List<string> Variaveis { get; set; } = new();
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
}

public class UpdateModeloDocumentoDto
{
    public string? Nome { get; set; }
    public string? Descricao { get; set; }
    public string? Conteudo { get; set; }
    public List<string>? Variaveis { get; set; }
}

public class AplicarVariaveisDto
{
    public Dictionary<string, string> Variaveis { get; set; } = new();
}
