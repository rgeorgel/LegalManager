using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.Interfaces;
using System.IO.Compression;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class DocumentoService : IDocumentoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IStorageService _storageService;
    private const long COTA_BYTES = 20L * 1024 * 1024 * 1024;

    public DocumentoService(AppDbContext context, ITenantContext tenantContext, IStorageService storageService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _storageService = storageService;
    }

    public async Task<DocumentoDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await _context.Documentos
            .Include(d => d.Processo)
            .Include(d => d.Cliente)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        return doc == null ? null : MapToDto(doc);
    }

    public async Task<IEnumerable<DocumentoDto>> GetByProcessoAsync(Guid processoId, CancellationToken ct = default)
    {
        var documentos = await _context.Documentos
            .Include(d => d.Processo)
            .Include(d => d.Cliente)
            .Where(d => d.ProcessoId == processoId && d.TenantId == _tenantContext.TenantId)
            .OrderByDescending(d => d.CriadoEm)
            .ToListAsync(ct);

        return documentos.Select(MapToDto);
    }

    public async Task<IEnumerable<DocumentoDto>> GetByClienteAsync(Guid clienteId, Guid? pastaId = null, CancellationToken ct = default)
    {
        var query = _context.Documentos
            .Include(d => d.Processo)
            .Include(d => d.Cliente)
            .Include(d => d.Pasta)
            .Where(d => d.ClienteId == clienteId && d.TenantId == _tenantContext.TenantId);

        if (pastaId.HasValue)
            query = query.Where(d => d.PastaId == pastaId.Value);

        var documentos = await query
            .OrderByDescending(d => d.CriadoEm)
            .ToListAsync(ct);

        return documentos.Select(MapToDto);
    }

    public async Task<IEnumerable<DocumentoDto>> GetAllAsync(Guid? pastaId = null, bool raiz = false, CancellationToken ct = default)
    {
        var query = _context.Documentos
            .Include(d => d.Processo)
            .Include(d => d.Cliente)
            .Include(d => d.Pasta)
            .Where(d => d.TenantId == _tenantContext.TenantId);

        if (pastaId.HasValue)
            query = query.Where(d => d.PastaId == pastaId.Value);
        else if (raiz)
            query = query.Where(d => d.PastaId == null);

        var documentos = await query
            .OrderByDescending(d => d.CriadoEm)
            .ToListAsync(ct);

        return documentos.Select(MapToDto);
    }

    public async Task<IEnumerable<DocumentoDto>> GetByPastaAsync(Guid pastaId, CancellationToken ct = default)
    {
        var documentos = await _context.Documentos
            .Include(d => d.Processo)
            .Include(d => d.Cliente)
            .Include(d => d.Pasta)
            .Where(d => d.PastaId == pastaId && d.TenantId == _tenantContext.TenantId)
            .OrderByDescending(d => d.CriadoEm)
            .ToListAsync(ct);

        return documentos.Select(MapToDto);
    }

    public async Task<DocumentoDto> UploadAsync(Stream fileStream, string fileName, string contentType,
        DocumentoUploadDto uploadInfo, Guid? uploadedPorId = null, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Tenant não identificado. Usuário deve estar autenticado.");

        var usadoBytes = await _context.Documentos
            .Where(d => d.TenantId == tenantId)
            .SumAsync(d => d.TamanhoBytes, ct);

        if (usadoBytes + fileStream.Length > COTA_BYTES)
            throw new InvalidOperationException("Cota de armazenamento excedida.");

        var safeFileName = System.Text.RegularExpressions.Regex.Replace(fileName, @"[^A-Za-z0-9._\-]", "_");

        string objectKey;
        if (uploadInfo.PastaId.HasValue)
            objectKey = $"{tenantId}/documentos/{uploadInfo.PastaId}/{DateTime.UtcNow:yyyyMMddHHmmss}_{safeFileName}";
        else if (uploadInfo.ProcessoId.HasValue)
            objectKey = $"{tenantId}/processos/{uploadInfo.ProcessoId}/{DateTime.UtcNow:yyyyMMddHHmmss}_{safeFileName}";
        else if (uploadInfo.ContratoId.HasValue)
            objectKey = $"{tenantId}/contratos/{uploadInfo.ContratoId}/{DateTime.UtcNow:yyyyMMddHHmmss}_{safeFileName}";
        else if (uploadInfo.ClienteId.HasValue)
            objectKey = $"{tenantId}/clientes/{uploadInfo.ClienteId}/{DateTime.UtcNow:yyyyMMddHHmmss}_{safeFileName}";
        else if (uploadInfo.ModeloId.HasValue)
            objectKey = $"{tenantId}/modelos/{uploadInfo.ModeloId}/{DateTime.UtcNow:yyyyMMddHHmmss}_{safeFileName}";
        else
            objectKey = $"{tenantId}/documentos/{DateTime.UtcNow:yyyyMMddHHmmss}_{safeFileName}";

        await _storageService.UploadAsync(fileStream, objectKey, contentType, ct);

        var documento = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcessoId = uploadInfo.ProcessoId,
            ClienteId = uploadInfo.ClienteId,
            ContratoId = uploadInfo.ContratoId,
            ModeloId = uploadInfo.ModeloId,
            Nome = string.IsNullOrWhiteSpace(uploadInfo.Nome) ? fileName : uploadInfo.Nome,
            ObjectKey = objectKey,
            ContentType = contentType,
            TamanhoBytes = fileStream.Length,
            Tipo = uploadInfo.Tipo,
            CriadoEm = DateTime.UtcNow,
            UploadedPorId = uploadedPorId,
            PastaId = uploadInfo.PastaId
        };

        _context.Documentos.Add(documento);
        await _context.SaveChangesAsync(ct);

        return MapToDto(documento);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var documento = await _context.Documentos
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == _tenantContext.TenantId, ct)
            ?? throw new KeyNotFoundException("Documento não encontrado.");

        await _storageService.DeleteAsync(documento.ObjectKey, ct);

        _context.Documentos.Remove(documento);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdatePastaAsync(Guid id, Guid? pastaId, CancellationToken ct = default)
    {
        var documento = await _context.Documentos
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == _tenantContext.TenantId, ct)
            ?? throw new KeyNotFoundException("Documento não encontrado.");

        await ValidarPastaAsync(pastaId, ct);
        documento.PastaId = pastaId;
        await _context.SaveChangesAsync(ct);
    }

    // A pasta precisa ser de documentos e do mesmo escritório.
    private async Task ValidarPastaAsync(Guid? pastaId, CancellationToken ct)
    {
        if (pastaId is not { } id) return;
        var existe = await _context.Pastas.AnyAsync(p =>
            p.Id == id && p.TenantId == _tenantContext.TenantId && p.EntidadeTipo == EntidadeTipo.Documento, ct);
        if (!existe) throw new KeyNotFoundException("Pasta não encontrada.");
    }

    private Task<List<Documento>> DoTenantAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
        _context.Documentos
            .Where(d => ids.Contains(d.Id) && d.TenantId == _tenantContext.TenantId)
            .ToListAsync(ct);

    public async Task<int> ExcluirEmMassaAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var documentos = await DoTenantAsync(ids, ct);
        foreach (var d in documentos)
            await _storageService.DeleteAsync(d.ObjectKey, ct);
        _context.Documentos.RemoveRange(documentos);
        await _context.SaveChangesAsync(ct);
        return documentos.Count;
    }

    public async Task<int> MoverEmMassaAsync(IReadOnlyList<Guid> ids, Guid? pastaId, CancellationToken ct = default)
    {
        await ValidarPastaAsync(pastaId, ct);
        var documentos = await DoTenantAsync(ids, ct);
        foreach (var d in documentos) d.PastaId = pastaId;
        await _context.SaveChangesAsync(ct);
        return documentos.Count;
    }

    public async Task<int> AlterarTipoEmMassaAsync(IReadOnlyList<Guid> ids, TipoDocumento tipo, CancellationToken ct = default)
    {
        var documentos = await DoTenantAsync(ids, ct);
        foreach (var d in documentos) d.Tipo = tipo;
        await _context.SaveChangesAsync(ct);
        return documentos.Count;
    }

    public async Task<int> GerarZipAsync(IReadOnlyList<Guid> ids, Stream destino, CancellationToken ct = default)
    {
        var documentos = (await DoTenantAsync(ids, ct)).OrderBy(d => d.Nome).ToList();
        if (documentos.Sum(d => d.TamanhoBytes) > DocumentosEmMassaDto.MaxBytesZip)
            throw new InvalidOperationException(
                $"Os documentos selecionados passam de {DocumentoHelpers.FormatFileSize(DocumentosEmMassaDto.MaxBytesZip)}. Baixe em partes menores.");

        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var zip = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var d in documentos)
        {
            var entrada = zip.CreateEntry(NomeUnico(d.Nome, usados), CompressionLevel.Fastest);
            await using var origem = await _storageService.DownloadAsync(d.ObjectKey, ct);
            await using var saida = entrada.Open();
            await origem.CopyToAsync(saida, ct);
        }
        return documentos.Count;
    }

    private static string NomeUnico(string nome, HashSet<string> usados)
    {
        var limpo = string.Join("_", nome.Split(Path.GetInvalidFileNameChars().Append('/').Append('\\').ToArray()));
        if (string.IsNullOrWhiteSpace(limpo)) limpo = "documento";
        var candidato = limpo;
        var ext = Path.GetExtension(limpo);
        var baseNome = Path.GetFileNameWithoutExtension(limpo);
        for (var i = 2; !usados.Add(candidato); i++)
            candidato = $"{baseNome} ({i}){ext}";
        return candidato;
    }

    public async Task<string> GetDownloadUrlAsync(Guid id, CancellationToken ct = default)
    {
        var documento = await _context.Documentos
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento não encontrado.");

        return await _storageService.GetPresignedUrlAsync(documento.ObjectKey, 30, ct);
    }

    public async Task<(Stream stream, string contentType, string fileName)> GetFileStreamAsync(Guid id, CancellationToken ct = default)
    {
        var documento = await _context.Documentos
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento não encontrado.");

        var stream = await _storageService.DownloadAsync(documento.ObjectKey, ct);
        return (stream, documento.ContentType, documento.Nome);
    }

    public async Task<CotaArmazenamentoDto> GetCotaAsync(CancellationToken ct = default)
    {
        var usadoBytes = await _context.Documentos
            .Where(d => d.TenantId == _tenantContext.TenantId)
            .SumAsync(d => d.TamanhoBytes, ct);

        return new CotaArmazenamentoDto
        {
            UsadoBytes = usadoBytes,
            CotaBytes = COTA_BYTES
        };
    }

    private static DocumentoDto MapToDto(Documento d) => new()
    {
        Id = d.Id,
        ProcessoId = d.ProcessoId,
        NumeroProcesso = d.Processo?.NumeroCNJ,
        ClienteId = d.ClienteId,
        NomeCliente = d.Cliente?.Nome,
        ModeloId = d.ModeloId,
        Nome = d.Nome,
        ContentType = d.ContentType,
        TamanhoBytes = d.TamanhoBytes,
        Tipo = d.Tipo,
        CriadoEm = d.CriadoEm,
        PastaId = d.PastaId,
        PastaNome = d.Pasta?.Nome
    };
}