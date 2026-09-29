using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

/// <summary>
/// Importação de processos por OAB executada em background (Hangfire): o job pesquisa
/// todos os processos da OAB e importa um por um. <see cref="ModoImportacao.Selecionados"/>
/// só existe em importações antigas (a tela não permite mais escolher processos).
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

    // Estado da pesquisa (modo Todos), para retomar de onde parou se o job reiniciar:
    // primeiro pagina o Escavador (cursor da próxima página), depois DataJud/e-SAJ.
    public string? CursorEscavador { get; set; }
    public bool BuscaEscavadorConcluida { get; set; }
    public bool BuscaTribunaisConcluida { get; set; }

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
