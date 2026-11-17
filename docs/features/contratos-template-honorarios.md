# Proposta de template: Contrato de Prestação de Serviços Advocatícios (honorários)

Proposta para o time do Contratos. É um template de contrato de honorários pensado para a integração com
o Causify. As chaves e os rótulos das variáveis foram escolhidos para o Causify preencher quase tudo
sozinho a partir do contrato de honorários. Quem envia só confere e completa o que falta.

> **Atenção:** o texto do contrato é um modelo de partida e precisa ser revisado por um advogado antes
> de virar template publicado. Cada escritório pode ajustar cláusulas, percentuais e o foro.

---

## 1. Como o Causify preenche as variáveis

Ao enviar um contrato de honorários para assinatura, o Causify busca o template pela API
(`GET /templates/{id}`). Para cada variável, ele sugere uma fonte de dados com base na **chave** e no
**rótulo**:
- Palavras como **contratante** ou **cliente** indicam o cliente.
- **Contratada**, **escritório** ou **advogado** indicam o escritório ou o advogado.
- O tipo da variável também conta: `money`, `date` e `cpf_cnpj`.

O usuário pode trocar a fonte, e a escolha fica salva para aquele template.

Por isso a proposta segue três regras:

1. Chaves agrupadas por papel: `contratante.*` para o cliente e `contratada.*` para o escritório.
2. Rótulos com a palavra que identifica a parte, por exemplo "CPF ou CNPJ **do contratante**".
3. Tipos certos: valores em `money`, datas em `date`, CPF/CNPJ em `cpf_cnpj` (o Contratos confere os
   dígitos).

---

## 2. Variáveis

Valores do tipo `money` vão como `"1500.50"`; datas, como `"AAAA-MM-DD"` (formato da API v1).

### Partes

| Chave | Rótulo | Tipo | Obrig. | Preenchido pelo Causify com |
|---|---|---|---|---|
| `contratante.nome` | Nome do contratante | text | sim | Nome do cliente |
| `contratante.documento` | CPF ou CNPJ do contratante | cpf_cnpj | sim | CPF/CNPJ do cliente |
| `contratante.qualificacao` | Qualificação do contratante (nacionalidade, estado civil, profissão) | text | não | — (digitado) |
| `contratante.endereco` | Endereço completo do contratante | text | sim | Endereço, cidade/UF e CEP do cliente |
| `contratante.email` | E-mail do contratante | text | não | E-mail do cliente |
| `contratada.nome` | Nome do escritório (contratada) | text | sim | Nome do escritório |
| `contratada.cnpj` | CNPJ do escritório (contratada) | cpf_cnpj | não | CNPJ do escritório |
| `contratada.endereco` | Endereço do escritório | text | sim | Endereço do escritório |
| `advogado.nome` | Nome do advogado responsável | text | sim | Usuário que está enviando |
| `advogado.oab` | Inscrição na OAB do advogado (nº/UF) | text | sim | — (digitado) |

### Objeto

| Chave | Rótulo | Tipo | Obrig. | Preenchido pelo Causify com |
|---|---|---|---|---|
| `objeto` | Serviço contratado (objeto) | text | sim | Serviço (objeto) do contrato de honorários |
| `processo.numero` | Número do processo, se já houver | text | não | Número CNJ do processo vinculado |
| `contrato.inicio` | Início da prestação dos serviços | date | sim | Data de início do contrato |

### Honorários e pagamento

| Chave | Rótulo | Tipo | Obrig. | Preenchido pelo Causify com |
|---|---|---|---|---|
| `valor.total` | Valor total dos honorários | money | sim | Valor total do contrato |
| `pagamento.forma` | Forma de pagamento | text | sim | "À vista", "Parcelado" ou "Entrada + parcelas" |
| `valor.entrada` | Valor da entrada | money | não | Valor da entrada |
| `parcelas.quantidade` | Quantidade de parcelas | number | não | Número de parcelas |
| `pagamento.primeiro_vencimento` | Vencimento da 1ª parcela | date | não | 1º vencimento |
| `multa.percentual` | Multa por atraso (%) | number | sim | — (digitado) |
| `juros.percentual` | Juros de mora ao mês (%) | number | sim | — (digitado) |
| `exito.percentual` | Honorários de êxito (% do proveito econômico) | number | não | — (digitado) |

