namespace LegalManager.Domain.Enums;

public enum PlanoTipo { Free, Pro, Enterprise }

public enum StatusTenant { Ativo, Trial, Suspenso, Cancelado }

public enum PerfilUsuario { Admin, Advogado, Colaborador, Cliente }

public enum TipoContato { Cliente, ParteContraria, Testemunha, Perito, Outro }

public enum TipoPessoa { PF, PJ }

public enum StatusProcesso { Ativo, Suspenso, Arquivado, Encerrado }

public enum FaseProcessual
{
    Conhecimento, Recursal, Execucao, Cumprimento,
    InqueritoPolicial, InvestigacaoDefensiva, Outro
}

public enum AreaDireito
{
    Civil, Trabalhista, Criminal, Tributario, Previdenciario,
    Administrativo, Consumidor, Familia, Empresarial,
    Ambiental, Imobiliario, Outro
}

public enum TipoParteProcesso { Autor, Reu, Interessado, Terceiro }

public enum FonteAndamento { Manual, Automatico }

public enum TipoAndamento
{
    Despacho, Decisao, Sentenca, Acordao, Audiencia,
    Peticao, Intimacao, Publicacao, Outro
}

public enum StatusTarefa { Pendente, EmAndamento, Concluida, Cancelada }

public enum PrioridadeTarefa { Baixa, Media, Alta, Urgente }

public enum TipoEvento { Audiencia, Reuniao, Pericia, Prazo, Despacho, Outro }

public enum TipoNotificacao { PrazoTarefa, PrazoEvento, TrialExpirando, Geral, NovoAndamento }

public enum TipoPublicacao { Prazo, Audiencia, Decisao, Despacho, Intimacao, Outro }

public enum StatusPublicacao { Nova, Lida, Arquivada }

public enum TipoDje { Djus, Djen, Dou }

public enum StatusPrazo { Pendente, Cumprido, Perdido, Suspenso }

public enum TipoCalculo { DiasUteis, DiasCorridos }

public enum TipoLancamento { Receita, Despesa }

public enum StatusLancamento { Pendente, Pago, Vencido, Cancelado }

public enum TipoDocumento { Peticao, Decisao, Contrato, Prova, Modelo, Outro }

public static class CategoriaLancamento
{
    public const string Honorario = "Honorario";
    public const string Custas = "Custas";
    public const string Pericia = "Pericia";
    public const string Deposito = "Deposito";
    public const string Multa = "Multa";
    public const string Reembolso = "Reembolso";
    public const string Salario = "Salario";
    public const string AluguelEscritorio = "AluguelEscritorio";
    public const string Software = "Software";
    public const string Marketing = "Marketing";
    public const string Outro = "Outro";
}
