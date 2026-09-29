using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

/// <summary>
/// Importação de processos por OAB executada em background (Hangfire). No modo
/// <see cref="ModoImportacao.Todos"/> o job também faz a busca nos tribunais; no modo
/// <see cref="ModoImportacao.Selecionados"/> os itens já chegam escolhidos pelo usuário.
/// </summary>
public class ImportacaoProcessos
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public ModoImportacao Modo { get; set; }
    public string NumeroOab { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public StatusImportacao Status { get; set; }

    public int Total { get; set; }
    public int Processados { get; set; }
    public int Importados { get; set; }
    public int JaCadastrados { get; set; }
    public int Erros { get; set; }
    public string? MensagemErro { get; set; }

    public DateTime CriadoEm { get; set; }
    // Último avanço do job — usado para detectar importações interrompidas (job morto).
    public DateTime AtualizadoEm { get; set; }
    public DateTime? ConcluidoEm { get; set; }

    public List<ImportacaoProcessoItem> Itens { get; set; } = [];
}

public class ImportacaoProcessoItem
{
    public Guid Id { get; set; }
    public Guid ImportacaoId { get; set; }
    public int Ordem { get; set; }
    public string NumeroCNJ { get; set; } = string.Empty;
    public string? Tribunal { get; set; }
    // ImportarProcessoItem serializado — o que a importação precisa para montar o processo.
    public string DadosJson { get; set; } = string.Empty;
    public StatusItemImportacao Status { get; set; }
    public string? Mensagem { get; set; }
    public Guid? ProcessoId { get; set; }
    public DateTime? ProcessadoEm { get; set; }

    public ImportacaoProcessos Importacao { get; set; } = null!;
}
