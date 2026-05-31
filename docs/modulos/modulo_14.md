# Módulo 14 — Envio de E-mails (Resend)

## Visão Geral

O módulo de e-mail é responsável por todos os envios transacionais do sistema, incluindo notificações de andamentos, alertas de prazos, convites de usuários, recuperação de senha e cobranças. Todos os e-mails são enviados através do **Resend**, SDK oficial .NET.

## Configuração

### appsettings.json

```json
"Resend": {
  "ApiToken": "re_xxxxxxxxxxxxxxxxxxxx",
  "FromEmail": "noreply@seudominio.com.br",
  "FromName": "LegalManager"
}
```

### Program.cs

```csharp
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
    o.ApiToken = builder.Configuration["Resend:ApiToken"] ?? "");
builder.Services.AddTransient<IResend, ResendClient>();
builder.Services.AddTransient<IEmailService, EmailService>();
```

## Arquitetura

### Interface IEmailService

```
Application/Interfaces/IEmailService.cs
```

Define o contrato para todos osenvios de e-mail do sistema:

| Método | Descrição |
|--------|-----------|
| `EnviarBoasVindasAsync` | E-mail de boas-vindas ao criar novo tenant |
| `EnviarConviteUsuarioAsync` | Convite para novo usuário do escritório |
| `EnviarResetSenhaAsync` | Link de redefinição de senha |
| `EnviarTrialExpirandoAsync` | Alerta de expiração de trial |
| `EnviarAlertaPrazoTarefaAsync` | Alerta de prazo de tarefa vencendo |
| `EnviarAlertaEventoAsync` | Alerta de evento amanhã |
| `EnviarNovoAndamentoAsync` | Notificação de novo andamento processual |
| `EnviarAlertaPrazoProcessualAsync` | Alerta de prazo processual |
| `EnviarNovaPublicacaoAsync` | Notificação de publicação capturada |
| `EnviarAcessoPortalAsync` | Credenciais de acesso ao Portal do Cliente |
| `EnviarAndamentoTraduzidoAsync` | Andamento traduzido por IA para cliente |
| `EnviarCobrancaAsync` | Cobrança de honorários com QR Code PIX |

### Implementação EmailService

```
Infrastructure/Services/EmailService.cs
```

- Injeta `IResend` (Resend SDK)
- Lê configurações de `IConfiguration` (FromName, FromEmail, FrontendUrl)
- Método privado `CriarMensagem` para construir `EmailMessage` padronizado
- Todos os templates usam HTML inline com estilos responsivos
- Assunto formatado com informações contextuais (número do processo, data de vencimento, etc.)

## Templates de E-mail

Todos os templates são HTML com estilos inline para máxima compatibilidade com clientes de e-mail. Estrutura base:

```
┌─────────────────────────────────────┐
│  ⚖️ LegalManager        [Header]    │
├─────────────────────────────────────┤
│                                     │
│  [Content com contexto]            │
│                                     │
│  [Botão CTA quando aplicável]       │
│                                     │
├─────────────────────────────────────┤
│  Footer com informações             │
└─────────────────────────────────────┘
```

### Template de Cobrança

Inclui QR Code PIX em Base64 quando disponível:

```
┌─────────────────────────────────────┐
│  ⚖️ LegalManager                    │
│  Cobrança de Honorários             │
├─────────────────────────────────────┤
│  Olá, {nomeCliente}!               │
│  O escritório {nomeEscritorio}      │
│  registrou uma cobrança.            │
│                                     │
│  ┌─────────────────────────────┐   │
│  │ Detalhes da cobrança        │   │
│  │ Valor: R$ {valor}          │   │
│  │ Vencimento: {vencimento}   │   │
│  └─────────────────────────────┘   │
│                                     │
│  [QR Code PIX em Base64]           │
│                                     │
│  [Ver detalhes]                     │
└─────────────────────────────────────┘
```

## Envio Assíncrono via Hangfire

E-mails nunca são enviados de forma síncrona em requisições HTTP. Sempre são enfileirados como jobs Hangfire:

```csharp
BackgroundJob.Enqueue<IEmailService>(s =>
    s.EnviarNovoAndamentoAsync(email, nomeUsuario, numeroCNJ, descricao, ct));
```

