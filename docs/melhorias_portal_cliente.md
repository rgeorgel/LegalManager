# Melhorias: Portal do Cliente

Documento de backlog para melhorias no fluxo de acesso e visualização do Portal do Cliente.

## Contexto

O portal do cliente (`/cliente/index.html`) permite que clientes do escritório acompanhem seus processos. O fluxo atual está funcional mas fragmentado — acesso e processo são gerenciados em telas separadas, e algumas experiências importantes (convite por link, notificações) ainda não existem.

## Fluxo atual (referência)

1. Advogado adiciona o contato como **parte do processo** (tela de detalhe do processo)
2. Advogado vai em **Contatos** → abre o contato → seção "Acesso ao Portal" → define email + senha manualmente
3. Sistema envia e-mail com as credenciais em texto claro e o link do portal
4. Cliente acessa `/cliente/index.html`, faz login, vê processos onde é parte
5. Nos andamentos, o cliente só vê os marcados com `VisivelCliente = true` (botão 👁️ na tela do processo)

---

## Melhorias mapeadas

### 1. Criar acesso ao portal direto da tela do processo

**Problema:** o advogado precisa navegar até o cadastro do contato para criar/ver o acesso ao portal, sem nenhum link ou atalho na tela do processo.

**Melhoria:** na seção "Partes do Processo" em `processo-detalhe.html`, mostrar um ícone/badge indicando se o contato já tem acesso ao portal ativo. Adicionar botão "Convidar para o portal" que abre um modal inline sem precisar sair da tela do processo.

**Arquivos envolvidos:**
- `wwwroot/pages/processo-detalhe.html`
- `wwwroot/js/processos.js`
- `ContatosController.cs` (endpoint `POST /api/contatos/{id}/portal-acesso` já existe)

---

### 2. Fluxo de convite por link (sem senha manual)

**Problema:** o advogado define a senha do cliente, que é enviada em texto claro no e-mail — má prática de segurança e UX ruim.

**Melhoria:** substituir o fluxo de "email + senha manual" por um **link de convite com token temporário** (validade de 72h). O cliente recebe o e-mail, clica no link, e define a própria senha na primeira entrada.

**O que implementar:**
- Adicionar campos `TokenConvite` (string) e `TokenConviteExpiraEm` (DateTime?) na entidade `AcessoCliente`
- Migration para adicionar os campos
- Endpoint `POST /api/portal/aceitar-convite` — valida o token e define a senha
- Endpoint `POST /api/contatos/{id}/portal-acesso/reenviar-convite` — regenera o token e reenvia o e-mail
- Página `/cliente/aceitar-convite.html` — formulário para o cliente definir a senha
- Atualizar `IEmailService.EnviarAcessoPortalAsync` para enviar o link em vez da senha

**Arquivos envolvidos:**
- `Domain/Entities/AcessoCliente.cs`
- `Infrastructure/Services/PortalClienteService.cs`
- `API/Controllers/PortalClienteController.cs`
- `API/Controllers/ContatosController.cs`
- `IEmailService` e implementação
- `wwwroot/cliente/aceitar-convite.html` (nova página)

---

### 3. Redefinição de senha pelo cliente

**Problema:** se o cliente esquecer a senha, não há fluxo de recuperação — o advogado precisa redefinir manualmente.

**Melhoria:** fluxo clássico de "esqueci a senha":
- Página `/cliente/esqueci-senha.html` com campo de e-mail
- Endpoint `POST /api/portal/solicitar-redefinicao` — gera token e envia e-mail
- Endpoint `POST /api/portal/redefinir-senha` — valida token e salva nova senha
- Campos na entidade: `TokenRedefinicao` e `TokenRedefinicaoExpiraEm`

---

### 4. Visibilidade de andamentos: controle por padrão e em lote

**Problema:** por padrão, novos andamentos importados do DataJud/ESAJ chegam com `VisivelCliente = false`, e o advogado precisa ativar um a um com o botão 👁️.

**Melhorias sugeridas:**
- **Configuração por processo:** checkbox "Compartilhar andamentos com o cliente automaticamente" no processo. Se ativo, novos andamentos já nascem com `VisivelCliente = true`.
- **Ação em lote:** botão "Mostrar todos para o cliente" / "Ocultar todos" na seção de andamentos.
- **Filtro rápido:** na listagem de andamentos, botão para filtrar "Visíveis pelo cliente" vs "Ocultos".

**Arquivos envolvidos:**
- `Domain/Entities/Processo.cs` (novo campo `CompartilharAndamentosComCliente`)
- `Infrastructure/Jobs/MonitoramentoJob.cs` (respeitar o campo ao criar andamentos)
- `wwwroot/pages/processo-detalhe.html`
- `ProcessosController.cs` (endpoint de update em lote de visibilidade)

---

### 5. Notificações ao cliente por e-mail em novos andamentos

**Problema:** o cliente não é avisado quando há um novo andamento no processo — precisa entrar no portal para verificar.

**Melhoria:** ao criar/importar um andamento com `VisivelCliente = true`, disparar um e-mail para o cliente informando que houve movimentação no processo.

**O que implementar:**
- No `MonitoramentoJob` e no endpoint de adicionar andamento manual, após salvar, verificar se o processo tem partes com `AcessoCliente` ativo e `NotificacaoHabilitada = true`
- Criar template de e-mail "Nova movimentação no seu processo"
- A flag `NotificacaoHabilitada` já existe na entidade `Contato` — verificar se está sendo usada ou só reservada

**Arquivos envolvidos:**
- `Infrastructure/Jobs/MonitoramentoJob.cs`
- `Infrastructure/Services/ProcessoService.cs` (ao adicionar andamento manual)
- `IEmailService` e implementação
- `Domain/Entities/Contato.cs` (`NotificacaoHabilitada` — confirmar uso atual)

---

### 6. Página do cliente: detalhe do andamento

**Problema:** na versão atual do portal, o cliente vê a lista de andamentos mas sem nenhum detalhe expandido — a descrição traduzida por IA (`DescricaoTraduzidaIA`) está sendo retornada pela API mas não é exibida.

**Melhoria:** exibir a descrição traduzida (em linguagem leiga) ao clicar em um andamento, diferenciando visualmente do texto técnico original.

**Arquivos envolvidos:**
- `wwwroot/cliente/` — páginas do portal do cliente

---

### 7. Portal do cliente: acesso via link público do processo (sem login)

**Ideia futura (baixa prioridade):** gerar um link público e autenticado por token para que o cliente acesse um processo específico sem precisar criar conta/senha. Útil para clientes avulsos ou consultas pontuais.

**Modelo:** link do tipo `/cliente/processo-publico?token=xyz` com validade configurável.
