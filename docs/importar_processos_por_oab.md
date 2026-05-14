# Importação de Processos por OAB e UF

## Visão geral da solução

A abordagem usa **duas fontes complementares**, pois a API pública do CNJ (DataJud) **não recebe dados de partes/advogados do TJSP**:

| Fonte | Cobre | Como busca |
|---|---|---|
| **ESAJ TJSP** (portal nativo) | 1º e 2º grau TJSP | Scraping de HTML — busca pública por OAB |
| **DataJud CNJ** (API REST) | TRF, TRT e demais tribunais do UF | Elasticsearch query por `advocacia.numeroOAB` |

---

## Fonte 1 — ESAJ TJSP

### Por que o DataJud não serve para o TJSP

O JSON retornado pelo DataJud para processos do TJSP **não contém os campos `partes` nem `advocacia`**. O TJSP não envia esses dados ao CNJ. Qualquer query por OAB no índice `api_publica_tjsp` retorna 0 resultados — não é erro de query, é ausência de dado.

### Endpoints ESAJ

| Operação | URL |
|---|---|
| Busca por OAB — 1º Grau | `GET https://esaj.tjsp.jus.br/cpopg/search.do?...` |
| Busca por OAB — 2º Grau | `GET https://esaj.tjsp.jus.br/cposg/search.do?...` |
| Detalhe do processo — 1º Grau | `GET https://esaj.tjsp.jus.br/cpopg/show.do?processo.numero=XXXXXXX` |
| Detalhe do processo — 2º Grau | `GET https://esaj.tjsp.jus.br/cposg/show.do?processo.numero=XXXXXXX` |

### Parâmetros da busca por OAB

```
GET /cpopg/search.do
  ?conversationId=
  &cbPesquisa=NUMOAB
  &dadosConsulta.valorConsulta=416717SP       <- número + UF colados, sem barra
  &dadosConsulta.tipoNuProcesso=UNIFICADO
  &numeroDigitoAnoUnificado=
  &foroNumeroUnificado=
  &dadosConsulta.valorConsultaNuUnificado=
  &pageNumber=1
```

- Sem autenticação — acesso público
- Retorna até 25 processos por página
- Paginação via `pageNumber`
- Headers necessários: `User-Agent` de browser real (o ESAJ bloqueia sem UA)

### Fluxo de importação ESAJ

```
1. GET search.do?cbPesquisa=NUMOAB&valorConsulta=416717SP
   → HTML com lista de links class="linkProcesso"
   → Extrair números de processo via regex

2. Para cada número → GET show.do?processo.numero=XXXXXXX
   → HTML com dados completos do processo
   → Parser extrai todos os campos pelos IDs do HTML

3. Repetir com pageNumber++ enquanto houver próxima página
```

### Estrutura HTML do detalhe (confirmada pelo HTML real)

#### Cabeçalho — `div#containerDadosPrincipaisProcesso`

```html
<span id="numeroProcesso">0103706-25.2002.8.26.0100</span>
<span id="labelSituacaoProcesso" class="unj-tag">Suspenso</span>
<span id="classeProcesso" title="Cumprimento de sentença">Cumprimento de sentença</span>
<span id="assuntoProcesso" title="DIREITO CIVIL">DIREITO CIVIL</span>
<span id="foroProcesso" title="Foro Central Cível">Foro Central Cível</span>
<span id="varaProcesso" title="35ª Vara Cível">35ª Vara Cível</span>
<span id="juizProcesso" title="Gustavo Henrique Bretas Marzagão">Gustavo Henrique Bretas Marzagão</span>
```

#### Detalhes secundários — `div#maisDetalhes` (seção colapsável)

```html
<div id="dataHoraDistribuicaoProcesso">29/05/2002 às 12:24 - Livre</div>
<div id="areaProcesso"><span title="Cível">Cível</span></div>
<div id="valorAcaoProcesso">R$ 14.524,64</div>
<div id="numeroControleProcesso">2002/001545</div>
```

#### Partes — `table#tablePartesPrincipais`

```html
<tr class="fundoClaro">
  <td class="label">
    <span class="tipoDeParticipacao">Exeqte</span>
  </td>
  <td class="nomeParteEAdvogado">
    Eduardo Henrique Osório
    <br/>
    <span class="mensagemExibindo">Advogado:</span>
    Marcelo Sanchez Cantero
  </td>
</tr>
```

**Regra crítica:** nome da parte e advogado ficam na **mesma `<td>`**, separados por `<br/>`.
Dividir no `<br/>` — antes = nome da parte, depois = advogado(s).

#### Movimentos — `tbody#tabelaUltimasMovimentacoes`

```html
<tr class="containerMovimentacao">
  <td class="dataMovimentacao">24/10/2022</td>
  <td><!-- ícone opcional --></td>
  <td class="descricaoMovimentacao">
    Decurso de Prazo
    <br/>
    <span style="font-style: italic;">
      Certidão - Decurso de Prazo - Movimentação
    </span>
  </td>
</tr>
```

**Regra crítica:** o complemento usa entidades HTML (`&atilde;`, `&ccedil;` etc.) — fazer decode.

### Campos extraídos por ID (mapeamento completo)

| Campo | Seletor HTML | Exemplo de valor |
|---|---|---|
| Número do processo | `id="numeroProcesso"` | `0103706-25.2002.8.26.0100` |
| Situação | `id="labelSituacaoProcesso"` | `Suspenso` |
| Classe | `id="classeProcesso"` | `Cumprimento de sentença` |
| Assunto | `id="assuntoProcesso"` | `DIREITO CIVIL` |
| Foro | `id="foroProcesso"` | `Foro Central Cível` |
| Vara | `id="varaProcesso"` | `35ª Vara Cível` |
| Juiz | `id="juizProcesso"` | `Gustavo Henrique Bretas Marzagão` |
| Distribuição | `id="dataHoraDistribuicaoProcesso"` | `29/05/2002 às 12:24 - Livre` |
| Área | `id="areaProcesso"` | `Cível` |
| Valor da ação | `id="valorAcaoProcesso"` | `R$ 14.524,64` |
| Nº Controle | `id="numeroControleProcesso"` | `2002/001545` |

