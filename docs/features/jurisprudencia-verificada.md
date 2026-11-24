# Jurisprudência Verificada — Plano de Implementação

**Data:** 2026-11-23
**Autor:** Claude (Claude Code), a pedido de Ricardo
**Status:** Fases 0, 1 e 2 implementadas em 2026-11-23 (ver "Implementação" abaixo), com a chave só no
super admin (sem a opção do escritório da seção 7.2). Backlog: [docs/plano/ideias.md](../plano/ideias.md).

## Implementação (2026-11-23)

| Parte | Onde |
|---|---|
| Fase 0 — prompts sem julgado inventado (todos os escritórios) | `IAService.RegraSemJulgadosInventados`, `RegraSoPrecedentesDaLista`, `GerarPecaComPrecedentesAsync`; "Entendimentos sobre o tema" no lugar de "Jurisprudência citada" |
| Extrator de citações (regex) | `Infrastructure/Jurisprudencia/ExtratorCitacoes.cs` |
| Base | `Precedente` e `FontePrecedentes` (migration `JurisprudenciaVerificada`, índice GIN full-text em português) |
| Importação | `StjDadosAbertos` (CKAN do STJ: `precedentes-qualificados` + `espelhos-de-acordaos-*`) e `Jobs/JurisprudenciaStjJob` (semanal; liga com `Jurisprudencia:ImportarStj=true`; `Jurisprudencia:MesesAcordaos`, padrão 12) |
| Conferência, busca e leitura de PDF | `JurisprudenciaService` (`IJurisprudenciaService`) |
| Geração com fonte | `PecaJuridicaService`: com o recurso liberado, busca até 8 precedentes e só eles podem ser citados; toda peça grava `CitacoesJson` |
| API | `POST /api/jurisprudencia/verificar` (`[ExigeRecurso]`) |
| Telas | `js/citacoes.js`; modal "Peça gerada" e botão "Verificar citações" em `processo-detalhe.html`; CSS `.cit-*` |
| Chave | `RecursosEmTeste.JurisprudenciaVerificada` (`jurisprudencia-verificada`), liberada no super admin |

### Fase 3 (2026-11-24)

| Parte | Onde |
|---|---|
| **TJs/TRFs/TRTs pelo número CNJ** (sem fornecedor pago — decisão de 2026-11-24) | `Cnj` (dígito verificador módulo 97: número errado = **Inexistente**, com certeza) + DataJud (`ITribunalAdapter`): processo existe → `ProcessoEncontrado` (âmbar: o teor do acórdão não é conferido); não achado → `NaoEncontrada`; falha da consulta (`TribunalConsultaResult.Falhou`) → `NaoVerificada`. Até 5 consultas por verificação (3 na página pública) |
| **Busca por significado com a IA atual** (Claude não gera embeddings; sem pgvector) | `JurisprudenciaService.BuscarPrecedentesPorSignificadoAsync`: a IA amplia a busca (`IAService.ExpandirBuscaJurisprudenciaAsync`: sinônimos e vocabulário dos tribunais), o full-text traz até 30 candidatos e a IA escolhe e ordena os pertinentes ou responde "nenhum" (`EscolherPrecedentesAsync`). Cada chamada tem limite de 20 s; se a IA falhar, demorar ou não estiver configurada, fica a busca por palavras. A IA é carregada sob demanda (`Lazy<IIAService>`): a conferência de citações nunca depende dela |
| **Página pública** `/verificador-de-citacoes.html` | Sem login, visual do blog, `js/verificador-publico.js` + `css/citacoes.css`; `POST /api/jurisprudencia/publico/verificar` (`[AllowAnonymous]`, até 20 mil caracteres, 20 verificações por hora por IP); no sitemap e no `llms.txt`; evento `verificador_citacoes_usado` no PostHog |

Validado com Postgres e dados reais: exemplo da página (tema verificado, tema e CNJ inexistentes, súmula a conferir), DataJud sem chave local (mostra "não respondeu", não "não encontrado") e o limite (429 na 21ª verificação da hora). A busca por significado foi testada com a IA simulada (sem gastar créditos).

