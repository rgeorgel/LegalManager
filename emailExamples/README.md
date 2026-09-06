# Causify — Email Templates

Visual previews de todos os emails transacionais enviados pelo sistema.

## Como abrir

Abra `index.html` no navegador — ele lista todos os emails com links para cada preview. Cada arquivo `.html` é standalone e pode ser aberto direto.

## Emails implementados (em uso)

| Arquivo | Método | Quando é enviado | Destinatário |
|---|---|---|---|
| `boas-vindas.html` | `EnviarBoasVindasAsync` | Logo após cadastro do escritório — inclui credenciais e data de expiração | Admin |
| `convite-usuario.html` | `EnviarConviteUsuarioAsync` | Admin convida novo usuário para o escritório | Novo usuário |
| `redefinicao-senha.html` | `EnviarResetSenhaAsync` | Solicitação de redefinição de senha (link válido 1h) | Qualquer |
| `trial-expirando.html` | `EnviarTrialExpirandoAsync` | Job diário — trial expira em 7, 3 ou 1 dia(s) | Admin |
| `resumo-tarefas.html` | `EnviarResumoTarefasAsync` | Resumo diário com tarefas atrasadas, hoje e próximas | Usuário |
| `alerta-evento.html` | `EnviarAlertaEventoAsync` | Template legado de evento único (mantido para reuso) | Usuário |
| `resumo-eventos.html` | `EnviarResumoEventosAsync` | Job agrupa eventos de amanhã por destinatário e envia um único email por usuário/dia | Usuário |
| `novo-andamento.html` | `EnviarNovoAndamentoAsync` | Monitoramento captura novo andamento processual | Usuário |
| `alerta-prazo-processual.html` | `EnviarAlertaPrazoProcessualAsync` | Job diário — prazos processuais próximos do vencimento | Usuário |
| `acesso-portal.html` | `EnviarAcessoPortalAsync` | Escritório libera acesso do cliente ao Portal | Cliente |
| `andamento-traduzido.html` | `EnviarAndamentoTraduzidoAsync` | Opt-in: andamento traduzido por IA para o cliente | Cliente |

## Emails definidos mas não chamados

Estes métodos existem no `EmailService` mas nenhum caller os invoca — candidatos para próxima implementação:

| Arquivo | Método | Uso pretendido |
|---|---|---|
| `alerta-prazo-tarefa.html` | `EnviarAlertaPrazoTarefaAsync` | Aviso individual de prazo de tarefa próximo (substitui ou complementa o resumo diário) |
| `alerta-tarefa-atrasada.html` | `EnviarAlertaTarefaAtrasadaAsync` | Aviso de tarefa vencida e ainda pendente |
| `nova-publicacao.html` | `EnviarNovaPublicacaoAsync` | Publicação capturada para processo monitorado |
| `cobranca.html` | `EnviarCobrancaAsync` | Cobrança de honorários ao cliente com QR Code PIX |

## Fonte dos templates

Todos os HTMLs estão definidos em `src/LegalManager.Infrastructure/Services/EmailService.cs`. Os previews nesta pasta reproduzem o markup com dados fictícios de exemplo.

## Gatilhos (jobs)

A maioria dos emails automáticos é disparada pelo `AlertasJob.cs` e `MonitoramentoJob.cs`:

- **Resumo diário de tarefas** — todo dia no horário configurado
- **Alerta de evento** — 1 dia antes de cada evento da agenda
- **Trial expirando** — diário, verifica tenants com trial em 7/3/1 dia(s)
- **Prazo processual** — diário, verifica prazos em janelas curtas (hoje, amanhã, próximos dias)
- **Novo andamento** — disparado pelo `MonitoramentoService` sempre que uma consulta a tribunais retorna novidades