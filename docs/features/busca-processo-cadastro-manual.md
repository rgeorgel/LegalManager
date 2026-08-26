# Busca de Processo no Cadastro Manual — Estado Atual, Custo e Plano de Melhoria

**Data:** 2026-08-25
**Autor:** Michael (god, hive), a pedido de Ricardo

---

## TL;DR

A busca de processo **já existe** no cadastro manual (botão 🔍 ao lado do campo Número CNJ, em `pages/processos.html`). Ela usa exclusivamente o **DataJud** (API pública e gratuita do CNJ) e já cobre **todos os tribunais brasileiros** (TJs, TRFs, TRTs, STF/STJ/TST/TSE/STM) — o tribunal é inferido automaticamente a partir dos dígitos do número CNJ. **Custo atual: R$ 0,00 por busca.**

O que falta não é "adicionar a busca" — é **completá-la**: hoje ela só preenche Vara/Classe/Assuntos/Data de Ajuizamento, mas o DataJud já retorna Partes, Valor da Causa e Tribunal/Sigla, que são descartados pelo backend antes de chegar ao frontend. Completar isso é **esforço de engenharia apenas, custo extra R$ 0,00** (mesma API, mesma chamada, só parar de descartar campos que já vêm na resposta).

A única parte que teria custo real é **opcional**: um fallback para o Escavador quando o DataJud não encontra o processo (raro, mas acontece com processos muito recentes ainda não indexados). Isso custaria **R$ 0,05–R$ 0,20 por busca que cai no fallback** — não por busca total.

---

## 1. Estado Atual (confirmado lendo o código)

| Camada | Arquivo | O que faz |
|---|---|---|
| Frontend | `wwwroot/pages/processos.html` (linhas 359–371, 696–744) | Botão 🔍 ao lado do campo Número CNJ no modal "Novo Processo". Chama `/api/processos-monitorados/search?cnj=...`. Preenche automaticamente `fVara`, `fClasse`, `fAssuntos`, `fDataAjuizamento` (os três últimos são `readonly`). Mostra preview com contagem de movimentações e link "Ver andamentos". |
| Backend | `API/Controllers/ProcessosMonitoradosController.cs` | Endpoint `GET /api/processos-monitorados/search?cnj=&tribunal=`. Valida CNJ, chama `DataJudAdapter.ConsultarAsync` (ou `ConsultarPorTribunalAsync` se o usuário informar o tribunal manualmente). |
| Adapter | `Infrastructure/Tribunais/DataJudAdapter.cs` | `InferirTribunal()` decodifica o dígito `J` (justiça) e `TT` (tribunal) do número CNJ e mapeia para **qualquer um dos 60+ índices** do DataJud (`TribunalIndex`: STF, STJ, TST, TSE, STM, TRF1-6, todos os 27 TJs, TRT1-24). Consulta `POST {baseUrl}/api_publica_{tribunal}/_search` com o número do processo. |
| Config | `.env.example` linha 99, `Program.cs:149-153`, `docker-compose.yml:63-64` | `DataJud:BaseUrl = https://api-publica.datajud.cnj.jus.br` — **API pública oficial do CNJ**, autenticação por chave pública gratuita (`DataJud:ApiKey`), sem cobrança por consulta. |

**Conclusão direta:** a pergunta "colocar a pesquisa de processos no cadastro manual" já está resolvida na infraestrutura — o botão existe, a API é gratuita, e cobre o Brasil inteiro. O gap é que a resposta do DataJud é mais rica do que o que a tela usa hoje.

---

## 2. Lacunas Identificadas

`DataJudAdapter.ConsultarTribunalAsync` já monta um `TribunalConsultaResult` com estes campos — **buscados na mesma chamada, sem custo adicional** — que a API `/processos-monitorados/search` **não repassa** no JSON de resposta:

| Campo já disponível no `TribunalConsultaResult` | Usado hoje? | Onde poderia preencher no formulário |
|---|---|---|
| `Partes` (Nome, Cpf/Cnpj, OAB, Polo) | ❌ Descartado no controller | Seção "Partes do Processo" (hoje 100% manual, um contato por vez via "+ Adicionar Parte") |
| `ValorCaixa` (valor da causa) | ❌ Descartado | Campo "Valor da Causa (R$)" |
| `SiglaTribunal` | ❌ Descartado (só `NomeTribunal` é exposto) | Campo "Tribunal" (hoje só aparece no texto do preview, não escreve no input) |
| `Comarca` | Sempre `null` — DataJud não retorna essa informação | N/A — permanece manual, não há como automatizar sem outra fonte |

Além disso, o próprio código já tem a lógica de **resolver/criar Contatos a partir de Partes do DataJud** implementada — em `OnboardingController.ResolverPartesDataJudAsync` (linha 345), usada hoje apenas no fluxo de importação direta por CNJ (`MontarDtoDataJudAsync`, linha 493). É privada ao `OnboardingController` — para reusar no cadastro manual precisa virar um método compartilhado (ex: em `IContatoService` ou um novo serviço `ProcessoBuscaService`).

---

