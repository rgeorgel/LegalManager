namespace LegalManager.Domain.Entities;

// Biblioteca de imagens públicas do site (superadmin → Imagens), servidas em
// /imagens/{pasta}/{nome} — ver ImagensController. Usadas nos guias, novidades e onde mais o
// super admin colar o endereço. Entidades globais (não pertencem a um tenant).
public class PastaImagem
{
    public Guid Id { get; set; }

    // Slug (minúsculas, sem acento, hífens): é o trecho {pasta} da URL. Não há renomear —
    // mudaria o endereço de todas as imagens da pasta.
    public string Nome { get; set; } = string.Empty;

    public DateTime CriadoEm { get; set; }

    public ICollection<ImagemPublica> Imagens { get; set; } = new List<ImagemPublica>();
}

public class ImagemPublica
{
    public Guid Id { get; set; }
    public Guid PastaId { get; set; }
    public PastaImagem Pasta { get; set; } = null!;

    // Nome do arquivo na URL, com extensão (ex: "tela-portal.png"). Único dentro da pasta.
    public string Nome { get; set; } = string.Empty;

    // Chave no storage ("imagens/{guid}{ext}"). Substituir a imagem grava um objeto novo e
    // troca a chave — a URL pública continua a mesma.
    public string ObjectKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;
    public long Tamanho { get; set; }

    public DateTime CriadoEm { get; set; }
    public Guid? CriadoPorId { get; set; }

    // Última troca do arquivo (envio ou substituição): entra no ETag da URL pública.
    public DateTime AtualizadoEm { get; set; }
}