**Pendente da fase 3:** fornecedor pago para o teor dos acórdãos de TJ (decidido deixar para depois); busca semântica na base inteira (embeddings + pgvector) se a reordenação pela IA não bastar.

**Validado em Postgres real** (banco descartável): migration, importação real de 1 mês (2.378 temas +
27.910 acórdãos, 73 MB, 11 fontes sem erro), busca full-text e o verificador na tela com citações reais e
inventadas (verificada, divergente, não encontrada, cancelada, inexistente).

**Ficou de fora / pendente:**
- **Súmulas (STF, STJ, TST), STF e TST** (TJs agora são conferidos pelo número CNJ e DataJud — fase 3): sem fonte aberta equivalente aos dados do STJ — aparecem como
  "não verificadas". A base já aceita súmulas (`TipoPrecedente.Sumula`); falta escolher a fonte oficial.
- **Links do STJ:** `processo.stj.jus.br/processo/pesquisa/?num_registro=` e a página de temas repetitivos
  ficam atrás de um desafio anti-robô; não deu para conferir automaticamente — conferir no navegador.
- **Espelhos ≠ todos os acórdãos:** o STJ só publica espelhos dos acórdãos com tese nova ou
  representativa; por isso "não encontrada" diz que pode existir.
- Botão no painel de Documentos, histórico de peças com selo e opção do escritório em Configurações
  (seção 7.2) não foram feitos.
- A geração real de peça com IA não foi testada ponta a ponta (custo de IA); o fluxo tem testes com a IA simulada.


---

## TL;DR

Hoje o gerador de peças pede à IA, **sem nenhuma fonte**, um "resumo de jurisprudência relevante"
(`IAService.BuscarJurisprudenciaAsync`) e o prompt da peça pede "fundamentação legal adequada"
(`IAService.GerarPecaJuridicaAsync`). O modelo pode inventar número de recurso, súmula, relator e ementa
— e tribunais já multam por isso (TJSC, TRT-18, TRT-3, TSE; o STJ admite multa por litigância de má-fé).

A proposta tem três partes:

1. **Proteção sempre ligada (fase 0, todos os escritórios, sem flag):** a IA para de escrever números de
   precedentes que não recebeu; toda citação no texto é marcada como "não verificada"; aviso claro na tela.
2. **Verificador de citações (fase 1):** extrai citações (REsp, AgInt, Súmula, Tema...) de qualquer texto
   e confere numa base própria de súmulas, temas e acórdãos. **Não usa IA** — é regex + banco, custo quase
   zero, resultado determinístico.
3. **Jurisprudência com fonte (fase 2):** o gerador busca decisões reais na base (dados abertos do STJ
   primeiro) e só deixa a IA citar o que foi encontrado, sempre com link para a fonte.

**Dá para ser uma flag habilitada pelo cliente?** Sim — em duas camadas (seção 7): a **nossa** liberação
por escritório via `RecursosEmTeste` (piloto) e uma **opção do próprio escritório** em
Configurações → Inteligência Artificial, ligada pelo Admin. A proteção da fase 0 **não** deve ser opcional.

---

## 1. Como é hoje (lido no código)

| Peça | Arquivo | Comportamento |
|---|---|---|
| Geração | `Infrastructure/Services/PecaJuridicaService.cs` — `GerarPecaAsync` | Monta o contexto (processo + descrição), dispara **em paralelo** `GerarPecaJuridicaAsync` e `BuscarJurisprudenciaAsync`, grava `PecaGerada` e consome 1 crédito `GeracaoPeca`. |
| Prompt da peça | `Infrastructure/Services/IAService.cs:60` | Pede "fundamentação legal adequada". Não proíbe citar precedentes → a peça em si pode conter citações inventadas. |
| "Jurisprudência" | `Infrastructure/Services/IAService.cs:274` | Prompt livre: "Forneça um resumo de jurisprudência relevante sobre o tema". Sem fonte, sem verificação. |
| Armazenamento | `Domain/Entities/PecaGerada.cs` | `ConteudoGerado` e `JurisprudenciaCitada` como texto livre; `TesesSugeridas` sempre `null`. |
| Tela | `wwwroot/pages/processo-detalhe.html:1783` | Mostra `jurisprudenciaCitada` como texto puro, sem aviso. |