## 3. Resposta à Pergunta: Custo Extra

| Escopo | Custo de API por busca | Esforço de engenharia |
|---|---|---|
| **Completar o que já existe** (repassar Partes/ValorCausa/Tribunal do DataJud e auto-preencher o formulário) | **R$ 0,00** — mesma chamada DataJud já feita hoje, só para de descartar campos | Pequeno-médio (ver Fase 1 abaixo) |
| **Fallback Escavador quando DataJud não encontra** (opcional, cobre processos muito recentes ainda não indexados pelo CNJ) | **R$ 0,05 – R$ 0,20 por busca que cai no fallback** (não por busca total — só quando DataJud retorna vazio). Ver tabela de preços em [`docs/processos/escavador-fluxo-e-custos.md`](../processos/escavador-fluxo-e-custos.md) seção 3 | Pequeno (reusar `EscavadorHttpClient` já existente) |

**Recomendação:** implementar a Fase 1 (custo zero) primeiro — resolve a maior parte do valor (Partes + Valor da Causa + Tribunal auto-preenchidos) sem gastar nada. Avaliar a Fase 2 (fallback Escavador) só se, na prática, aparecer volume relevante de cadastros manuais de processos muito recentes que o DataJud ainda não indexou.

---

## 4. Plano de Implementação

### Fase 1 — Completar o auto-preenchimento (custo R$ 0,00)

**Backend**

1. `ProcessosMonitoradosController.Search`: incluir no JSON de resposta os campos já presentes em `TribunalConsultaResult` e hoje descartados: `partes` (lista de `{nome, cpf, cnpj, oab, polo}`), `valorCausa`, `siglaTribunal`.
2. Extrair `OnboardingController.ResolverPartesDataJudAsync` (linha 345) para um serviço compartilhado (ex: `IContatoResolverService.ResolverPartesDataJudAsync`), injetável tanto em `OnboardingController` quanto em `ProcessosController`/`ProcessosMonitoradosController`. Evita duplicar a lógica de criar/reaproveitar `Contato` a partir de Nome/CPF/CNPJ/OAB/Polo.
3. **Decisão de design:** a resolução de Partes (que cria `Contato`s no banco) deve rodar **na busca (preview)** ou **só no Salvar**?
   - Rodar na busca cria efeitos colaterais (novos Contatos) mesmo se o usuário cancelar o modal sem salvar.
   - **Recomendado:** a busca retorna as Partes como dados brutos (nome/cpf/cnpj/oab/polo), sem criar Contato ainda. A resolução/criação de Contato só acontece no `POST /api/processos` (Salvar), reaproveitando `ResolverPartesDataJudAsync` a partir dos dados cacheados da busca (mesmo padrão que o Escavador já usa via `SalvarPartesEscavadorAsync`, linha 667).

**Frontend** (`pages/processos.html`)

4. Ao receber resposta da busca: preencher `fValorCausa` (se vazio) e escrever a sigla do tribunal no campo `fTribunal` (hoje só aparece no texto do preview).
5. Novo bloco no preview: lista das Partes encontradas (nome + polo, ex: "Autor: João Silva | Réu: Empresa X Ltda") com um botão **"Usar partes encontradas"** que popula a seção "Partes do Processo" do formulário (reaproveitando o componente `partesContainer` já existente).
6. Guardar o resultado bruto da busca (`lastSearchResult`, variável já existe na linha 720) para reenviar ao `POST /api/processos` no Salvar, permitindo a resolução de Contato no backend conforme o item 3.

### Fase 2 (opcional) — Fallback Escavador para CNJ não encontrado no DataJud

7. Em `ProcessosMonitoradosController.Search`, se `DataJudAdapter` retornar `Encontrado = false`, chamar `EscavadorHttpClient` (endpoint de capa/movimentações por CNJ — ver mapa de endpoints em `escavador-fluxo-e-custos.md` seção 1) como segunda tentativa.
8. Expor no preview qual fonte respondeu (`"DataJud"` vs `"Escavador"`), mesmo padrão de badge já usado no fluxo de importação por OAB (`fontes-dados-processos.md` seção 2.3 — badges por `Fonte`).
9. **Importante:** registrar no log/auditoria quando o fallback é usado, para acompanhar volume real e validar se o custo mensal do Escavador se justifica antes de tornar isso o padrão.

---

## 5. Estimativa de Esforço

| Fase | Escopo | Estimativa |
|---|---|---|
| 1 | Backend (extrair serviço compartilhado + enriquecer response) + Frontend (auto-fill + UI de partes) | 0,5–1 dia |
| 2 (opcional) | Fallback Escavador + badge de fonte + log de auditoria | 0,5 dia |

---

## 6. Riscos / Casos de Borda

