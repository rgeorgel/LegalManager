namespace LegalManager.Domain.Enums;

public enum PlanoTipo { Free = 0, Pro = 1, Enterprise = 2, Plus = 3, Max = 4 }

public enum StatusTenant { Ativo, Trial, Suspenso, Cancelado }

public enum PerfilUsuario { Admin, Advogado, Colaborador, Cliente, SuperAdmin = 5 }

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

public enum FonteAndamento { Manual, Automatico, DataJud }

public enum TipoAndamento
{
    Despacho, Decisao, Sentenca, Acordao, Audiencia,
    Peticao, Intimacao, Publicacao, Outro
}

public enum StatusTarefa { Pendente, EmAndamento, Concluida, Cancelada, Perdida, Suspensa }

public enum PrioridadeTarefa { Baixa, Media, Alta, Urgente }

public enum TipoTarefa { Tarefa, Prazo }

public enum TipoEvento { Audiencia, Reuniao, Pericia, Prazo, Despacho, Outro }

public enum TipoNotificacao { PrazoTarefa, PrazoEvento, TrialExpirando, Geral, NovoAndamento }

public enum TipoPublicacao { Prazo, Audiencia, Decisao, Despacho, Intimacao, Outro }

public enum StatusPublicacao { Nova, Lida, Arquivada }

public enum FonteCaptura { DJe = 0, Escavador = 1, Manual = 2 }

public enum TipoDje { Djus, Djen, Dou }

public enum StatusPrazo { Pendente, Cumprido, Perdido, Suspenso }

public enum TipoCalculo { DiasUteis, DiasCorridos }

public enum TipoLancamento { Receita, Despesa }

public enum StatusLancamento { Pendente, Pago, Vencido, Cancelado }

public enum EntidadeTipo { Documento = 0, Contato = 1 }

public enum TipoDocumento { Peticao, Decisao, Contrato, Prova, Modelo, Outro }

public enum TipoIndice { IPCA, IGPM, TJSP }

public enum FormaPagamentoContrato { AVista = 1, Parcelado = 2, EntradaParcelado = 3 }

public enum PeriodicidadeParcela { Mensal = 1, Quinzenal = 2, Semanal = 3, Semestral = 4 }

public enum StatusContratoHonorario { Ativo = 1, Suspenso = 2, Quitado = 3, Inadimplente = 4, Encerrado = 5, Distratado = 6 }

public enum StatusParcelaHonorario { Pendente = 1, Pago = 2, Vencido = 3, Cancelado = 4 }

public enum EventoContratoHonorario { Criado = 1, Alterado = 2, ParcelaPaga = 3, ParcelaCancelada = 4, Suspenso = 5, Reativado = 6, Distratado = 7, Renegociado = 8 }

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