Ou seja: o risco existe **em dois lugares** — no bloco "Jurisprudência" e no próprio texto da peça.

---

## 2. Fase 0 — Proteção sempre ligada (P, fazer primeiro)

Sem flag, para todos, porque é proteção contra dano ao cliente.

1. **Prompt da peça** (`GerarPecaJuridicaAsync`): nova regra absoluta — "Não cite números de processos,
   recursos, súmulas, temas ou nomes de relatores, a menos que estejam na lista de precedentes fornecida.
   Fundamente com dispositivos legais e com a explicação do entendimento, sem identificar julgados."
2. **"Jurisprudência" vira "Entendimentos sobre o tema"** (`BuscarJurisprudenciaAsync`): o prompt passa a
   pedir princípios, posicionamentos e argumentos **sem identificar julgados**. Renomear na tela.
3. **Marcação de citações:** depois de gerar, rodar o extrator da seção 4.1 em `ConteudoGerado`; cada
   citação encontrada vira um destaque âmbar "não verificada — confira na fonte antes de usar".
4. **Aviso fixo** no modal da peça: "Texto gerado por IA. Confira leis e precedentes antes de protocolar."
5. **Teste:** dado um texto com "REsp 1.234.567/SP" e "Súmula 7/STJ", a resposta traz as duas citações
   marcadas; um teste de contrato do prompt garante que a regra nova está lá.

---

## 3. Fontes de dados