### Fechamento

| Chave | Rótulo | Tipo | Obrig. | Preenchido pelo Causify com |
|---|---|---|---|---|
| `foro.cidade` | Cidade do foro | text | sim | Cidade do cliente (ajustável) |
| `assinatura.cidade` | Cidade da assinatura | text | sim | Cidade do cliente (ajustável) |
| `assinatura.data` | Data da assinatura | date | sim | Data de hoje |

**Ficam para digitar:** OAB, qualificação, percentuais de multa, juros e êxito. O Causify guarda
multa e juros no contrato de honorários, mas ainda não os oferece como fonte. Dá para incluir depois,
e aí as variáveis `multa.percentual` e `juros.percentual` passariam a vir preenchidas.

**Evitar rótulos que confundem a sugestão:**
- "Valor de cada parcela": com `money`, o Causify sugere o valor total.
- "Comarca do foro": a palavra "comarca" leva à comarca do processo.

Os rótulos da tabela já evitam esses dois casos.

---

## 3. Signatários sugeridos

| Ordem | Papel (`role`) | Ação (`action`) | No Causify |
|---|---|---|---|
| 1 | Contratante | `sign` | Cliente: e-mail, ou WhatsApp quando não há e-mail; CPF quando é pessoa física |
| 2 | Contratada | `sign` | Advogado que envia |
| 3 | Testemunha | `sign_as_witness` | Opcional, adicionada na hora |
| 4 | Testemunha | `sign_as_witness` | Opcional, adicionada na hora |

O Causify usa o 1º papel sugerido para o cliente e o 2º para o advogado. As testemunhas são opcionais
(o contrato com duas testemunhas vale como título executivo, art. 784, III, do CPC); quem envia
adiciona na hora se quiser.

---

## 4. Texto do template

A sintaxe de variável abaixo (`{{chave}}`) é ilustrativa. Use a do editor de templates do Contratos.

