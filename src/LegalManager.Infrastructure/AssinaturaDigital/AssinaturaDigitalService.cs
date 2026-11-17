using System.Text.Json;
using Hangfire;
using LegalManager.Application.DTOs.AssinaturaDigital;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.AssinaturaDigital;

/// <summary>Erro de regra da integração, com a mensagem para o usuário e o status HTTP sugerido.</summary>
public sealed class AssinaturaDigitalException(string mensagem, int status = 400) : Exception(mensagem)
{
    public int Status { get; } = status;
}

public enum ResultadoWebhook { Processado, Ignorado, Repetido, IntegracaoInexistente, AssinaturaInvalida, CorpoInvalido }

/// <summary>De onde sai o envio: contrato de honorários, contato ou processo — exatamente um.</summary>
public sealed record OrigemAssinatura(Guid? ContratoHonorarioId = null, Guid? ContatoId = null, Guid? ProcessoId = null)
{
    public static OrigemAssinatura DoContrato(Guid id) => new(ContratoHonorarioId: id);

    public void Validar()
    {
        var informadas = new[] { ContratoHonorarioId, ContatoId, ProcessoId }.Count(x => x is { } g && g != Guid.Empty);
        if (informadas != 1)
            throw new AssinaturaDigitalException("Informe o contrato de honorários, o contato ou o processo do envio.");
    }
}