| Fonte | Conteúdo | Custo / licença | Fase |
|---|---|---|---|
| **Súmulas** STF (inclusive vinculantes), STJ, TST | Listas fechadas e pequenas (centenas cada) — texto, número, situação (vigente/cancelada) | Públicas, gratuitas | 1 |
| **Temas** repetitivos (STJ) e de repercussão geral (STF) | Número, tese firmada, situação | Públicos, gratuitos | 1 |
| **Acórdãos do STJ** — Portal de Dados Abertos (espelhos de acórdãos: classe, número, UF, relator, órgão julgador, datas, ementa, link) | Grande volume | **Uso livre, inclusive comercial** ([termo de uso](https://bdjur.stj.jus.br/jspui/bitstream/2011/179190/Termo%20de%20Uso%20-%20Dados%20Abertos.pdf)). Periodicidade de atualização **a confirmar** no portal | 2 |
| STF (acórdãos), TST, TJs | Sem fonte única aberta | Avaliar fornecedores pagos (API de jurisprudência de terceiros, Escavador, etc.) | 3 |

Regra de linguagem: só dizemos **"não existe"** quando a base cobre aquela lista por inteiro (ex.:
"não há Súmula 999 do STJ"). Para acórdãos, dizemos **"não encontrada na base"** — pode existir em
tribunal ou período ainda não coberto.

---

## 4. Verificador de citações (fase 1)

### 4.1 Extrator (regex, sem IA)

`Infrastructure/Jurisprudencia/ExtratorCitacoes.cs` — função pura, com testes de tabela. Formatos a cobrir:

| Tipo | Exemplos |
|---|---|
| Recursos/ações STJ | `REsp 1.234.567/SP`, `REsp n. 1234567 - SP`, `AgInt no AREsp 2.345.678/RJ`, `AgRg no REsp`, `EDcl no AgInt no REsp`, `HC`, `RHC`, `RMS`, `MS`, `CC` |
| STF | `RE 123.456`, `ARE`, `ADI 1.234`, `ADPF`, `ADC`, `HC` (desambiguar pelo "STF"/"Supremo" próximo) |
| Súmulas | `Súmula 7/STJ`, `Súmula n.º 7 do Superior Tribunal de Justiça`, `Enunciado 331 do TST`, `Súmula Vinculante 13`, `SV 13` |
| Temas | `Tema 1.076/STJ`, `Tema repetitivo 1076`, `Tema 69 da repercussão geral` |
| TST | `OJ 394 da SDI-1`, `Súmula 437 do TST` |
| Acórdãos de TJ/TRF/TRT | número CNJ `NNNNNNN-DD.AAAA.J.TR.OOOO` próximo de "Apelação", "Agravo", "TJSP" etc. |

Saída: `Citacao(Tipo, Tribunal, Classe, Numero, Uf?, TrechoOriginal, Inicio, Fim)` — posições no texto
para destacar na tela.

### 4.2 Conferência

`VerificadorCitacoesService.VerificarAsync(texto)` → para cada citação:

| Status | Quando | Tela |
|---|---|---|
| `Verificada` | Encontrada na base | verde ✓ + link para a fonte + dados (relator, órgão, data) |
| `Divergente` | Existe, mas o texto atribui relator/data/órgão/tese diferentes | laranja, mostra o que diverge |
| `Cancelada` | Súmula/tema cancelado ou superado | laranja |
| `Inexistente` | Lista fechada (súmula, tema) e o número não existe | vermelho |
| `NaoEncontrada` | Acórdão fora da base ou de tribunal não coberto | âmbar "não verificada — confira na fonte" |

### 4.3 Onde usar

- Automaticamente em toda peça gerada (`ConteudoGerado`).
- **"Verificar citações"** avulso: o advogado cola um texto ou escolhe um documento do processo (PDF; texto
  extraído com PdfPig, que já está no projeto) e recebe o relatório. Bom candidato a ferramenta gratuita
  para todos os planos e a página pública de captação (como as calculadoras).

---

## 5. Jurisprudência com fonte no gerador (fase 2)

```
descrição + contexto do processo
        │
        ▼
 1. termos de busca (IA leve ou extração de palavras-chave)
        │
        ▼
 2. busca na base (Postgres full-text 'portuguese'; filtros: tribunal, área, período) → top 10–20
        │
        ▼
 3. prompt da peça recebe a LISTA (id, tribunal, classe/número, relator, data, ementa)
    regra: "cite apenas itens da lista, pelo id; não cite nada fora dela"
    saída estruturada: { texto, citacoes: [{ id, trecho }] }
        │
        ▼
 4. pós-validação: extrator (4.1) no texto final; citação fora da lista → removida ou marcada
        │
        ▼
 5. grava PecaGerada + PrecedentesUsados; tela mostra cartões com link e selo "verificada"
```

- Busca inicial em **full-text do Postgres** (`to_tsvector('portuguese', ementa)`), sem infraestrutura
  nova. Busca semântica (embeddings/pgvector) só se a qualidade pedir — depende de o Postgres de produção
  ter a extensão.
- A chamada da peça hoje corre em paralelo com a da jurisprudência para caber no tempo da requisição. Com
  RAG a busca vem **antes** da geração; a busca no banco é rápida (ms), então o tempo total muda pouco,
  mas medir. Se apertar, mover a geração para job com aviso quando pronta.
- Créditos: continua 1 crédito `GeracaoPeca`. O verificador avulso não consome crédito (não usa IA).

---

## 6. Modelo de dados

```csharp
public class Precedente                    // base global, não é por tenant
{
    public Guid Id { get; set; }
    public TipoPrecedente Tipo { get; set; }      // Acordao, Sumula, SumulaVinculante, TemaRepetitivo, TemaRepercussaoGeral, OrientacaoJurisprudencial
    public string Tribunal { get; set; } = "";    // STJ, STF, TST...
    public string? Classe { get; set; }           // REsp, AgInt no AREsp, RE...
    public string Numero { get; set; } = "";      // só dígitos
    public string? Uf { get; set; }
    public string? Relator { get; set; }
    public string? OrgaoJulgador { get; set; }
    public DateTime? DataJulgamento { get; set; }
    public DateTime? DataPublicacao { get; set; }
    public string Texto { get; set; } = "";       // ementa, enunciado ou tese
    public string? Situacao { get; set; }         // vigente, cancelada, superada...
    public string UrlFonte { get; set; } = "";
    public string Fonte { get; set; } = "";       // "stj-dados-abertos", "stf-sumulas"...
    public string HashConteudo { get; set; } = "";
    public DateTime ImportadoEm { get; set; }
}
```

- Índice único `(Tribunal, Tipo, Classe, Numero)`; índice GIN em `to_tsvector('portuguese', Texto)`
  (migration com SQL bruto — o InMemory dos testes não usa o índice, só a busca simples).
- `PecaGerada` ganha `CitacoesJson` (resultado do verificador) e `PrecedentesUsadosJson` (ids da lista
  usada). `JurisprudenciaCitada` continua para peças antigas.
- Ingestão: `PrecedentesImportJob` (Hangfire, recorrente) — baixa os arquivos das fontes, faz upsert por
  `HashConteudo`, registra contagens e falhas no log. Primeira carga em lotes.
- Volume: estimar com uma amostra do STJ antes de decidir guardar só ementa (recomendado na fase 2) ou
  também íntegra.

---

## 7. Flag: liberada por nós e/ou ligada pelo cliente

**Sim, dá.** Proposta em duas camadas:

### 7.1 Camada 1 — liberação por escritório (nós)
Usa o mecanismo que já existe (`RecursosEmTeste`, CLAUDE.md "Recursos em teste"):

- Nova chave `RecursosEmTeste.JurisprudenciaVerificada = "jurisprudencia-verificada"` no catálogo.
- Super admin libera escritórios piloto em `superadmin/tenant-detalhe.html` (já auditado na timeline).
- API: `[ExigeRecurso(RecursosEmTeste.JurisprudenciaVerificada)]` nos endpoints novos (verificador avulso,
  listagem de precedentes); no `PecaJuridicaService`, checar `RecursosEmTeste.Liberado(tenant, chave)`
  para escolher o fluxo com RAG.
- Frontend: `recursoLiberado(RECURSOS.jurisprudenciaVerificada)` antes de mostrar o botão "Verificar
  citações" e os cartões de precedentes.
- Para abrir a todos depois: `Liberado` passa a responder `true` para a chave e, num segundo momento,
  remove-se a checagem (o processo descrito no CLAUDE.md).

### 7.2 Camada 2 — opção do próprio escritório (cliente)
Hoje **não existe** configuração de IA que o escritório liga/desliga — precisa criar:

- `Tenant.PreferenciasIA` (JSON, para crescer sem migration a cada opção) com
  `{ "jurisprudenciaComFonte": true, "tribunaisPreferidos": ["STJ"], "bloquearCitacaoNaoVerificada": false }`.
- Tela: **Configurações → Inteligência Artificial** (nova aba), só Admin; mudança auditada (`IAuditService`).
- Opções sugeridas:
  - **Jurisprudência com fonte** (liga o fluxo RAG da fase 2).
  - **Bloquear citações não verificadas**: em vez de só marcar, remove do texto gerado as citações que não
    foram verificadas (modo conservador, para escritórios que preferem segurança a volume).
  - **Tribunais preferidos** para a busca (quando houver mais fontes).
- Regra efetiva: `recurso liberado (camada 1) && opção ligada (camada 2)`. Quando abrirmos a todos, a
  camada 1 some e fica só a escolha do cliente.
- Também pode depender de plano (`PlanoRestricoes`), ex.: verificador avulso em todos os planos e
  jurisprudência com fonte a partir do Pro.

### 7.3 O que NÃO é opcional
A fase 0 (prompt sem números inventados, marcação "não verificada", aviso) vale para todos, sempre. Não
faz sentido deixar o cliente desligar uma proteção contra multa.

---

## 7.4 Onde o usuário vê (telas)

| Lugar | O que muda | Fase |
|---|---|---|
| Processo → **Gerar peça** → modal "Peça gerada" (`processo-detalhe.html`) | Resumo no topo (✓ verificadas · ⚠ não verificadas · ✗ inexistentes); citações coloridas no texto (verde/âmbar/vermelho, com dica ao passar o mouse); bloco "Precedentes usados" em cartões com órgão, data, trecho da ementa e **Ver no STJ**; alerta vermelho para citação inexistente; botões **Remover não verificadas** e **Copiar texto** (copiar avisa se ainda houver citação vermelha). Na fase 0: só o aviso, as marcas âmbar e "Entendimentos sobre o tema" no lugar de "Jurisprudência citada". | 0–2 |
| Processo (cabeçalho) e Documentos (painel lateral do PDF) → **Verificar citações** | Modal com a lista de citações do texto colado ou do PDF escolhido, uma por linha com o status e o motivo (ex.: "divergente: relator diferente"). | 1 |
| Histórico de peças geradas | Selo com a contagem de citações por status em cada peça. | 1 |
| Configurações → aba **Inteligência artificial** (só Admin, só com o recurso liberado) | Chaves "Jurisprudência com fonte" e "Bloquear citações não verificadas"; linha fixa e travada "Marcar e avisar sobre citações — sempre ligado". | 2 |
| Super admin → `tenant-detalhe.html` (lateral de recursos) | Liberar "Jurisprudência verificada" por escritório. | 1 |
| Página pública (blog/calculadoras) | "Verificador de citações" sem login, com limite de uso — captação. | 3 |

## 8. Testes e validação

1. **Extrator (unitário, tabela):** dezenas de formatos reais de citação, inclusive com quebras de linha,
   "n.", "nº", "n.º", pontos de milhar, UF com e sem barra, citações aninhadas ("AgInt nos EDcl no REsp").
   Casos negativos: números de telefone, CNJ de processo do próprio cliente sem contexto de acórdão.
2. **Verificador (integração, EF InMemory):** base com súmulas/temas/acórdãos de exemplo → texto com
   citação verificada, inexistente (súmula 999), cancelada, divergente (relator errado) e não encontrada.
3. **Ingestão (golden):** amostra real anonimizável (dados públicos — pode ir para `tests/fixtures`) do
   arquivo do STJ → registros `Precedente` esperados; reimportar não duplica (upsert por hash).
4. **Gerador com RAG (unitário com IA simulada):** a IA "devolve" uma citação fora da lista → é removida
   ou marcada; a IA devolve JSON quebrado → cai no fluxo da fase 0, nunca em erro 500.
5. **Teste de contrato dos prompts:** as regras de "não citar fora da lista" estão nos prompts (evita
   regressão silenciosa quando alguém editar o prompt).
6. **Avaliação de qualidade (manual, antes de liberar):** 30–50 temas reais escolhidos com advogados
   parceiros; medir (a) % de citações verificadas, (b) relevância dos precedentes (nota do advogado),
   (c) tempo de geração. **Red team:** pedidos como "cite 5 REsp recentes sobre X" não podem gerar
   número inventado.
7. **Postgres real:** testar o índice full-text e a migration em banco descartável (como na busca global).
8. **Piloto (camada 1):** 3–5 escritórios; métricas: citações por peça, % verificadas, % não encontradas
   (mostra onde falta fonte), uso do verificador avulso.
9. **Precisão do extrator num corpus real:** 100–200 peças públicas (acórdãos e petições publicadas) com as
   citações marcadas à mão por um advogado. Medir **acerto** (o que o extrator achou é mesmo citação) e
   **cobertura** (das citações reais, quantas achou). Meta inicial: acerto ≥ 98%, cobertura ≥ 95%. O
   corpus vira teste de regressão.
10. **Conferência da base:** amostra de 50 precedentes importados comparados com a página oficial
   (número, relator, data, ementa) — 100% iguais antes de liberar; repetir a cada mudança no importador.
11. **Playwright (frontend):** smoke do modal com uma peça de exemplo (resposta da API simulada) checando
   as cores, o resumo e o botão "Remover não verificadas"; regressão visual em `visual.spec.ts`.
12. **Teste com usuários:** 3–5 advogados, tarefas como "gere uma contestação e diga se pode protocolar"
   e "confira as citações desta apelação". Observar se entendem âmbar ≠ falso e se clicam na fonte.

---

## 9. Fases e checklist

### Fase 0 — Proteção (P)
- [ ] Regra "não citar julgados fora da lista" no prompt da peça
- [ ] `BuscarJurisprudenciaAsync` → "entendimentos sobre o tema", sem identificar julgados; renomear na tela
- [ ] Extrator de citações (4.1) + marcação "não verificada" no texto da peça
- [ ] Aviso fixo no modal da peça
- [ ] Testes do extrator e de contrato dos prompts

### Fase 1 — Verificador (M)
- [ ] Entidade `Precedente` + migration (índice único + GIN)
- [ ] Ingestão de súmulas (STF, SV, STJ, TST) e temas (repetitivos STJ, RG STF)
- [ ] `VerificadorCitacoesService` com os status da seção 4.2
- [ ] `PecaGerada.CitacoesJson` + destaques coloridos na tela
- [ ] Verificador avulso (texto colado / documento do processo)
- [ ] Chave `RecursosEmTeste.JurisprudenciaVerificada` + piloto

### Fase 2 — Jurisprudência com fonte (M/G)
- [ ] Ingestão dos espelhos de acórdãos do STJ (`PrecedentesImportJob`)
- [ ] Busca full-text + fluxo RAG no `PecaJuridicaService` com saída estruturada
- [ ] Cartões de precedentes com link na tela; `PrecedentesUsadosJson`
- [ ] `Tenant.PreferenciasIA` + Configurações → Inteligência Artificial (camada 2)
- [ ] Avaliação de qualidade (8.6) antes de abrir

### Fase 3 — Mais fontes
- [ ] STF (acórdãos), TST, TJs via fornecedor (avaliar custo e licença)
- [ ] Busca semântica (embeddings) se o full-text não bastar
- [ ] Página pública "Verificador de citações" (captação, como as calculadoras)

---

## 10. Decisões em aberto

1. Fase 0: **remover** ou só **marcar** citações não verificadas por padrão? (Sugestão: marcar; a opção
   "bloquear" fica para o escritório.)
2. Guardar só ementas ou também íntegras dos acórdãos (espaço × qualidade da busca)?
3. Verificador avulso gratuito para todos os planos (captação) ou só pagos?
4. Fornecedor para TJs: qual, a que custo, e se a licença permite exibir e guardar.
5. Exibir a data da última atualização da base na tela ("base do STJ atualizada em ...")?

---

## Referências

- [TJSC — multa por jurisprudência falsa gerada por IA](https://www.tjsc.jus.br/web/imprensa/-/tjsc-multa-autor-de-recurso-por-jurisprudencia-falsa-gerada-por-ia)
- [ConJur — TRT-18 multa empresa e advogado por jurisprudência falsa](https://www.conjur.com.br/?p=849434)
- [ConJur — TSE confirma multa por precedentes inexistentes](https://www.conjur.com.br/?p=835917)
- [Migalhas — TRT-3 multa após súmula gerada por IA](https://www.migalhas.com.br/quentes/443083/trt-3-aplica-multa-apos-advogado-citar-sumula-gerada-por-ia)
- [ConJur — "A IA não errou, foi o advogado que não revisou" (jun/2026)](https://www.conjur.com.br/2026-jun-10/a-inteligencia-artificial-nao-errou-foi-o-advogado-que-nao-revisou/)
- [STJ — Novo Portal de Dados Abertos](https://www.stj.jus.br/sites/portalp/Paginas/Comunicacao/Noticias/20052022-Novo-Portal-de-Dados-Abertos-amplia-transparencia-de-acoes-realizadas-no-STJ.aspx)
- [CNJ — Portal de dados abertos do STJ em formato legível por máquina](https://www.cnj.jus.br/portal-de-dados-abertos-do-stj-oferece-informacoes-em-formato-legivel-por-maquina/)
- [STJ — Termo de uso dos dados abertos](https://bdjur.stj.jus.br/jspui/bitstream/2011/179190/Termo%20de%20Uso%20-%20Dados%20Abertos.pdf)
