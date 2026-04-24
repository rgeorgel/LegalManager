using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Documentos;

public class DocumentoDto
{
    public Guid Id { get; set; }
    public Guid? ProcessoId { get; set; }
    public string? NumeroProcesso { get; set; }
    public Guid? ClienteId { get; set; }
    public string? NomeCliente { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long TamanhoBytes { get; set; }
    public string TamanhoFormatado => DocumentoHelpers.FormatFileSize(TamanhoBytes);
    public TipoDocumento Tipo { get; set; }
    public DateTime CriadoEm { get; set; }
    public string? UrlDownload { get; set; }
}

public class DocumentoUploadDto
{
    public Guid? ProcessoId { get; set; }
    public Guid? ClienteId { get; set; }
    public Guid? ContratoId { get; set; }
    public Guid? ModeloId { get; set; }
    public TipoDocumento Tipo { get; set; }
    public string? Nome { get; set; }
}

public class CotaArmazenamentoDto
{
    public long UsadoBytes { get; set; }
    public long CotaBytes { get; set; }
    public string UsadoFormatado => DocumentoHelpers.FormatFileSize(UsadoBytes);
    public string CotaFormatado => DocumentoHelpers.FormatFileSize(CotaBytes);
    public double PorcentagemUsada => CotaBytes > 0 ? (double)UsadoBytes / CotaBytes * 100 : 0;
}

public static class DocumentoHelpers
{
    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}