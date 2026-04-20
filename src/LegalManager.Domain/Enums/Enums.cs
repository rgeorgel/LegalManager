namespace LegalManager.Domain.Enums;

public enum PlanoTipo { Smart, Pro, Enterprise }

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
