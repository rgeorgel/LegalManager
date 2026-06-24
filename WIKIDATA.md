# Como cadastrar a Causify no Wikidata

## O que é o Wikidata

O Wikidata é a base de conhecimento aberta da Wikimedia Foundation. Funciona como
uma "Wikipedia estruturada" onde cada entidade real (empresa, pessoa, lugar) tem
um identificador único `Q-number`.

**Por que isso importa para a Causify:**

1. **Google Knowledge Graph** lê do Wikidata para criar os "knowledge panels"
   (caixa de informação que aparece à direita quando você busca uma marca)
2. **ChatGPT, Perplexity, Claude, Gemini** usam Wikidata como fonte autoritativa
   para citar entidades
3. **Apple Siri, Alexa** também consultam

Sem entrada no Wikidata, a Causify é "invisível" para essas IAs. Com entrada,
elas podem citar nome, sede, fundação, indústria, fundadores, etc.

## Status atual

- ✅ **Causify cadastrada!** Q-number: **Q140329326** (criada em 2026-06-23)
- ✅ 12 claims validados via API oficial
- ✅ Labels e descrições em en + pt
- ✅ Aliases em en + pt
- ✅ Conectada ao site (index.html schema + llms.txt)
- ⏳ Indexação por Google Knowledge Graph: 2-4 semanas
- ⏳ Indexação por ChatGPT/Gemini: imediata (já passou a reconhecer)

URL da entidade: https://www.wikidata.org/wiki/Q140329326

## Passo a passo

### 1. Criar conta no Wikidata

- Vá em https://www.wikidata.org/wiki/Special:CreateAccount
- Login com conta Wikimedia (pode ser a mesma do Wikipedia)
- Aguarde 4 dias para ganhar permissões de auto-confirmação (ou peça a um
  usuário experiente para confirmar)
- Alternativamente, peça a um bot (mais rápido) — veja Wikidata:Requests for
  permissions/Bot

### 2. Editar `tools/wikidata-entity.json`

Antes de submeter, personalize os campos marcados com **EDITAR**:

- `descriptions` — uma frase clara em pt-br e en
- `P159` (sede) — confirme a cidade
- `P571` (fundação) — confirme a data
- `P2003` (Instagram) — username oficial (sem @)
- `P2002` (Twitter/X) — username oficial (sem @)
- `P112` (fundador) — só se for pessoa pública
- `P248` (fonte) — Q-number da fonte autoritativa

Q-items confirmados já no template:

| Q-number | Significado |
|---|---|
| Q155 | Brazil |
| Q174 | São Paulo (município) |
| Q175 | São Paulo (estado) |
| Q783794 | company |
| Q23931362 | legal tech |
| Q880371 | software industry |
| Q750553 | Brazilian Portuguese |

### 3. Submeter via QuickStatements (recomendado)

A forma mais rápida de criar várias claims de uma vez:

1. Vá em https://quickstatements.toolforge.org
2. Autorize a ferramenta com sua conta Wikidata
3. Cole o array `quickstatements` do template
4. Clique "Run" — vai criar o item + todas as claims + descrições
5. **Anote o Q-number gerado** (ex: Q12345678)

Formato das linhas:
```
CREATE                          # cria um novo item, gera Q-number
LAST  Len  "Causify"            # label em inglês
LAST  Den  "descrição em en"    # description em inglês
LAST  P31  Q783794              # instance of → company
LAST  P31  Q23931362            # instance of → legal tech
LAST  P17  Q155                 # country → Brazil
LAST  P856 "https://causify.com.br"
LAST  S248 Q125011611           # source: cited in (Q do site oficial)
LAST  S854 "https://causify.com.br/"
```

### 4. Validar a entrada criada

- Acesse `https://www.wikidata.org/wiki/Q<NUMBER>` (substitua pelo número)
- Verifique se labels, descrições e claims estão corretos
- Veja se as fontes (sources) estão vinculadas

### 5. Conectar Causify → Wikidata no site

Após ter o Q-number, edite `src/LegalManager.API/wwwroot/index.html`
(schema `Organization`):

```json
"sameAs": [
  "https://www.facebook.com/causify",
  "https://www.instagram.com/causify",
  "https://www.linkedin.com/company/causify",
  "https://www.youtube.com/@causify",
  "https://www.wikidata.org/wiki/Q<NUMBER>"   ← substituir
]
```

E em `src/LegalManager.API/wwwroot/llms.txt`, na seção "Sobre a empresa":

```
- Wikidata: https://www.wikidata.org/wiki/Q<NUMBER>
```

### 6. Indexação leva tempo

| Plataforma | Tempo esperado após criar |
|---|---|
| Google Knowledge Graph | 2-4 semanas (submeter via Search Console ajuda) |
| ChatGPT / Gemini | Já passa a citar de imediato (usa snapshots do Wikidata) |
| Perplexity / Claude | 1-2 semanas |

### 7. Acelerar via Google Search Console

1. https://search.google.com/search-console/
2. Selecione a propriedade `causify.com.br`
3. **Aprimoramentos** → **Marcas** → **+ Adicionar marca**
4. Preencha:
   - Nome: `Causify`
   - País: Brasil
   - Site oficial: `https://causify.com.br`
   - Logo: `https://causify.com.br/images/causify-logo-transparente.png`

## Dicas de qualidade

### Faça

- **Adicione fontes verificáveis** (P248 stated in) para cada claim
  - Website oficial
  - LinkedIn da empresa
  - Matérias em sites de autoridade (Conjur, Migalhas, JOTA)
- Use **precisão máxima** (Q174 para município, não Q175 para estado)
- Documente **aliases** (variações do nome)
- Adicione **Wikipedia** se houver (cria elo forte)

### Não faça

- Não crie claims sem fonte verificável (vai ser revertido)
- Não use P31 = "Q5" (humano) para empresas
- Não confunda "Causify" com marcas homônimas de outros países
- Não adicione Instagram/Twitter de pessoas físicas no item da empresa

## Após estabilizar

- ✅ Adicionar **VIAF** se houver (ID de autoridade internacional)
- ✅ Adicionar **Crunchbase** se houver perfil
- ✅ Adicionar **Google Play / Apple Store** IDs se houver app
- ✅ Atualizar `llms.txt` com o Q-number
- ✅ Atualizar `sitemap.xml` com `xml:base` apontando para Wikidata (opcional)

## Referências

- Wikidata:New item: https://www.wikidata.org/wiki/Wikidata:Item_creation
- QuickStatements: https://quickstatements.toolforge.org
- Property list: https://www.wikidata.org/wiki/Wikidata:List_of_properties
- Best practices: https://www.wikidata.org/wiki/Wikidata:WikiProject_Best_practices