- **Contato duplicado:** `ResolverPartesDataJudAsync` já busca por nome (`GetByNomeAsync`) antes de criar — mesmo risco de falso-positivo/negativo que já existe hoje no fluxo de importação por OAB (nomes iguais de pessoas diferentes, nomes com grafias levemente diferentes). Não é uma regressão introduzida por este plano, é uma limitação preexistente herdada do reuso.
- **CNJ ambíguo/não padronizado:** `InferirTribunal` depende do formato de 20 dígitos (`NNNNNNNDDAAAAJTTOOOO`); números fora do padrão caem no fallback do usuário selecionar o Tribunal manualmente (`ConsultarPorTribunalAsync`) — comportamento já existente, não muda.
- **Comarca continua manual:** DataJud não retorna essa informação (campo sempre `null` no adapter); nenhuma fonte gratuita cobre isso hoje. Fora de escopo.
- **Rate limit do DataJud:** não documentado neste repositório; a API pública do CNJ tem limites de uso justo. Como o cadastro manual é uma ação pontual do usuário (não um job em lote), o risco é baixo — mas vale monitorar se o volume de buscas manuais crescer muito.

---

## 7. Decisões em Aberto

| # | Questão | Recomendação |
|---|---|---|
| 1 | Resolver Partes na busca ou só no Salvar? | Só no Salvar (evita Contatos órfãos de buscas canceladas) — ver seção 4, item 3 |
| 2 | Implementar Fase 2 (fallback Escavador) desde já ou só sob demanda? | Só sob demanda — medir primeiro quantos cadastros manuais o DataJud não resolve antes de pagar por um fallback |
| 3 | UI: sobrescrever campos já preenchidos manualmente ao clicar em "Usar partes encontradas"? | Não — só adicionar partes novas, nunca sobrescrever o que o usuário já digitou (mesmo princípio já usado nos campos `readonly` de Vara/Classe hoje) |

---

## 8. Fase 3 — Capa via Escavador no fallback (implementado, endpoint não confirmado)

A Fase 2 acima entregou o fallback Escavador via `ListarMovimentacoesPorProcessoAsync`
(`GET /api/v2/processos/numero_cnj/{cnj}/movimentacoes`), que só devolve a lista de
movimentações — nenhum campo de capa (classe/vara/tribunal/valor da causa/assuntos). Em
produção, Ricardo confirmou que o fallback funciona (o processo é encontrado, movimentações
aparecem), mas a tela de preview mostra "N/A" em todos esses campos porque o backend sempre
retornava `null` para eles nesse branch.

Esta fase fecha esse gap adicionando uma **segunda chamada Escavador**, disparada apenas
dentro do mesmo branch (DataJud não encontrou **e** o Escavador já confirmou o processo via
movimentações):

- Novo método `IEscavadorService.BuscarCapaPorNumeroCnjAsync(numeroCNJ, ct)`, implementado em
  `EscavadorHttpClient` reaproveitando **exatamente** o parser já usado por `BuscarPorOabAsync`/
  `BuscarPorCpfCnpjAsync` (`MapProcesso` + `ProcessoData`/`FonteData`/`CapaData`/
  `UnidadeOrigemRef`) — nenhum DTO novo foi criado, só um novo campo `ValorCausa` em
  `EscavadorProcessoDto` (a busca em massa nunca precisou dele, mas a capa parseia
  `capa.valor_causa.valor` do mesmo jeito).
- `ProcessosMonitoradosController.Search`: quando o fallback por movimentações encontra o
  processo, chama também `BuscarCapaPorNumeroCnjAsync` e, se vier um resultado não-nulo,
  preenche `tribunal`/`vara`/`classe`/`assuntos`/`valorCausa`/`siglaTribunal` na resposta (hoje
  forçados `null`). Se a chamada de capa falhar ou devolver `null`, a resposta continua
  idêntica à de antes desta mudança (`encontrado=true` via movimentações, capa `null`) — a
  falha da capa nunca derruba o resultado que já funcionava.

> ⚠️ **ENDPOINT NÃO CONFIRMADO — precisa validação em produção.** O endpoint chamado,
> `GET /api/v2/processos/numero_cnj/{cnj}` (sem o sufixo `/movimentacoes`), é uma inferência
> por convenção REST e pelo item "Capa de um processo por CNJ — R$ 0,05" já catalogado em
> [`docs/processos/escavador-fluxo-e-custos.md`](../processos/escavador-fluxo-e-custos.md) —
> **nunca foi testado contra a API real**. Este ambiente de desenvolvimento não tem acesso ao
> Escavador; só o servidor de produção do Ricardo tem. Se o endpoint ou o shape da resposta
> estiver errado, `BuscarCapaPorNumeroCnjAsync` simplesmente não encontra `numero_cnj` no JSON
> e retorna `null` (falha graciosa, documentada em código) — o pior caso é a tela continuar
> mostrando "N/A" como já mostra hoje, nunca uma quebra.
>
> **Como validar:** repetir o mesmo teste que já confirmou o fallback de movimentações — buscar
> no cadastro manual um CNJ que o DataJud não encontre mas que exista no Escavador, e conferir
> nos logs do container (`docker logs`, grep pelo CNJ) se aparece
> `[Escavador] Buscando capa do processo CNJ=...` seguido de sucesso (sem o warning de "resposta
> sem numero_cnj") — e se a tela passa a mostrar tribunal/vara/classe/valor da causa em vez de
> "N/A".