```text
CONTRATO DE PRESTAÇÃO DE SERVIÇOS ADVOCATÍCIOS

CONTRATANTE: {{contratante.nome}}, {{contratante.qualificacao}}, inscrito(a) no CPF/CNPJ sob o
nº {{contratante.documento}}, com endereço em {{contratante.endereco}}.

CONTRATADA: {{contratada.nome}}, inscrita no CNPJ sob o nº {{contratada.cnpj}}, com sede em
{{contratada.endereco}}, neste ato representada por {{advogado.nome}}, inscrito(a) na OAB sob o
nº {{advogado.oab}}.

As partes acima identificadas celebram o presente contrato, que se rege pelas cláusulas seguintes.

CLÁUSULA 1ª – DO OBJETO
1.1. A CONTRATADA prestará ao CONTRATANTE os seguintes serviços jurídicos: {{objeto}}.
1.2. Processo relacionado (quando houver): {{processo.numero}}.
1.3. Os serviços terão início em {{contrato.inicio}} e abrangem a atuação até a decisão final na
instância em que a causa estiver, salvo disposição diversa no objeto. Recursos a tribunais
superiores, ações autônomas, execuções e incidentes não previstos no objeto dependem de ajuste à parte.

CLÁUSULA 2ª – DOS HONORÁRIOS CONTRATUAIS
2.1. Pelos serviços, o CONTRATANTE pagará à CONTRATADA honorários de {{valor.total}}.
2.2. Forma de pagamento: {{pagamento.forma}}. Entrada de {{valor.entrada}} e {{parcelas.quantidade}}
parcela(s), com o primeiro vencimento em {{pagamento.primeiro_vencimento}} e as demais nos meses
subsequentes, no mesmo dia.
2.3. Em caso de atraso, incidirão multa de {{multa.percentual}}% sobre o valor em atraso e juros de
mora de {{juros.percentual}}% ao mês, calculados pro rata die, além de correção monetária.
2.4. Honorários de êxito (quando houver): {{exito.percentual}}% sobre o proveito econômico obtido
pelo CONTRATANTE, devidos no recebimento.

CLÁUSULA 3ª – DOS HONORÁRIOS DE SUCUMBÊNCIA
3.1. Os honorários de sucumbência pertencem exclusivamente à CONTRATADA (art. 23 da Lei nº 8.906/94)
e não se compensam com os honorários contratuais.

CLÁUSULA 4ª – DAS DESPESAS
4.1. Custas, emolumentos, perícias, diligências, cópias e deslocamentos fora da comarca correm por
conta do CONTRATANTE, mediante prévia comunicação e comprovação.

CLÁUSULA 5ª – DAS OBRIGAÇÕES DAS PARTES
5.1. A CONTRATADA atuará com zelo e diligência, mantendo o CONTRATANTE informado do andamento, sem
garantia de resultado, por se tratar de obrigação de meio.
5.2. O CONTRATANTE fornecerá documentos e informações verdadeiros e completos, no prazo solicitado,
e manterá seus dados de contato atualizados.

CLÁUSULA 6ª – DA RESCISÃO E DA REVOGAÇÃO DO MANDATO
6.1. Qualquer das partes poderá encerrar este contrato mediante comunicação por escrito.
6.2. Na revogação do mandato ou rescisão sem culpa da CONTRATADA, serão devidos os honorários
vencidos e os proporcionais aos serviços já prestados, além dos de êxito, se o proveito vier a ser
obtido em razão do trabalho realizado.

CLÁUSULA 7ª – DA PROTEÇÃO DE DADOS
7.1. A CONTRATADA tratará os dados pessoais do CONTRATANTE exclusivamente para a execução deste
contrato e o cumprimento de obrigações legais, nos termos da Lei nº 13.709/2018 (LGPD), com sigilo
profissional.

CLÁUSULA 8ª – DA ASSINATURA ELETRÔNICA
8.1. As partes reconhecem a validade da assinatura eletrônica deste instrumento, nos termos do
art. 10, § 2º, da MP nº 2.200-2/2001 e da Lei nº 14.063/2020, e do registro de evidências
(data, hora, IP e autenticação) emitido pela plataforma de assinatura.

CLÁUSULA 9ª – DO FORO
9.1. Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer questões oriundas deste
contrato.

{{assinatura.cidade}}, {{assinatura.data}}.

_________________________________________
CONTRATANTE: {{contratante.nome}}

_________________________________________
CONTRATADA: {{contratada.nome}}
{{advogado.nome}} – OAB {{advogado.oab}}

TESTEMUNHAS:
1. ______________________________________
2. ______________________________________
```

---

## 5. Pedidos ao time do Contratos

1. **Trechos condicionais.** Processo, entrada, parcelas e êxito são opcionais. Sem eles, o texto
   fica com frases incompletas ("Processo relacionado: ."). Duas saídas:
   - **Preferível:** suporte a blocos que só aparecem com a variável preenchida, por exemplo
     `{{#se processo.numero}} … {{/se}}`.
   - **Alternativa:** publicar variações do template (à vista; parcelado; com êxito).
2. **Percentuais.** Um tipo `percent`, ou o `number` aceitando `"2"`, `"1,5"` e `"1.5"` e
   mostrando "2%" e "1,5%" no PDF, evita o "%" fixo no texto.
3. **Valor por extenso.** Contratos de honorários costumam trazer o valor por extenso. Um formato
   como `{{valor.total|extenso}}` ("R$ 1.500,50 (mil e quinhentos reais e cinquenta centavos)")
   evita uma variável a mais e erros de digitação.
4. **Template de exemplo publicado.** Deixar este template disponível como modelo em todas as contas
   do plano Empresa, para o escritório partir dele e só ajustar.
