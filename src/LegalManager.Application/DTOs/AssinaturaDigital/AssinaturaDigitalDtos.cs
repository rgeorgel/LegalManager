using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.AssinaturaDigital;

/// <summary>Estado da conexão com o Contratos. Quem não é Admin só recebe <see cref="Conectado"/>.</summary>
public record IntegracaoAssinaturaDto(
    bool Conectado,
    bool Disponivel,
    bool PodeConfigurar,
    string? Organizacao = null,
    string? Plano = null,
    string? ChaveFinal = null,
    bool TemSegredoWebhook = false,
    string? WebhookUrl = null,
    DateTime? ConectadoEm = null,
    DateTime? UltimoWebhookEm = null,
    UsoContratosDto? Uso = null,
    string? AvisoUso = null,
    string? ContratosUrl = null,
    string? OrigemEmbed = null,
    bool WebhookAutomatico = false,
    bool EmbedAutomatico = false,
    ConfiguracaoAutomaticaDto? Configuracao = null);

/// <summary>
/// Resultado de "conectar sem copiar URL e segredo". Webhook e Embed: "configurado" (o Causify
/// cadastrou no Contratos), "manual" (o Contratos ainda não aceita pela API: siga os passos),
/// "indisponivel" (endereço do Causify sem https) ou "erro" (motivo em Avisos).
/// </summary>
public record ConfiguracaoAutomaticaDto(string Webhook, string Embed, List<string> Avisos);

public record UsoContratosDto(int Usados, int? Limite, int Creditos, int EnviosDisponiveis, bool PagamentoPendente);

public record ConectarAssinaturaDto(string? ApiKey, string? WebhookSecret);

public record ModeloAssinaturaDto(string Id, string Nome, string? Descricao, int Versao, int Variaveis);

public record FonteAssinaturaDto(string Chave, string Rotulo, string Grupo);

public record VariavelPreparadaDto(
    string Chave, string Rotulo, string Tipo, bool Obrigatoria, string? Fonte, string? Valor);

public record SignatarioEnvioDto(
    string Nome,
    string Canal,
    string? Email,
    string? Telefone,
    string? Cpf,
    string? Papel,
    string? Acao = null,
    int? Ordem = null);

/// <summary>Tudo o que o modal de envio precisa, já preenchido com os dados do contrato de honorários.</summary>
public record EnvioPreparadoDto(
    string TemplateId,
    string TemplateNome,
    int TemplateVersao,
    string Titulo,
    IReadOnlyList<VariavelPreparadaDto> Variaveis,
    IReadOnlyList<SignatarioEnvioDto> Signatarios,
    IReadOnlyList<FonteAssinaturaDto> Fontes,
    IReadOnlyDictionary<string, string?> ValoresFontes);

/// <summary>De onde sai o envio: um contrato de honorários, um contato ou um processo (um dos três).</summary>
public record CriarSolicitacaoAssinaturaDto(
    Guid? ContratoHonorarioId,
    string TemplateId,
    string? Titulo,
    Dictionary<string, string?> Variaveis,
    Dictionary<string, string?>? Fontes,
    List<SignatarioEnvioDto> Signatarios,
    bool EmOrdem = false,
    DateOnly? Prazo = null,
    string? Mensagem = null,
    bool Enviar = true,
    Guid? ContatoId = null,
    Guid? ProcessoId = null);

/// <summary>Snapshot de um signatário, como veio do Contratos (também é o formato guardado em JSON).</summary>
public record SignatarioAssinaturaDto(
    string Id,
    string Nome,
    string? Email,
    string? Telefone,
    string Canal,
    string? Papel,
    string Status,
    DateTime? VistoEm,
    DateTime? AssinadoEm,
    DateTime? RecusadoEm,
    string? MotivoRecusa,
    string? Link = null);

public record SolicitacaoAssinaturaDto(
    Guid Id,
    Guid? ContratoHonorarioId,
    string ContratoExternoId,
    int? Numero,
    string Titulo,
    string TemplateNome,
    StatusSolicitacaoAssinatura Status,
    IReadOnlyList<SignatarioAssinaturaDto> Signatarios,
    int Assinados,
    string? Erro,
    string? MotivoRecusa,
    Guid? DocumentoAssinadoId,
    string? CriadoPor,
    DateTime CriadoEm,
    DateTime? EnviadaEm,
    DateTime? ConcluidaEm,
    DateTime? ExpiraEm);

/// <summary>
/// Painel "Assinaturas" do escritório. Situação: "pendentes" (enviadas, aguardando alguém),
/// "rascunhos", "atencao" (recusadas, expiradas ou com falha de envio, últimos 60 dias),
/// "concluidas" ou "todas". Contagens traz o total de cada situação, para as abas.
/// </summary>
public record PainelAssinaturasDto(
    Dictionary<string, int> Contagens,
    List<ItemPainelAssinaturaDto> Itens,
    int Total,
    bool TemMais);

/// <summary>Origem: "contrato", "processo" ou "contato". Aguardando: quem ainda não assinou (na ordem).</summary>
public record ItemPainelAssinaturaDto(
    SolicitacaoAssinaturaDto Solicitacao,
    string Origem,
    string? OrigemTitulo,
    string? ClienteNome,
    string Url,
    List<string> Aguardando);

/// <summary>Selo de assinatura nos cards da lista de contratos de honorários (último envio não cancelado).</summary>
public record ResumoAssinaturaDto(StatusSolicitacaoAssinatura Status, int Assinados, int Total);

/// <summary>
/// O que o cliente vê no portal: sem e-mail/telefone dos outros signatários. <see cref="MinhaSituacao"/> e
/// <see cref="LinkAssinatura"/> vêm do signatário que corresponde ao contato (e-mail ou celular).
/// </summary>
public record AssinaturaPortalDto(
    Guid Id,
    string Titulo,
    StatusSolicitacaoAssinatura Status,
    int Assinados,
    int Total,
    DateTime? EnviadaEm,
    DateTime? ConcluidaEm,
    DateTime? ExpiraEm,
    string? MinhaSituacao,
    string? Canal,
    string? LinkAssinatura,
    bool TemPdf);

public record CriarSessaoEmbedDto(string? TemplateId = null);

/// <summary>
/// Sessão do editor de templates do Contratos para abrir num iframe. <see cref="Url"/> abre uma vez só
/// (até 10 minutos); <see cref="Origem"/> é a origem que o navegador deve conferir nos postMessage.
/// </summary>
public record SessaoEmbedDto(string Id, string Url, string Origem, IReadOnlyList<string> Permissoes, DateTime? ExpiraEm);