/// <summary>
/// Assinatura eletrônica pelo Contratos: conexão por chave de API do escritório, envio de contrato a
/// partir de um template preenchido com os dados do contrato de honorários, acompanhamento por webhook
/// (e conferência periódica) e o PDF assinado salvo em Documentos.
/// <para>As mudanças (assinou, recusou, concluiu…) saem da comparação entre o estado guardado e o novo —
/// webhook repetido, fora de ordem ou a conferência periódica não geram aviso em dobro.</para>
/// </summary>
public class AssinaturaDigitalService(
    AppDbContext db,
    IContratosApi api,
    CifraSegredos cifra,
    IStorageService storage,
    IBackgroundJobClient jobs,
    IConfiguration config,
    ILogger<AssinaturaDigitalService> logger)
{
    public const string RotaWebhook = "/api/assinatura-digital/webhook/";
    private const int MaxConferencias = 300;
    private static readonly TimeSpan JanelaConferencia = TimeSpan.FromDays(120);
    private static readonly TimeSpan IntervaloConferencia = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetencaoEventos = TimeSpan.FromDays(30);

    private static readonly StatusSolicitacaoAssinatura[] EmAberto =
        [StatusSolicitacaoAssinatura.Rascunho, StatusSolicitacaoAssinatura.Enviada, StatusSolicitacaoAssinatura.ParcialmenteAssinada];

    /// <summary>O servidor tem endereço do Contratos e chave de cifragem configurados.</summary>
    public bool Disponivel => cifra.Disponivel && !string.IsNullOrWhiteSpace(config[ContratosHttpClient.ConfigBaseUrl]);

    private string? ContratosUrl => config[ContratosHttpClient.ConfigBaseUrl]?.TrimEnd('/') is { Length: > 0 } u ? u : null;

    /// <summary>Origem do Causify (App:FrontendUrl): o site que precisa estar cadastrado no embed do Contratos.</summary>
    public string? OrigemCausify => Origem(config["App:FrontendUrl"]);

    /// <summary>"https://app.x.com.br/algo" → "https://app.x.com.br" (com a porta, se houver).</summary>
    public static string? Origem(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? u.GetLeftPart(UriPartial.Authority)
            : null;

    public string WebhookUrl(IntegracaoAssinatura i) =>
        $"{(config["App:FrontendUrl"] ?? "").TrimEnd('/')}{RotaWebhook}{i.Id}";

    public Task<IntegracaoAssinatura?> IntegracaoAsync(Guid tenantId, CancellationToken ct = default) =>
        db.IntegracoesAssinatura.FirstOrDefaultAsync(i => i.TenantId == tenantId, ct);

    // ── Conexão ────────────────────────────────────────────────────────────────────────

    public async Task<IntegracaoAssinaturaDto> ObterAsync(Guid tenantId, bool admin, CancellationToken ct = default)
    {
        var integ = await IntegracaoAsync(tenantId, ct);
        if (integ == null || !admin)
            return new IntegracaoAssinaturaDto(integ != null, Disponivel, admin, ContratosUrl: ContratosUrl);

        UsoContratosDto? uso = null;
        string? aviso = null;
        try
        {
            var conta = await api.ObterContaAsync(cifra.Decifrar(integ.ApiKeyCifrada), ct);
            var u = conta.Usage;
            uso = new UsoContratosDto(u.ContractsUsed, u.ContractsLimit, u.CreditsAvailable, u.AvailableSends, u.PaymentOverdue);
            if (integ.OrganizacaoNome != conta.Organization.Name || integ.PlanoNome != conta.Organization.PlanName)
            {
                integ.OrganizacaoNome = Cortar(conta.Organization.Name, 200);
                integ.PlanoNome = conta.Organization.PlanName;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (ContratosApiException e)
        {
            aviso = e.Mensagem;
        }

        return new IntegracaoAssinaturaDto(true, Disponivel, true, integ.OrganizacaoNome, integ.PlanoNome,
            integ.ApiKeyFinal, integ.WebhookSecretCifrado != null, WebhookUrl(integ), integ.ConectadoEm,
            integ.UltimoWebhookEm, uso, aviso, ContratosUrl, OrigemCausify);
    }

    public async Task<IntegracaoAssinatura> ConectarAsync(Guid tenantId, Guid usuarioId, ConectarAssinaturaDto dto, CancellationToken ct = default)
    {
        if (!Disponivel)
            throw new AssinaturaDigitalException("A integração com o Contratos ainda não está disponível neste servidor.", 503);

        var integ = await IntegracaoAsync(tenantId, ct);
        var chave = dto.ApiKey?.Trim();
        if (!string.IsNullOrEmpty(chave))
        {
            if (!chave.StartsWith("ctr_", StringComparison.Ordinal) || chave.Length is < 12 or > 200 || chave.Any(char.IsWhiteSpace))
                throw new AssinaturaDigitalException("A chave de API do Contratos começa com ctr_. Copie-a em Integrações → Chaves de API.");

            ContratosConta conta;
            try { conta = await api.ObterContaAsync(chave, ct); }
            catch (ContratosApiException e) when (e.Status == 401)
            { throw new AssinaturaDigitalException("O Contratos não reconheceu a chave: confira se ela foi copiada inteira e não foi revogada."); }
            catch (ContratosApiException e)
            { throw new AssinaturaDigitalException(e.Mensagem, e.Status >= 500 ? 502 : 400); }

            if (integ == null)
            {
                integ = new IntegracaoAssinatura { Id = Guid.NewGuid(), TenantId = tenantId };
                db.IntegracoesAssinatura.Add(integ);
            }
            integ.ApiKeyCifrada = cifra.Cifrar(chave);
            integ.ApiKeyFinal = chave[^4..];
            integ.OrganizacaoNome = Cortar(conta.Organization.Name, 200);
            integ.PlanoNome = conta.Organization.PlanName;
            integ.ConectadoPorId = usuarioId;
            integ.ConectadoEm = DateTime.UtcNow;
        }
        else if (integ == null)
        {
            throw new AssinaturaDigitalException("Cole a chave de API criada no Contratos.");
        }

        var segredo = dto.WebhookSecret?.Trim();
        if (!string.IsNullOrEmpty(segredo))
        {
            if (!segredo.StartsWith("whsec_", StringComparison.Ordinal) || segredo.Length is < 12 or > 200 || segredo.Any(char.IsWhiteSpace))
                throw new AssinaturaDigitalException("O segredo do webhook começa com whsec_. Ele aparece no Contratos ao cadastrar o webhook.");
            integ.WebhookSecretCifrado = cifra.Cifrar(segredo);
        }

        await db.SaveChangesAsync(ct);
        return integ;
    }

    public async Task<bool> DesconectarAsync(Guid tenantId, CancellationToken ct = default)
    {
        var integ = await IntegracaoAsync(tenantId, ct);
        if (integ == null) return false;
        db.IntegracoesAssinatura.Remove(integ);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Modelos e preparação do envio ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<ModeloAssinaturaDto>> ListarModelosAsync(Guid tenantId, CancellationToken ct = default)
    {
        var chave = await ChaveAsync(tenantId, ct);
        var lista = await Chamar(() => api.ListarTemplatesAsync(chave, ct));
        return lista.Select(t => new ModeloAssinaturaDto(t.Id, t.Name, t.Description, t.Version, t.VariableCount)).ToList();
    }

    public async Task<EnvioPreparadoDto> PrepararAsync(Guid tenantId, Guid usuarioId, OrigemAssinatura origem, string templateId, CancellationToken ct = default)
    {
        var chave = await ChaveAsync(tenantId, ct);
        var ctx = await ContextoAsync(tenantId, usuarioId, origem, ct);
        var tpl = await Chamar(() => api.ObterTemplateAsync(chave, templateId, ct));

        var salvas = await FontesSalvasAsync(tenantId, templateId, ct);
        var sugeridas = FontesAssinatura.Sugerir(tpl.Variables);
        var variaveis = tpl.Variables.Select(v =>
        {
            var fonte = salvas.TryGetValue(v.Key, out var s) && FontesAssinatura.Existe(s) ? s : sugeridas.GetValueOrDefault(v.Key);
            if (fonte != null && !FontesAssinatura.Disponivel(fonte, ctx)) fonte = null;
            return new VariavelPreparadaDto(v.Key, v.Label, v.Type, v.Required, fonte,
                fonte != null ? FontesAssinatura.Valor(fonte, ctx) : null);
        }).ToList();

        var fontes = FontesAssinatura.Catalogo.Where(f => FontesAssinatura.Disponivel(f.Chave, ctx)).ToList();
        var assunto = ctx.Cliente?.Nome ?? ctx.Processo?.NumeroCNJ;
        return new EnvioPreparadoDto(tpl.Id, tpl.Name, tpl.Version,
            Cortar(string.IsNullOrWhiteSpace(assunto) ? tpl.Name : $"{tpl.Name}: {assunto}", 200),
            variaveis, SignatariosSugeridos(ctx, tpl.SuggestedSigners), fontes,
            fontes.ToDictionary(f => f.Chave, f => FontesAssinatura.Valor(f.Chave, ctx)));
    }

    internal static List<SignatarioEnvioDto> SignatariosSugeridos(ContextoAssinatura ctx, List<ContratosSignatarioSugerido>? sugeridos)
    {
        var papeis = (sugeridos ?? []).OrderBy(s => s.Order ?? int.MaxValue).ToList();
        var advogado = new SignatarioEnvioDto(ctx.Advogado.Nome, "email", ctx.Advogado.Email, null, null,
            papeis.ElementAtOrDefault(1)?.Role ?? "Contratado", papeis.ElementAtOrDefault(1)?.Action ?? "sign", 2);
        if (ctx.Cliente is not { } cliente) return [advogado with { Ordem = 1 }];

        var telefone = FontesAssinatura.TelefoneInternacional(cliente.Telefone);
        var canal = !string.IsNullOrWhiteSpace(cliente.Email) ? "email" : telefone != null ? "whatsapp" : "email";
        return
        [
            new(cliente.Nome, canal, cliente.Email, telefone, FontesAssinatura.CpfDoSignatario(cliente),
                papeis.ElementAtOrDefault(0)?.Role ?? "Contratante", papeis.ElementAtOrDefault(0)?.Action ?? "sign", 1),
            advogado,
        ];
    }

    // ── Embed de templates ────────────────────────────────────────────────────────────

    private const int DuracaoSessaoEmbedMinutos = 60;

    /// <summary>
    /// O que cada perfil pode fazer no editor de templates do Contratos: Admin edita, publica e arquiva;
    /// Advogado edita e publica; Colaborador só edita (rascunhos, que alguém publica depois).
    /// </summary>
    public static List<string> PermissoesEmbed(PerfilUsuario perfil) => perfil switch
    {
        PerfilUsuario.Admin => ["edit", "publish", "archive"],
        PerfilUsuario.Advogado => ["edit", "publish"],
        PerfilUsuario.Colaborador => ["edit"],
        _ => [],
    };

    /// <summary>
    /// Cria a sessão do editor de templates para abrir num iframe. A chave de API fica no servidor;
    /// o navegador recebe só a URL (abre uma vez, até 10 minutos).
    /// </summary>
    public async Task<SessaoEmbedDto> CriarSessaoEmbedAsync(Guid tenantId, Guid usuarioId, string? templateId, CancellationToken ct = default)
    {
        var chave = await ChaveAsync(tenantId, ct);
        templateId = string.IsNullOrWhiteSpace(templateId) ? null : templateId.Trim();
        if (templateId != null && !Guid.TryParse(templateId, out _))
            throw new AssinaturaDigitalException("Template inválido.");
        var usuario = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId && u.TenantId == tenantId, ct)
            ?? throw new AssinaturaDigitalException("Usuário não encontrado.", 404);
        var origemCausify = OrigemCausify
            ?? throw new AssinaturaDigitalException("O endereço do Causify (App:FrontendUrl) não está configurado neste servidor.", 503);

        var permissoes = PermissoesEmbed(usuario.Perfil);
        ContratosSessaoEmbed sessao;
        try
        {
            sessao = await api.CriarSessaoEmbedAsync(chave, new ContratosNovaSessaoEmbed(
                permissoes, new ContratosUsuarioEmbed(usuario.Id.ToString(), Cortar(usuario.Nome, 120)),
                origemCausify, DuracaoSessaoEmbedMinutos, templateId), ct);
        }
        catch (ContratosApiException e) when (e.Codigo?.StartsWith("embed_", StringComparison.Ordinal) == true || e.Status is 400 or 422)
        {
            // O motivo mais comum: o site do Causify não está cadastrado no embed do Contratos
            // (409 embed_origin_required quando a conta não tem nenhum site cadastrado).
            var erro = Traduzir(e);
            throw new AssinaturaDigitalException(
                $"{erro.Message} Confira se {origemCausify} está cadastrado no Contratos em Integrações → Embed de templates.", erro.Status);
        }
        catch (ContratosApiException e)
        {
            throw Traduzir(e);
        }

        // A URL precisa ser do próprio Contratos configurado: é ela que vai para o iframe.
        var origemSessao = Origem(sessao.Url);
        if (origemSessao == null || !string.Equals(origemSessao, Origem(ContratosUrl), StringComparison.OrdinalIgnoreCase))
            throw new AssinaturaDigitalException("O Contratos devolveu um endereço de editor inesperado.", 502);

        return new SessaoEmbedDto(sessao.Id, sessao.Url, origemSessao, sessao.Permissions ?? permissoes, sessao.ExpiresAt?.UtcDateTime);
    }

    /// <summary>Encerra a sessão do editor (a pessoa fechou a tela). Sessão já vencida ou inexistente: nada a fazer.</summary>
    public async Task EncerrarSessaoEmbedAsync(Guid tenantId, string sessaoId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(sessaoId, out _)) throw new AssinaturaDigitalException("Sessão inválida.");
        var chave = await ChaveAsync(tenantId, ct);
        try { await api.EncerrarSessaoEmbedAsync(chave, sessaoId, ct); }
        catch (ContratosApiException e) when (e.Status == 404) { }
        catch (ContratosApiException e) { throw Traduzir(e); }
    }

    // ── Envio e ações ─────────────────────────────────────────────────────────────────

    public async Task<SolicitacaoAssinatura> CriarAsync(Guid tenantId, Guid usuarioId, CriarSolicitacaoAssinaturaDto dto, CancellationToken ct = default)
    {
        var chave = await ChaveAsync(tenantId, ct);
        var ctx = await ContextoAsync(tenantId, usuarioId, new OrigemAssinatura(dto.ContratoHonorarioId, dto.ContatoId, dto.ProcessoId), ct);
        if (string.IsNullOrWhiteSpace(dto.TemplateId)) throw new AssinaturaDigitalException("Escolha o template.");
        var signatarios = ValidarSignatarios(dto.Signatarios);
        if (dto.Prazo is { } p && p < DateOnly.FromDateTime(ctx.Hoje))
            throw new AssinaturaDigitalException("O prazo para assinar não pode ser no passado.");

        var id = Guid.NewGuid();
        var novo = new ContratosNovoContrato(
            dto.TemplateId.Trim(),
            (dto.Variaveis ?? []).Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value!.Trim()),
            signatarios,
            string.IsNullOrWhiteSpace(dto.Titulo) ? null : Cortar(dto.Titulo.Trim(), 200),
            id.ToString(),
            dto.EmOrdem ? "sequential" : "parallel",
            dto.Prazo?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(dto.Mensagem) ? null : Cortar(dto.Mensagem.Trim(), 500),
            dto.Enviar);

        ContratosContrato contrato;
        string? erroEnvio = null;
        try
        {
            contrato = await api.CriarContratoAsync(chave, novo, $"causify-{id}", ct);
        }
        catch (ContratosApiException e) when (e.ContratoId != null)
        {
            // Criou mas não enviou (ex.: sem cota): o rascunho existe no Contratos.
            erroEnvio = e.Mensagem;
            contrato = await Chamar(() => api.ObterContratoAsync(chave, e.ContratoId, ct));
        }
        catch (ContratosApiException e)
        {
            throw Traduzir(e);
        }

        var sol = new SolicitacaoAssinatura
        {
            Id = id,
            TenantId = tenantId,
            ContratoHonorarioId = ctx.Contrato?.Id,
            ContatoId = ctx.Cliente?.Id,
            ProcessoId = ctx.Processo?.Id,
            ContratoExternoId = contrato.Id,
            TemplateId = dto.TemplateId.Trim(),
            TemplateNome = Cortar(contrato.Template?.Name ?? "", 200),
            CriadoPorId = usuarioId,
            CriadoEm = DateTime.UtcNow,
        };
        Aplicar(sol, contrato);
        if (erroEnvio != null) sol.Erro = Cortar(erroEnvio, 1000);
        db.SolicitacoesAssinatura.Add(sol);

        await SalvarFontesAsync(tenantId, sol.TemplateId, dto.Fontes, ct);

        var nomes = string.Join(", ", signatarios.Select(s => s.Name));
        Historico(sol, usuarioId, sol.Status == StatusSolicitacaoAssinatura.Rascunho
            ? $"Rascunho de assinatura criado no Contratos (nº {sol.NumeroExterno}) para {nomes}."
            : $"Enviado para assinatura pelo Contratos (nº {sol.NumeroExterno}): {nomes}.");

        await db.SaveChangesAsync(ct);
        return sol;
    }

    internal static List<ContratosNovoSignatario> ValidarSignatarios(List<SignatarioEnvioDto>? lista)
    {
        if (lista == null || lista.Count == 0) throw new AssinaturaDigitalException("Inclua pelo menos um signatário.");
        if (lista.Count > 30) throw new AssinaturaDigitalException("O Contratos aceita até 30 signatários por contrato.");

        var r = new List<ContratosNovoSignatario>();
        for (var i = 0; i < lista.Count; i++)
        {
            var s = lista[i];
            var nome = s.Nome?.Trim();
            if (string.IsNullOrEmpty(nome)) throw new AssinaturaDigitalException($"Informe o nome do {i + 1}º signatário.");
            var canal = (s.Canal ?? "email").Trim().ToLowerInvariant();
            if (canal is not ("email" or "whatsapp" or "sms"))
                throw new AssinaturaDigitalException($"Canal inválido para {nome}.");
            var email = string.IsNullOrWhiteSpace(s.Email) ? null : s.Email.Trim();
            string? telefone = null;
            if (canal == "email" && email == null)
                throw new AssinaturaDigitalException($"Informe o e-mail de {nome}, ou envie por WhatsApp.");
            if (canal != "email")
            {
                telefone = FontesAssinatura.TelefoneInternacional(s.Telefone)
                    ?? throw new AssinaturaDigitalException($"Informe o celular de {nome} com DDD, ex.: (11) 98765-4321.");
            }
            var cpf = string.IsNullOrWhiteSpace(s.Cpf) ? null : new string(s.Cpf.Where(char.IsDigit).ToArray());
            if (cpf is { Length: not 11 }) throw new AssinaturaDigitalException($"O CPF de {nome} precisa ter 11 dígitos.");
            var acao = string.IsNullOrWhiteSpace(s.Acao) ? "sign" : s.Acao.Trim();
            if (acao is not ("sign" or "sign_as_witness" or "approve" or "recognize"))
                throw new AssinaturaDigitalException($"Ação inválida para {nome}.");
            r.Add(new ContratosNovoSignatario(Cortar(nome, 100), canal, email, telefone, cpf,
                string.IsNullOrWhiteSpace(s.Papel) ? null : Cortar(s.Papel.Trim(), 100), acao, s.Ordem ?? i + 1));
        }
        return r;
    }

    public async Task<SolicitacaoAssinatura> AtualizarAsync(Guid tenantId, Guid solicitacaoId, CancellationToken ct = default)
    {
        var (sol, chave) = await SolicitacaoAsync(tenantId, solicitacaoId, ct);
        var contrato = await Chamar(() => api.ObterContratoAsync(chave, sol.ContratoExternoId, ct));
        AplicarComMudancas(sol, contrato, null);
        sol.ConferidoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        AgendarDownloadSePronto(sol, contrato.HasSignedDocument);
        return sol;
    }

    public async Task<SolicitacaoAssinatura> EnviarRascunhoAsync(Guid tenantId, Guid usuarioId, Guid solicitacaoId, CancellationToken ct = default)
    {
        var (sol, chave) = await SolicitacaoAsync(tenantId, solicitacaoId, ct);
        if (sol.Status != StatusSolicitacaoAssinatura.Rascunho)
            throw new AssinaturaDigitalException("Este contrato já foi enviado.", 409);
        ContratosContrato contrato;
        try { contrato = await api.EnviarAsync(chave, sol.ContratoExternoId, $"causify-envio-{sol.Id}", ct); }
        catch (ContratosApiException e) when (e.Codigo is "already_final" or "already_sent")
        {
            // Mudou no Contratos (enviado ou cancelado pela tela de lá): traz o estado atual.
            contrato = await Chamar(() => api.ObterContratoAsync(chave, sol.ContratoExternoId, ct));
            AplicarComMudancas(sol, contrato, null);
            await db.SaveChangesAsync(ct);
            throw new AssinaturaDigitalException(e.Codigo == "already_final"
                ? "Este contrato já foi finalizado ou cancelado no Contratos e não pode mais ser enviado."
                : "Este contrato já foi enviado pelo Contratos.", 409);
        }
        catch (ContratosApiException e) { throw Traduzir(e); }
        sol.Erro = null;
        AplicarComMudancas(sol, contrato, usuarioId);
        Historico(sol, usuarioId, $"Contrato nº {sol.NumeroExterno} enviado para assinatura.");
        await db.SaveChangesAsync(ct);
        return sol;
    }

    public async Task<SolicitacaoAssinatura> CancelarAsync(Guid tenantId, Guid usuarioId, Guid solicitacaoId, CancellationToken ct = default)
    {
        var (sol, chave) = await SolicitacaoAsync(tenantId, solicitacaoId, ct);
        ContratosContrato contrato;
        try { contrato = await api.CancelarAsync(chave, sol.ContratoExternoId, ct); }
        catch (ContratosApiException e) when (e.Codigo == "already_final")
        {
            contrato = await Chamar(() => api.ObterContratoAsync(chave, sol.ContratoExternoId, ct));
            AplicarComMudancas(sol, contrato, null);
            await db.SaveChangesAsync(ct);
            throw new AssinaturaDigitalException("Este contrato já foi finalizado e não pode mais ser cancelado.", 409);
        }
        catch (ContratosApiException e) { throw Traduzir(e); }
        AplicarComMudancas(sol, contrato, usuarioId);
        await db.SaveChangesAsync(ct);
        return sol;
    }

    public async Task ReenviarLinkAsync(Guid tenantId, Guid solicitacaoId, string signatarioId, CancellationToken ct = default)
    {
        var (sol, chave) = await SolicitacaoAsync(tenantId, solicitacaoId, ct);
        try { await api.ReenviarLinkAsync(chave, sol.ContratoExternoId, signatarioId, ct); }
        catch (ContratosApiException e) { throw Traduzir(e); }
    }

    /// <summary>Envios da origem: do contrato; do contato (inclui os dos contratos e processos dele); do processo.</summary>
    public async Task<List<SolicitacaoAssinaturaDto>> ListarAsync(Guid tenantId, OrigemAssinatura origem, CancellationToken ct = default)
    {
        origem.Validar();
        var q = db.SolicitacoesAssinatura.AsNoTracking().Where(s => s.TenantId == tenantId);
        q = origem.ContratoHonorarioId is { } c ? q.Where(s => s.ContratoHonorarioId == c)
            : origem.ContatoId is { } k ? q.Where(s => s.ContatoId == k)
            : q.Where(s => s.ProcessoId == origem.ProcessoId);
        var lista = await q
            .OrderByDescending(s => s.CriadoEm)
            .Select(s => new { S = s, Criador = s.CriadoPor != null ? s.CriadoPor.Nome : null })
            .ToListAsync(ct);
        return lista.Select(x => ParaDto(x.S, x.Criador)).ToList();
    }

    /// <summary>Último envio não cancelado de cada contrato de honorários (selo nos cards da lista).</summary>
    public async Task<Dictionary<Guid, ResumoAssinaturaDto>> ResumoPorContratoAsync(Guid tenantId, IReadOnlyCollection<Guid> contratoIds, CancellationToken ct = default)
    {
        if (contratoIds.Count == 0) return [];
        if (contratoIds.Count > 200) throw new AssinaturaDigitalException("Peça o resumo de no máximo 200 contratos por vez.");
        var ids = contratoIds.Distinct().ToList();
        var lista = await db.SolicitacoesAssinatura.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ContratoHonorarioId != null && ids.Contains(s.ContratoHonorarioId.Value)
                && s.Status != StatusSolicitacaoAssinatura.Cancelada)
            .Select(s => new { s.ContratoHonorarioId, s.Status, s.SignatariosJson, s.CriadoEm })
            .ToListAsync(ct);
        return lista
            .GroupBy(s => s.ContratoHonorarioId!.Value)
            .ToDictionary(g => g.Key, g =>
            {
                var ultimo = g.OrderByDescending(s => s.CriadoEm).First();
                var sigs = LerSignatarios(ultimo.SignatariosJson);
                return new ResumoAssinaturaDto(ultimo.Status, sigs.Count(x => x.Status == "signed"), sigs.Count);
            });
    }

    public async Task<SolicitacaoAssinaturaDto> DtoAsync(SolicitacaoAssinatura sol, CancellationToken ct = default)
    {
        var criador = sol.CriadoPorId == null ? null
            : await db.Users.Where(u => u.Id == sol.CriadoPorId).Select(u => u.Nome).FirstOrDefaultAsync(ct);
        return ParaDto(sol, criador);
    }

    /// <summary>
    /// Visão do escritório. Sem o signLink: ele abre a assinatura em nome do signatário, e o Contratos
    /// pede para mostrá-lo só à própria pessoa (o portal do cliente usa o link do cliente).
    /// </summary>
    public static SolicitacaoAssinaturaDto ParaDto(SolicitacaoAssinatura s, string? criador)
    {
        var signatarios = LerSignatarios(s.SignatariosJson).Select(x => x with { Link = null }).ToList();
        return new SolicitacaoAssinaturaDto(s.Id, s.ContratoHonorarioId, s.ContratoExternoId, s.NumeroExterno, s.Titulo,
            s.TemplateNome, s.Status, signatarios, signatarios.Count(x => x.Status == "signed"), s.Erro, s.MotivoRecusa,
            s.DocumentoAssinadoId, criador, s.CriadoEm, s.EnviadaEm, s.ConcluidaEm, s.ExpiraEm);
    }

    // ── Portal do cliente ─────────────────────────────────────────────────────────────

    /// <summary>Envios do cliente no portal: em andamento e encerrados no último ano (rascunhos e cancelados ficam de fora).</summary>
    public async Task<List<AssinaturaPortalDto>> DoClienteAsync(Guid tenantId, Guid contatoId, CancellationToken ct = default)
    {
        var contato = await db.Contatos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contatoId && c.TenantId == tenantId, ct);
        if (contato == null) return [];
        var desde = DateTime.UtcNow.AddDays(-365);
        var lista = await db.SolicitacoesAssinatura.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ContatoId == contatoId && s.CriadoEm >= desde
                && s.Status != StatusSolicitacaoAssinatura.Rascunho && s.Status != StatusSolicitacaoAssinatura.Cancelada)
            .OrderByDescending(s => s.CriadoEm)
            .Take(50)
            .ToListAsync(ct);
        return lista.Select(s => ParaPortal(s, contato)).ToList();
    }

    internal static AssinaturaPortalDto ParaPortal(SolicitacaoAssinatura s, Contato contato)
    {
        var sigs = LerSignatarios(s.SignatariosJson);
        var eu = SignatarioDoContato(sigs, contato);
        var aberto = s.Status is StatusSolicitacaoAssinatura.Enviada or StatusSolicitacaoAssinatura.ParcialmenteAssinada;
        var link = aberto && eu is { Status: "pending" or "viewed" } ? eu.Link : null;
        return new AssinaturaPortalDto(s.Id, s.Titulo, s.Status, sigs.Count(x => x.Status == "signed"), sigs.Count,
            s.EnviadaEm, s.ConcluidaEm, s.ExpiraEm, eu?.Status, eu?.Canal, link, s.DocumentoAssinadoId != null);
    }

    /// <summary>Qual signatário é o contato: pelo e-mail, depois pelo celular, por fim pelo nome.</summary>
    internal static SignatarioAssinaturaDto? SignatarioDoContato(List<SignatarioAssinaturaDto> sigs, Contato contato)
    {
        static string Digitos(string? t) => new((t ?? "").Where(char.IsDigit).ToArray());
        var email = contato.Email?.Trim();
        if (!string.IsNullOrEmpty(email)
            && sigs.FirstOrDefault(s => string.Equals(s.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase)) is { } porEmail)
            return porEmail;
        var tel = Digitos(contato.Telefone);
        if (tel.Length >= 10 && sigs.FirstOrDefault(s => Digitos(s.Telefone) is { Length: >= 10 } d && d.EndsWith(tel[^10..])) is { } porTelefone)
            return porTelefone;
        var nome = contato.Nome.Trim();
        return sigs.FirstOrDefault(s => string.Equals(s.Nome.Trim(), nome, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>O PDF assinado de um envio do cliente, para baixar pelo portal.</summary>
    public async Task<(Stream Conteudo, string Nome)?> PdfDoClienteAsync(Guid tenantId, Guid contatoId, Guid solicitacaoId, CancellationToken ct = default)
    {
        var docId = await db.SolicitacoesAssinatura.AsNoTracking()
            .Where(s => s.Id == solicitacaoId && s.TenantId == tenantId && s.ContatoId == contatoId)
            .Select(s => s.DocumentoAssinadoId)
            .FirstOrDefaultAsync(ct);
        if (docId == null) return null;
        var doc = await db.Documentos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId && d.TenantId == tenantId, ct);
        if (doc == null) return null;
        return (await storage.DownloadAsync(doc.ObjectKey, ct), doc.Nome);
    }

    // ── Webhook ───────────────────────────────────────────────────────────────────────

    public async Task<ResultadoWebhook> ProcessarWebhookAsync(Guid integracaoId, string? assinatura, byte[] corpo, CancellationToken ct = default)
    {
        var integ = await db.IntegracoesAssinatura.FirstOrDefaultAsync(i => i.Id == integracaoId, ct);
        if (integ == null) return ResultadoWebhook.IntegracaoInexistente;
        if (integ.WebhookSecretCifrado == null)
        {
            logger.LogWarning("Webhook do Contratos recebido sem segredo cadastrado no Causify (tenant {TenantId})", integ.TenantId);
            return ResultadoWebhook.AssinaturaInvalida;
        }
        if (!WebhookContratos.AssinaturaValida(assinatura, corpo, cifra.Decifrar(integ.WebhookSecretCifrado), DateTimeOffset.UtcNow))
            return ResultadoWebhook.AssinaturaInvalida;

        var evento = WebhookContratos.Ler(corpo);
        if (evento == null || string.IsNullOrWhiteSpace(evento.Id) || evento.Id.Length > 100 || string.IsNullOrWhiteSpace(evento.Type))
            return ResultadoWebhook.CorpoInvalido;

        integ.UltimoWebhookEm = DateTime.UtcNow;
        if (await db.EventosAssinaturaRecebidos.AnyAsync(e => e.Id == evento.Id, ct))
        {
            await db.SaveChangesAsync(ct);
            return ResultadoWebhook.Repetido;
        }
        db.EventosAssinaturaRecebidos.Add(new EventoAssinaturaRecebido
        {
            Id = evento.Id, TenantId = integ.TenantId, Tipo = Cortar(evento.Type, 60), RecebidoEm = DateTime.UtcNow,
        });

        var resultado = ResultadoWebhook.Ignorado;
        SolicitacaoAssinatura? sol = null;
        var contrato = evento.Data?.Contract;
        if (contrato != null && !string.IsNullOrEmpty(contrato.Id))
        {
            contrato = contrato with
            {
                Signers = (contrato.Signers ?? []).Select(s => s with { DeliveryMethod = ContratosHttpClient.NormalizarCanal(s.DeliveryMethod) }).ToList(),
            };
            sol = await db.SolicitacoesAssinatura
                .FirstOrDefaultAsync(s => s.TenantId == integ.TenantId && s.ContratoExternoId == contrato.Id, ct);
            if (sol != null)
            {
                // O motivo da falha de envio vem em data.reason; entra antes da comparação para gerar o aviso.
                if (evento.Type == "contract.send_failed" && string.IsNullOrWhiteSpace(contrato.SendError)
                    && !string.IsNullOrWhiteSpace(evento.Data!.Reason))
                    contrato = contrato with { SendError = evento.Data.Reason };
                AplicarComMudancas(sol, contrato, null);
                if (evento.Type == "contract.declined" && !string.IsNullOrWhiteSpace(evento.Data!.Reason))
                    sol.MotivoRecusa = Cortar(evento.Data.Reason, 1000);
                resultado = ResultadoWebhook.Processado;
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Duas entregas do mesmo evento ao mesmo tempo: a outra já gravou.
            return ResultadoWebhook.Repetido;
        }

        if (sol != null)
            AgendarDownloadSePronto(sol, evento.Type == "contract.signed_document_ready" || contrato!.HasSignedDocument);
        return resultado;
    }

    // ── Jobs ──────────────────────────────────────────────────────────────────────────

    /// <summary>Baixa o PDF assinado e guarda como Documento do cliente/processo/contrato.</summary>
    public async Task BaixarAssinadoAsync(Guid solicitacaoId, CancellationToken ct = default)
    {
        var sol = await db.SolicitacoesAssinatura.FirstOrDefaultAsync(s => s.Id == solicitacaoId, ct);
        if (sol == null || sol.DocumentoAssinadoId != null) return;
        var integ = await IntegracaoAsync(sol.TenantId, ct);
        if (integ == null) return;

        byte[] pdf;
        try { pdf = await api.BaixarAssinadoAsync(cifra.Decifrar(integ.ApiKeyCifrada), sol.ContratoExternoId, ct); }
        catch (ContratosApiException e) when (e.Status == 404)
        {
            logger.LogInformation("PDF assinado do contrato {Contrato} ainda não está disponível", sol.ContratoExternoId);
            return;
        }
        if (pdf.Length < 5 || pdf[0] != '%' || pdf[1] != 'P' || pdf[2] != 'D' || pdf[3] != 'F')
            throw new InvalidOperationException($"O Contratos não devolveu um PDF para o contrato {sol.ContratoExternoId}.");

        var agora = DateTime.UtcNow;
        var pasta = sol.ContratoHonorarioId is { } cid ? $"contratos/{cid}"
            : sol.ProcessoId is { } pid ? $"processos/{pid}"
            : sol.ContatoId is { } kid ? $"clientes/{kid}"
            : "documentos";
        var objectKey = $"{sol.TenantId}/{pasta}/{agora:yyyyMMddHHmmss}_contrato_{sol.NumeroExterno}_assinado.pdf";
        using (var stream = new MemoryStream(pdf))
            await storage.UploadAsync(stream, objectKey, "application/pdf", ct);

        // Outro job pode ter terminado enquanto este baixava.
        await db.Entry(sol).ReloadAsync(ct);
        if (sol.DocumentoAssinadoId != null)
        {
            await storage.DeleteAsync(objectKey, ct);
            return;
        }

        var doc = new Documento
        {
            Id = Guid.NewGuid(),
            TenantId = sol.TenantId,
            ClienteId = sol.ContatoId,
            ProcessoId = sol.ProcessoId,
            ContratoId = sol.ContratoHonorarioId,
            Nome = $"{Cortar(sol.Titulo, 180)} (assinado).pdf",
            ObjectKey = objectKey,
            ContentType = "application/pdf",
            TamanhoBytes = pdf.Length,
            Tipo = TipoDocumento.Contrato,
            CriadoEm = agora,
        };
        db.Documentos.Add(doc);
        sol.DocumentoAssinadoId = doc.Id;
        Historico(sol, null, $"PDF assinado do contrato nº {sol.NumeroExterno} salvo em Documentos.");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Backstop do webhook: confere no Contratos as solicitações em aberto (e as concluídas sem PDF),
    /// no máximo uma vez por hora cada, e apaga os ids de evento com mais de 30 dias.
    /// </summary>
    public async Task<int> ConferirPendentesAsync(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var desde = agora - JanelaConferencia;
        var limite = agora - IntervaloConferencia;
        var pendentes = await db.SolicitacoesAssinatura
            .Where(s => s.CriadoEm >= desde
                && (EmAberto.Contains(s.Status) || (s.Status == StatusSolicitacaoAssinatura.Concluida && s.DocumentoAssinadoId == null))
                && (s.ConferidoEm == null || s.ConferidoEm < limite)
                && db.IntegracoesAssinatura.Any(i => i.TenantId == s.TenantId))
            .OrderBy(s => s.ConferidoEm ?? DateTime.MinValue)
            .Take(MaxConferencias)
            .ToListAsync(ct);

        var conferidas = 0;
        foreach (var grupo in pendentes.GroupBy(s => s.TenantId))
        {
            var integ = await IntegracaoAsync(grupo.Key, ct);
            if (integ == null) continue;
            var chave = cifra.Decifrar(integ.ApiKeyCifrada);
            foreach (var sol in grupo)
            {
                try
                {
                    var contrato = await api.ObterContratoAsync(chave, sol.ContratoExternoId, ct);
                    AplicarComMudancas(sol, contrato, null);
                    sol.ConferidoEm = agora;
                    await db.SaveChangesAsync(ct);
                    conferidas++;
                    if (sol.Status == StatusSolicitacaoAssinatura.Concluida && sol.DocumentoAssinadoId == null && contrato.HasSignedDocument)
                        await BaixarAssinadoAsync(sol.Id, ct);
                }
                catch (ContratosApiException e) when (e.Status is 401 or 402 or 403 or 429)
                {
                    logger.LogWarning("Conferência de assinaturas interrompida no tenant {TenantId}: {Status} {Codigo}", grupo.Key, e.Status, e.Codigo);
                    break;
                }
                catch (ContratosApiException e)
                {
                    logger.LogWarning("Falha ao conferir o contrato {Contrato}: {Status} {Codigo}", sol.ContratoExternoId, e.Status, e.Codigo);
                    sol.ConferidoEm = agora;
                    await db.SaveChangesAsync(ct);
                }
            }
        }

        var antigos = await db.EventosAssinaturaRecebidos
            .Where(e => e.RecebidoEm < agora - RetencaoEventos)
            .Take(1000)
            .ToListAsync(ct);
        if (antigos.Count > 0)
        {
            db.EventosAssinaturaRecebidos.RemoveRange(antigos);
            await db.SaveChangesAsync(ct);
        }
        return conferidas;
    }

    // ── Estado e mudanças ─────────────────────────────────────────────────────────────

    public static StatusSolicitacaoAssinatura MapearStatus(string? status) => status switch
    {
        "draft" => StatusSolicitacaoAssinatura.Rascunho,
        "sent" => StatusSolicitacaoAssinatura.Enviada,
        "partially_signed" => StatusSolicitacaoAssinatura.ParcialmenteAssinada,
        "completed" => StatusSolicitacaoAssinatura.Concluida,
        "declined" => StatusSolicitacaoAssinatura.Recusada,
        "expired" => StatusSolicitacaoAssinatura.Expirada,
        "cancelled" => StatusSolicitacaoAssinatura.Cancelada,
        _ => StatusSolicitacaoAssinatura.Enviada,
    };

    /// <summary>Copia o estado do Contratos. Devolve false (sem mudar nada) se o snapshot é mais antigo que o guardado.</summary>
    internal static bool Aplicar(SolicitacaoAssinatura sol, ContratosContrato c)
    {
        var atualizado = c.UpdatedAt?.UtcDateTime;
        if (atualizado != null && sol.AtualizadoExternoEm != null && atualizado < sol.AtualizadoExternoEm) return false;

        sol.ContratoExternoId = c.Id;
        sol.NumeroExterno = c.Number;
        sol.Titulo = Cortar(c.Title ?? sol.Titulo, 200);
        if (c.Template?.Name is { Length: > 0 } nomeTpl) sol.TemplateNome = Cortar(nomeTpl, 200);
        sol.Status = MapearStatus(c.Status);
        sol.SignatariosJson = JsonSerializer.Serialize(
            (c.Signers ?? []).Select(s => new SignatarioAssinaturaDto(s.Id, s.Name, s.Email, s.Phone,
                ContratosHttpClient.NormalizarCanal(s.DeliveryMethod), s.Role, s.Status, s.ViewedAt?.UtcDateTime,
                s.SignedAt?.UtcDateTime, s.DeclinedAt?.UtcDateTime, s.DeclineReason,
                string.IsNullOrWhiteSpace(s.SignLink) ? null : s.SignLink)).ToList(),
            ContratosHttpClient.Json);
        if (!string.IsNullOrWhiteSpace(c.SendError)) sol.Erro = Cortar(c.SendError, 1000);
        else if (sol.Status != StatusSolicitacaoAssinatura.Rascunho) sol.Erro = null;
        var recusa = c.Signers?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.DeclineReason))?.DeclineReason;
        if (recusa != null) sol.MotivoRecusa = Cortar(recusa, 1000);
        sol.EnviadaEm = c.SentAt?.UtcDateTime;
        sol.ConcluidaEm = c.CompletedAt?.UtcDateTime;
        sol.ExpiraEm = c.ExpiresAt?.UtcDateTime;
        sol.AtualizadoExternoEm = atualizado ?? sol.AtualizadoExternoEm;
        return true;
    }

    /// <summary>Aplica o snapshot e registra no histórico do contrato e nas notificações o que mudou.</summary>
    private void AplicarComMudancas(SolicitacaoAssinatura sol, ContratosContrato c, Guid? usuarioId)
    {
        var statusAntes = sol.Status;
        var erroAntes = sol.Erro;
        var antes = LerSignatarios(sol.SignatariosJson).ToDictionary(s => s.Id, s => s.Status);
        if (!Aplicar(sol, c)) return;

        var depois = LerSignatarios(sol.SignatariosJson);
        var total = depois.Count;
        var assinados = depois.Count(s => s.Status == "signed");
        var url = UrlDaOrigem(sol);

        foreach (var s in depois)
        {
            var anterior = antes.GetValueOrDefault(s.Id);
            if (anterior == s.Status) continue;
            switch (s.Status)
            {
                case "signed":
                    Historico(sol, null, $"{s.Nome} assinou o contrato nº {sol.NumeroExterno} ({assinados}/{total}).");
                    if (sol.Status != StatusSolicitacaoAssinatura.Concluida)
                        Notificar(sol, $"{s.Nome} assinou o contrato", $"{sol.Titulo}: {assinados} de {total} assinaturas.", url);
                    break;
                case "declined":
                    Historico(sol, null, $"{s.Nome} recusou o contrato nº {sol.NumeroExterno}." +
                        (string.IsNullOrWhiteSpace(s.MotivoRecusa) ? "" : $" Motivo: {s.MotivoRecusa}"));
                    Notificar(sol, $"{s.Nome} recusou o contrato",
                        string.IsNullOrWhiteSpace(s.MotivoRecusa) ? sol.Titulo : $"{sol.Titulo}. Motivo: {s.MotivoRecusa}", url);
                    break;
                case "delivery_failed":
                    Historico(sol, null, $"O link de assinatura não chegou a {s.Nome}.");
                    Notificar(sol, $"O link não chegou a {s.Nome}",
                        $"{sol.Titulo}: confira o e-mail ou o telefone no Contratos e reenvie o link.", url);
                    break;
            }
        }

        if (sol.Status != statusAntes)
        {
            switch (sol.Status)
            {
                case StatusSolicitacaoAssinatura.Concluida:
                    Historico(sol, null, $"Contrato nº {sol.NumeroExterno} assinado por todos.");
                    Notificar(sol, "Contrato assinado por todos", $"{sol.Titulo}. O PDF assinado vai para Documentos.", url);
                    break;
                case StatusSolicitacaoAssinatura.Expirada:
                    Historico(sol, null, $"O prazo para assinar o contrato nº {sol.NumeroExterno} acabou ({assinados}/{total} assinaturas).");
                    Notificar(sol, "Prazo de assinatura encerrado", $"{sol.Titulo}: {assinados} de {total} assinaturas.", url);
                    break;
                case StatusSolicitacaoAssinatura.Cancelada:
                    Historico(sol, usuarioId, $"Assinatura do contrato nº {sol.NumeroExterno} cancelada.");
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(sol.Erro) && sol.Erro != erroAntes && sol.Status != StatusSolicitacaoAssinatura.Rascunho)
            Notificar(sol, "Falha ao enviar o contrato para assinatura", $"{sol.Titulo}: {sol.Erro}", url);
    }

    internal static string UrlDaOrigem(SolicitacaoAssinatura sol) =>
        sol.ContratoHonorarioId != null ? $"/pages/honorarios-contrato-detalhe.html?id={sol.ContratoHonorarioId}"
        : sol.ProcessoId != null ? $"/pages/processo-detalhe.html?id={sol.ProcessoId}"
        : sol.ContatoId != null ? $"/pages/contato-detalhe.html?id={sol.ContatoId}"
        : "/pages/documentos.html";

    private void AgendarDownloadSePronto(SolicitacaoAssinatura sol, bool pdfPronto)
    {
        if (pdfPronto && sol.Status == StatusSolicitacaoAssinatura.Concluida && sol.DocumentoAssinadoId == null)
            jobs.Enqueue<AssinaturaDigitalService>(s => s.BaixarAssinadoAsync(sol.Id, CancellationToken.None));
    }

    private void Historico(SolicitacaoAssinatura sol, Guid? usuarioId, string descricao)
    {
        if (sol.ContratoHonorarioId == null) return;
        db.HistoricosContratoHonorario.Add(new HistoricoContratoHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = sol.TenantId,
            ContratoId = sol.ContratoHonorarioId.Value,
            TipoEvento = EventoContratoHonorario.Assinatura,
            Descricao = Cortar(descricao, 1000),
            UsuarioId = usuarioId,
            CriadoEm = DateTime.UtcNow,
        });
    }

    private void Notificar(SolicitacaoAssinatura sol, string titulo, string mensagem, string url)
    {
        if (sol.CriadoPorId == null) return;
        db.Notificacoes.Add(new Notificacao
        {
            Id = Guid.NewGuid(),
            TenantId = sol.TenantId,
            UsuarioId = sol.CriadoPorId.Value,
            Tipo = TipoNotificacao.AssinaturaContrato,
            Titulo = Cortar(titulo, 300),
            Mensagem = Cortar(mensagem, 1000),
            Url = url,
            Lida = false,
            CriadaEm = DateTime.UtcNow,
        });
    }

    public static List<SignatarioAssinaturaDto> LerSignatarios(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<SignatarioAssinaturaDto>>(json, ContratosHttpClient.Json) ?? []; }
        catch (JsonException) { return []; }
    }

    // ── Apoio ─────────────────────────────────────────────────────────────────────────

    private async Task<string> ChaveAsync(Guid tenantId, CancellationToken ct)
    {
        var integ = await IntegracaoAsync(tenantId, ct)
            ?? throw new AssinaturaDigitalException("Conecte o Contratos em Configurações → Integrações para enviar contratos para assinatura.", 409);
        return cifra.Decifrar(integ.ApiKeyCifrada);
    }

    private async Task<(SolicitacaoAssinatura, string)> SolicitacaoAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var sol = await db.SolicitacoesAssinatura.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct)
            ?? throw new AssinaturaDigitalException("Solicitação de assinatura não encontrada.", 404);
        return (sol, await ChaveAsync(tenantId, ct));
    }

    private async Task<ContextoAssinatura> ContextoAsync(Guid tenantId, Guid usuarioId, OrigemAssinatura origem, CancellationToken ct)
    {
        origem.Validar();
        ContratoHonorario? contrato = null;
        Contato? cliente = null;
        Processo? processo = null;

        if (origem.ContratoHonorarioId is { } contratoId)
        {
            contrato = await db.ContratosHonorarios
                .Include(c => c.Contato)
                .Include(c => c.Processo)
                .FirstOrDefaultAsync(c => c.Id == contratoId && c.TenantId == tenantId, ct)
                ?? throw new AssinaturaDigitalException("Contrato de honorários não encontrado.", 404);
            cliente = contrato.Contato;
            processo = contrato.Processo;
        }
        else if (origem.ContatoId is { } contatoId)
        {
            cliente = await db.Contatos.FirstOrDefaultAsync(c => c.Id == contatoId && c.TenantId == tenantId, ct)
                ?? throw new AssinaturaDigitalException("Contato não encontrado.", 404);
        }
        else
        {
            processo = await db.Processos.FirstOrDefaultAsync(p => p.Id == origem.ProcessoId && p.TenantId == tenantId, ct)
                ?? throw new AssinaturaDigitalException("Processo não encontrado.", 404);
            // O cliente do processo: a parte cadastrada como cliente; senão, a primeira parte.
            var partes = await db.ProcessoPartes
                .Where(pp => pp.ProcessoId == processo.Id && pp.Contato.TenantId == tenantId)
                .Select(pp => pp.Contato)
                .ToListAsync(ct);
            cliente = partes.FirstOrDefault(c => c.TipoContato == TipoContato.Cliente) ?? partes.FirstOrDefault();
        }

        var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId, ct);
        var usuario = await db.Users.FirstOrDefaultAsync(u => u.Id == usuarioId && u.TenantId == tenantId, ct)
            ?? throw new AssinaturaDigitalException("Usuário não encontrado.", 404);
        var hoje = FusoHorario.HojeParede(FusoHorario.Resolver(tenant.FusoHorario));
        return new ContextoAssinatura(cliente, contrato, processo, tenant, usuario, hoje);
    }

    private async Task<Dictionary<string, string>> FontesSalvasAsync(Guid tenantId, string templateId, CancellationToken ct)
    {
        var json = await db.MapeamentosModeloAssinatura
            .Where(m => m.TenantId == tenantId && m.TemplateId == templateId)
            .Select(m => m.FontesJson)
            .FirstOrDefaultAsync(ct);
        if (json == null) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private async Task SalvarFontesAsync(Guid tenantId, string templateId, Dictionary<string, string?>? fontes, CancellationToken ct)
    {
        if (fontes == null) return;
        var validas = fontes.Where(kv => FontesAssinatura.Existe(kv.Value) && kv.Key.Length <= 200)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);
        var m = await db.MapeamentosModeloAssinatura.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.TemplateId == templateId, ct);
        if (m == null)
        {
            m = new MapeamentoModeloAssinatura { Id = Guid.NewGuid(), TenantId = tenantId, TemplateId = templateId };
            db.MapeamentosModeloAssinatura.Add(m);
        }
        m.FontesJson = JsonSerializer.Serialize(validas);
        m.AtualizadoEm = DateTime.UtcNow;
    }

    private static async Task<T> Chamar<T>(Func<Task<T>> chamada)
    {
        try { return await chamada(); }
        catch (ContratosApiException e) { throw Traduzir(e); }
    }

    /// <summary>Erro da API → mensagem para o usuário (com os erros de validação por campo, se houver).</summary>
    internal static AssinaturaDigitalException Traduzir(ContratosApiException e)
    {
        var msg = e.Status == 401
            ? "O Contratos recusou a chave de API (revogada?). Um Admin precisa conectar de novo em Configurações → Integrações."
            : e.Mensagem;
        if (e.Erros is { Count: > 0 })
            msg += " " + string.Join(" ", e.Erros.SelectMany(kv => kv.Value).Distinct().Take(5));
        // 401/402/403 são problemas da conta no Contratos, não da sessão do usuário no Causify.
        var status = e.Status switch
        {
            400 or 404 or 409 or 429 => e.Status,
            401 or 402 or 403 => 409,
            _ => 502,
        };
        return new AssinaturaDigitalException(msg, status);
    }

    private static string Cortar(string s, int max) => s.Length <= max ? s : s[..max];
}