### Estrutura do objeto retornado

```typescript
interface ProcessoESAJ {
  numeroProcesso: string;
  situacao: string;
  classe: string;
  assunto: string;
  area: string;
  foro: string;
  vara: string;
  juiz: string;
  dataDistribuicao: string;
  valorAcao: string;
  numeroControle: string;
  partes: Array<{
    nome: string;
    polo: string;        // "Exeqte", "Exectdo", "Autor", "Réu", etc.
    advogados: string[];
  }>;
  movimentos: Array<{
    data: string;        // "dd/mm/yyyy"
    titulo: string;      // "Decurso de Prazo"
    complemento: string; // "Certidão - Decurso de Prazo - Movimentação"
  }>;
  _fonte: "esaj";
  _grau: "G1" | "G2";
}
```

---

## Fonte 2 — DataJud CNJ

### Quando usar

Para TRF, TRT e demais tribunais (exceto TJSP). Verificar se o tribunal envia o campo `advocacia` antes de usar — alguns também não preenchem.

### Autenticação

```
Authorization: APIKey cDZHYzlZa0JadVREZDJCendQbXY6SkJlTzNjLV9TRENyQk1RdnFKZGRQdw==
```

Chave pública do CNJ — verificar versão vigente: https://datajud-wiki.cnj.jus.br/api-publica/acesso/

### Query por OAB

```json
POST https://api-publica.datajud.cnj.jus.br/api_publica_{sigla}/_search

{
  "query": {
    "bool": {
      "must": [{
        "nested": {
          "path": "advocacia",
          "query": {
            "bool": {
              "must": [
                { "match": { "advocacia.numeroOAB": "416717" } },
                { "match": { "advocacia.ufOAB": "SP" } }
              ]
            }
          }
        }
      }]
    }
  },
  "size": 100,
  "sort": [
    { "dataAjuizamento": { "order": "desc" } },
    { "_id": { "order": "asc" } }
  ]
}
```

### Mapeamento UF → Tribunais DataJud

```javascript
// TJSP removido — usa ESAJ
const TRIBUNAIS_POR_UF = {
  AC:["tjac","trf1","trt14"],  AL:["tjal","trf5","trt19"],
  AM:["tjam","trf1","trt11"],  AP:["tjap","trf1","trt8"],
  BA:["tjba","trf1","trt5"],   CE:["tjce","trf5","trt7"],
  DF:["tjdft","trf1","trt10"], ES:["tjes","trf2","trt17"],
  GO:["tjgo","trf1","trt18"],  MA:["tjma","trf1","trt16"],
  MG:["tjmg","trf1","trt3"],   MS:["tjms","trf3","trt24"],
  MT:["tjmt","trf1","trt23"],  PA:["tjpa","trf1","trt8"],
  PB:["tjpb","trf5","trt13"],  PE:["tjpe","trf5","trt6"],
  PI:["tjpi","trf1","trt22"],  PR:["tjpr","trf4","trt9"],
  RJ:["tjrj","trf2","trt1"],   RN:["tjrn","trf5","trt21"],
  RO:["tjro","trf1","trt14"],  RR:["tjrr","trf1","trt11"],
  RS:["tjrs","trf4","trt4"],   SC:["tjsc","trf4","trt12"],
  SE:["tjse","trf5","trt20"],
  SP:["trf3","trt2","trt15"],
  TO:["tjto","trf1","trt10"]
};
```

---

## Arquitetura do proxy Node.js

O browser não pode chamar ESAJ nem DataJud diretamente (CORS). Um servidor local atua como proxy:

```
Browser → POST /api/datajud/:tribunal  → Node → DataJud CNJ
Browser → GET  /api/esaj/oab/:num/:uf  → Node → ESAJ TJSP (lista)
Browser → GET  /api/esaj/processo/:num → Node → ESAJ TJSP (detalhe)
Browser → GET  /api/esaj/debug/:num    → Node → ESAJ TJSP (HTML bruto)
```

Dependência única: `express ^4.18.2`. Node.js 18+ (fetch nativo).

---

## Considerações de produção

**Performance:** enriquecimento em lotes paralelos de 5, com 300ms de pausa entre lotes para não bloquear o ESAJ.

**Paginação ESAJ:** até 25 por página — iterar `pageNumber++` enquanto houver próxima página.

**Entidades HTML:** o ESAJ usa `&atilde;`, `&ccedil;`, etc. nos movimentos — sempre fazer decode antes de persistir.

**Seção maisDetalhes:** é colapsável no frontend mas o HTML completo sempre vem do servidor — o parser lê normalmente mesmo com `class="collapse"`.

**Processo sigiloso:** o servidor retorna HTML com `id="popupSenha"` — detectar esse ID e marcar o processo como `sigiloso: true` sem tentar parsear os campos.

**Campos vazios:** se juiz, valor ou outros campos não estiverem preenchidos, o ID existe no HTML mas o conteúdo é string vazia — tratar como `""`, não como erro.

---

## Referências

- ESAJ 1º Grau: https://esaj.tjsp.jus.br/cpopg/open.do
- ESAJ 2º Grau: https://esaj.tjsp.jus.br/cposg/open.do
- DataJud Wiki: https://datajud-wiki.cnj.jus.br/api-publica/
- Chave DataJud vigente: https://datajud-wiki.cnj.jus.br/api-publica/acesso/
