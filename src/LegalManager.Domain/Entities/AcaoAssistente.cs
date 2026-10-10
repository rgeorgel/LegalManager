namespace LegalManager.Domain.Entities;

public enum StatusAcaoAssistente { Pendente, Confirmada, Cancelada, Falhou, Desfeita }

/// <summary>
/// Alteração proposta pelo assistente de IA (contatos, tarefas, agenda, atendimentos, horas).
/// O assistente nunca grava direto: a ferramenta só registra a proposta, e ela é executada
/// quando o próprio usuário clica em "Confirmar" no chat. Fica guardada também como trilha de
/// auditoria (o que foi proposto, por quem, e se foi confirmado). Tabela de log: sem FK.
/// </summary>
public class AcaoAssistente
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid? ImpersonadoPorId { get; set; }
    public Guid ConversaId { get; set; }
    /// <summary>Pergunta (<see cref="InteracaoAssistente"/>) em que a ação foi proposta.</summary>
    public Guid InteracaoId { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }

    /// <summary>Nome da ferramenta que propôs (ex.: atualizar_contato, criar_tarefa).</summary>
    public string Tipo { get; set; } = string.Empty;
    /// <summary>Registro alterado — ou o registro criado, depois de confirmada.</summary>
    public Guid? EntidadeId { get; set; }
    /// <summary>Resumo para o usuário, ex.: "Atualizar contato Maria Silva".</summary>
    public string Titulo { get; set; } = string.Empty;
    /// <summary>JSON: [{ campo, antes, depois }] — o que o usuário vê antes de confirmar.</summary>
    public string AlteracoesJson { get; set; } = "[]";
    /// <summary>JSON com os dados validados que serão gravados na confirmação.</summary>
    public string DadosJson { get; set; } = "{}";

    public StatusAcaoAssistente Status { get; set; }
    public DateTime? ResolvidaEm { get; set; }
    public string? Erro { get; set; }

    /// <summary>
    /// JSON gravado na confirmação para permitir desfazer: o registro como era antes (alterações)
    /// e o AtualizadoEm deixado pela confirmação — se mudar depois, o desfazer é recusado.
    /// </summary>
    public string? DesfazerJson { get; set; }
    public DateTime? DesfeitaEm { get; set; }
}