### Jobs que utilizam e-mail

| Job | Schedule | E-mails enviados |
|-----|----------|-----------------|
| `AlertasJob` | A cada 3 horas | Prazo de tarefa, evento amanhã, trial expirando, prazo processual |
| `MonitoramentoJob` | Diariamente às 06:00 UTC | Novos andamentos capturados |
| `CapturaPublicacaoJob` | Diariamente às 07:00 UTC | Publicações capturadas |

## Fluxos de E-mail por Evento

### Cadastro de novo escritório
1. `AuthService.CriarTenantAsync` cria tenant e usuário admin
2. `EmailService.EnviarBoasVindasAsync` é enfileirado via Hangfire

### Convite de usuário
1. Admin convida usuário via `UsuariosController`
2. `ConviteUsuario` criado com token de 7 dias
3. `EmailService.EnviarConviteUsuarioAsync` é enfileirado com link contendo token

### Reset de senha
1. `AuthController.ForgotPassword` recebe email
2. `AuthService.GerarTokenResetAsync` cria token de 1h
3. `EmailService.EnviarResetSenhaAsync` é enfileirado

### Novo andamento processual
1. `MonitoramentoJob` detecta novo andamento
2. Para cada advogado responsável: `EmailService.EnviarNovoAndamentoAsync`
3. Se IA habilitada para cliente: `EmailService.EnviarAndamentoTraduzidoAsync`

### Cobrança (via AbacatePay)
1. `FinanceiroService` cria lançamento `Pendente`
2. `AbacatePayService.GerarPixAsync` gera QR Code
3. `EmailService.EnviarCobrancaAsync` com QR Code é enfileirado

## Campos Personalizáveis

O campo `From` dos e-mails usa o nome configurado:

```csharp
msg.From = $"{_config["Resend:FromName"]} <{_config["Resend:FromEmail"]}>";
```

O link do frontend (`App:FrontendUrl`) é usado em todos os botões de CTA:

```csharp
$"<a href=\"{_config["App:FrontendUrl"]}/pages/tarefas.html\">"
```

## Segurança

- Todos os valores dinâmicos são escapados com `System.Net.WebUtility.HtmlEncode()`
- Links de convite expiram em 7 dias
- Links de reset expiram em 1 hora
- Não há exposição de dados sensíveis nos templates

## Dependências

- `Resend` (pacote NuGet oficial)
- `Hangfire` + `Hangfire.PostgreSql` (já configurados no Program.cs)

## Variáveis de Ambiente

As configurações do Resend são lidas do arquivo `appsettings.json` ou de variáveis de ambiente equivalentes. Para produção,强烈推荐 usar secrets via variável de ambiente ou arquivo `.env` (com `dotnet-env` ou similar).

### Arquivo .env (desenvolvimento)

```env
RESEND_API_TOKEN=re_xxxxxxxxxxxxxxxxxxxx
RESEND_FROM_EMAIL=noreply@seudominio.com.br
RESEND_FROM_NAME=LegalManager
APP_FRONTENDURL=http://localhost:5000
```

### Mapeamento para appsettings.json

O sistema lê as configurações na seguinte ordem de precedência:

1. Variável de ambiente (maior prioridade)
2. `appsettings.json` (para desenvolvimento/local)

| Variável de Ambiente | appsettings.json path | Descrição |
|---------------------|----------------------|-----------|
| `RESEND_API_TOKEN` | `Resend:ApiToken` | Token da API Resend |
| `RESEND_FROM_EMAIL` | `Resend:FromEmail` | E-mail remetente |
| `RESEND_FROM_NAME` | `Resend:FromName` | Nome do remetente |
| `APP_FRONTENDURL` | `App:FrontendUrl` | URL base do frontend |

### Exemplo appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;..."
  },
  "Resend": {
    "ApiToken": "",
    "FromEmail": "noreply@seudominio.com.br",
    "FromName": "LegalManager"
  },
  "App": {
    "FrontendUrl": "http://localhost:5000"
  }
}
```

> **Importante:** Em produção, deixe `ApiToken` vazio no `appsettings.json` e defina a variável `RESEND_API_TOKEN` no ambiente. Isso evita que secrets fiquem commitados no repositório.