# Causify — Posts em vídeo com a identidade Brutalista

Oito vídeos curtos (7 s, 30 fps, MP4 H.264, sem áudio) feitos a partir do **painel esquerdo
dos logins brutalistas** (`docs/design/login-propostas.html`, propostas J a J3): a manchete
em caixa-alta que cai na tela, a faixa de destaque que varre a última linha, os selos coloridos
e, no fim, o botão do login vira o CTA "Teste grátis · causify.com.br".

São **2 variações para cada um dos 4 logins brutalistas**:

| Login | Variação 1 | Variação 2 |
|---|---|---|
| Amarelo (original, assunto prazo) | **Prazo perdido não volta.** Placa com o CPC, art. 223 sendo datilografada | **Só conta dia útil.** Semana de 18 a 24/11 com feriado (20/11), sábado e domingo riscados; os dias úteis vão sendo contados |
| Cobalto (publicações) | **Publicou? A gente avisa.** Selo "DJE lido hoje às 06h" girando e a fita de tribunais correndo | **Intimação lida antes do seu café.** Cartão "Nova intimação" sobe com o prazo já calculado |
| Concreto (organização e rotina) | **Nenhum prazo na gaveta.** Faixas zebradas de obra correndo e o número CNJ datilografado na placa | **Cada tarefa tem dono.** Kanban Pendente → Em andamento → Concluída, com o card "Contestação" atravessando o quadro |
| Madrugada (prazo fatal) | **Vence às 23h59.** Relógio acelerando de 23:41 até 23:58 e a regra da Lei 11.419/2006 | **Sem susto às 23h41.** Relógio vira 00:00 e chega o e-mail "Seu dia" com os prazos de hoje |

Cada vídeo sai em dois formatos:

- **Instagram:** 1080×1350 (4:5), o formato de feed que o Instagram recomenda.
- **LinkedIn e Facebook:** 1080×1080 (quadrado). Os dois exibem 1:1 no feed sem cortar.

Também sai uma **capa PNG** de cada vídeo (último quadro, com tudo na tela), para usar como
miniatura no LinkedIn/Facebook ou como capa no Instagram, e como post estático caso não queiram
o vídeo.

## Arquivos (`out/`)

| Post | Instagram (1080×1350) | LinkedIn / Facebook (1080×1080) |
|---|---|---|
| Amarelo 1 | `causify-bru-amarelo-prazo-perdido-instagram-1080x1350.mp4` | `causify-bru-amarelo-prazo-perdido-1080x1080.mp4` |
| Amarelo 2 | `causify-bru-amarelo-dia-util-instagram-1080x1350.mp4` | `causify-bru-amarelo-dia-util-1080x1080.mp4` |
| Cobalto 1 | `causify-bru-cobalto-publicou-instagram-1080x1350.mp4` | `causify-bru-cobalto-publicou-1080x1080.mp4` |
| Cobalto 2 | `causify-bru-cobalto-antes-do-cafe-instagram-1080x1350.mp4` | `causify-bru-cobalto-antes-do-cafe-1080x1080.mp4` |
| Concreto 1 | `causify-bru-concreto-gaveta-instagram-1080x1350.mp4` | `causify-bru-concreto-gaveta-1080x1080.mp4` |
| Concreto 2 | `causify-bru-concreto-tarefa-tem-dono-instagram-1080x1350.mp4` | `causify-bru-concreto-tarefa-tem-dono-1080x1080.mp4` |
| Madrugada 1 | `causify-bru-madrugada-23h59-instagram-1080x1350.mp4` | `causify-bru-madrugada-23h59-1080x1080.mp4` |
| Madrugada 2 | `causify-bru-madrugada-seu-dia-instagram-1080x1350.mp4` | `causify-bru-madrugada-seu-dia-1080x1080.mp4` |

As capas têm o mesmo nome com `-capa.png` no lugar de `.mp4`.

Mesmo padrão dos posts navy (`marketing/posts/`): sem link clicável no corpo do LinkedIn (evita a
penalização de alcance), "Link na bio" no Instagram e só 2 hashtags no Facebook.

> Sobre o que os vídeos prometem: a leitura dos diários roda todo dia às 06h (Brasília) e o
> resumo "Seu dia" sai à meia-noite no fuso do escritório (`Program.cs`, `AlertasJob`). O selo e
> os textos falam em "Diário de Justiça"/"DJE", não em cobertura de todos os tribunais.

## Textos — LinkedIn

Vídeo quadrado 1080×1080. Subir como vídeo nativo (não como link), com a capa PNG como miniatura.

### Amarelo 1 — Prazo perdido não volta
**Vídeo:** `causify-bru-amarelo-prazo-perdido-1080x1080.mp4`

> Prazo perdido não volta.
>
> O CPC é direto (art. 223): decorrido o prazo, extingue-se o direito de praticar o ato. Não tem
> segunda chamada, não tem "eu ia protocolar amanhã".
>
> E quase nunca o problema é competência. É depender de memória, de planilha e de alguém lembrar
> de olhar.
>
> Prazo seguro é prazo que:
> → é contado em dias úteis, sem conta de cabeça
> → tem processo e responsável vinculados
> → avisa antes, não depois
>
> O Causify traz as publicações, conta o prazo e avisa a equipe antes do vencimento. Teste grátis
> em causify.com.br.
>
> #AdvocaciaDigital #PrazosProcessuais #GestãoJurídica #CPC #LegalTech

### Amarelo 2 — Só conta dia útil
**Vídeo:** `causify-bru-amarelo-dia-util-1080x1080.mp4`

> Fim de semana não conta. Feriado também não.
>
> Desde o CPC/2015, prazo processual em dias conta só os dias úteis (art. 219). Parece simples até
> a semana ter um feriado no meio, como o 20 de novembro, que virou feriado nacional em 2024.
>
> De quarta, 18, a terça, 24, são sete dias no calendário e só quatro no prazo. A conta de cabeça
> erra justamente na semana mais apertada.
>
> O Causify conta os prazos em dias úteis, já considerando os feriados, e avisa antes do
> vencimento. Teste grátis em causify.com.br.
>
> #PrazosProcessuais #CPC #AdvocaciaDigital #GestãoJurídica #LegalTech

### Cobalto 1 — Publicou? A gente avisa.
**Vídeo:** `causify-bru-cobalto-publicou-1080x1080.mp4`

> Publicou? A gente avisa.
>
> Todo dia, às 6h da manhã, o Causify lê o Diário de Justiça e traz as publicações dos seus
> processos para dentro do sistema. Antes de você abrir o e-mail.
>
> Sem entrar tribunal por tribunal. Sem depender de alguém lembrar de checar. Sem descobrir a
> intimação quando metade do prazo já correu.
>
> O Causify lê o diário todo dia, calcula o prazo e avisa por e-mail. Teste grátis em
> causify.com.br.
>
> #Publicações #Intimações #AdvocaciaDigital #GestãoJurídica #LegalTech

### Cobalto 2 — Intimação lida antes do seu café
**Vídeo:** `causify-bru-cobalto-antes-do-cafe-1080x1080.mp4`

> Às 6h a intimação já foi lida. Às 8h, você só decide o que fazer com ela.
>
> Começar o dia caçando publicação é trabalho que não devia ser seu. O tempo do advogado vale
> mais na estratégia do que na busca.
>
> No Causify, a publicação chega com processo vinculado e prazo já calculado. Você abre o sistema
> e a pauta do dia já está montada.
>
> Teste grátis em causify.com.br.
>
> #Intimações #PrazosProcessuais #AdvocaciaDigital #Produtividade #LegalTech

### Concreto 1 — Nenhum prazo na gaveta
**Vídeo:** `causify-bru-concreto-gaveta-1080x1080.mp4`

> Nenhum prazo na gaveta.
>
> Processo parado não avisa que está parado. Ele só aparece quando o cliente liga, ou quando o
> prazo já venceu.
>
> Escritório organizado é escritório em que cada processo tem próximo passo, responsável e data:
> tarefas num kanban, audiências na agenda e o cliente acompanhando pelo portal, sem precisar
> perguntar.
>
> O Causify junta tudo isso num lugar só. Teste grátis em causify.com.br.
>
> #GestãoDeEscritório #OrganizaçãoJurídica #AdvocaciaDigital #GestãoJurídica #LegalTech

### Concreto 2 — Cada tarefa tem dono
**Vídeo:** `causify-bru-concreto-tarefa-tem-dono-1080x1080.mp4`

> "De quem é essa tarefa?" Essa pergunta não devia existir.
>
> Quando ninguém sabe quem cuida de quê, a tarefa fica no meio do caminho, e prazo no meio do
> caminho é prazo em risco.
>
> No kanban do Causify cada card tem responsável e anda de Pendente para Em andamento e
> Concluída à vista da equipe inteira. Os prazos da agenda viram tarefas e entram no mesmo
> quadro.
>
> Teste grátis em causify.com.br.
>
> #GestãoDeEquipe #Kanban #AdvocaciaDigital #Produtividade #LegalTech

### Madrugada 1 — Vence às 23h59
**Vídeo:** `causify-bru-madrugada-23h59-1080x1080.mp4`

> Vence às 23h59.
>
> A Lei 11.419/2006 (art. 3º, parágrafo único) considera tempestiva a petição eletrônica enviada
> até as 24 horas do último dia. É uma garantia, não um plano.
>
> Sistema do tribunal instável, PDF grande demais, certificado vencido: tudo isso costuma
> aparecer justamente às 23h41.
>
> O Causify avisa os prazos dias antes do vencimento e manda o resumo do dia à meia-noite, para o
> último dia não virar madrugada. Teste grátis em causify.com.br.
>
> #PrazoFatal #ProcessoEletrônico #AdvocaciaDigital #GestãoJurídica #LegalTech

### Madrugada 2 — Sem susto às 23h41
**Vídeo:** `causify-bru-madrugada-seu-dia-1080x1080.mp4`

> Sem susto às 23h41.
>
> À meia-noite, cada pessoa da equipe recebe o "Seu dia": um e-mail só com os prazos, as tarefas
> e as audiências que vêm pela frente.
>
> Ninguém começa o dia sem saber o que vence hoje. E ninguém descobre um prazo fatal quando já
> está escuro lá fora.
>
> O resumo "Seu dia" já vem no Causify. Teste grátis em causify.com.br.
>
> #PrazoFatal #GestãoDeEquipe #AdvocaciaDigital #GestãoJurídica #LegalTech

## Textos — Facebook

Mesmo vídeo quadrado 1080×1080 do LinkedIn. Legenda curta e só 2 hashtags.

### Amarelo 1 — Prazo perdido não volta
**Vídeo:** `causify-bru-amarelo-prazo-perdido-1080x1080.mp4`

> Prazo perdido não volta. Decorrido o prazo, extingue-se o direito de praticar o ato (CPC, art. 223).
>
> O Causify conta os prazos em dias úteis e avisa antes do vencimento. Teste grátis em
> causify.com.br.
>
> #AdvocaciaDigital #GestãoJurídica

### Amarelo 2 — Só conta dia útil
**Vídeo:** `causify-bru-amarelo-dia-util-1080x1080.mp4`

> Sete dias no calendário, quatro no prazo: feriado e fim de semana não contam (CPC, art. 219).
>
> O Causify faz essa conta por você, com os feriados. Teste grátis em causify.com.br.
>
> #PrazosProcessuais #AdvocaciaDigital

### Cobalto 1 — Publicou? A gente avisa.
**Vídeo:** `causify-bru-cobalto-publicou-1080x1080.mp4`

> Publicou? A gente avisa.
>
> Todo dia às 6h o Causify lê o Diário de Justiça, calcula o prazo e avisa por e-mail. Teste grátis
> em causify.com.br.
>
> #AdvocaciaDigital #GestãoJurídica

### Cobalto 2 — Intimação lida antes do seu café
**Vídeo:** `causify-bru-cobalto-antes-do-cafe-1080x1080.mp4`

> Às 6h a intimação já foi lida. Às 8h você só decide o que fazer com ela.
>
> No Causify, a publicação chega com o prazo calculado. Teste grátis em causify.com.br.
>
> #AdvocaciaDigital #GestãoJurídica

### Concreto 1 — Nenhum prazo na gaveta
**Vídeo:** `causify-bru-concreto-gaveta-1080x1080.mp4`

> Processo parado não avisa que está parado.
>
> Tarefas, agenda de audiências e portal do cliente no mesmo lugar: nenhum prazo na gaveta.
> Teste grátis em causify.com.br.
>
> #GestãoDeEscritório #AdvocaciaDigital

### Concreto 2 — Cada tarefa tem dono
**Vídeo:** `causify-bru-concreto-tarefa-tem-dono-1080x1080.mp4`

> "De quem é essa tarefa?" No Causify a resposta está no card.
>
> Kanban com responsável, prazo e status à vista da equipe. Teste grátis em causify.com.br.
>
> #GestãoDeEquipe #AdvocaciaDigital

### Madrugada 1 — Vence às 23h59
**Vídeo:** `causify-bru-madrugada-23h59-1080x1080.mp4`

> A lei aceita a petição até as 24h do último dia. Seu escritório não precisa testar esse limite.
>
> O Causify avisa os prazos antes do último dia. Teste grátis em causify.com.br.
>
> #PrazoFatal #AdvocaciaDigital

### Madrugada 2 — Sem susto às 23h41
**Vídeo:** `causify-bru-madrugada-seu-dia-1080x1080.mp4`

> À meia-noite chega o "Seu dia": prazos, tarefas e audiências que vêm pela frente, num e-mail só.
>
> Sem susto às 23h41. Teste grátis em causify.com.br.
>
> #GestãoJurídica #AdvocaciaDigital

## Textos — Instagram

Vídeo 4:5, 1080×1350, publicado como post de feed (ou Reels; o Instagram mostra 4:5 sem corte
no feed). Use a capa PNG como capa. "Link na bio" no lugar da URL e 8 a 10 hashtags.

### Amarelo 1 — Prazo perdido não volta
**Vídeo:** `causify-bru-amarelo-prazo-perdido-instagram-1080x1350.mp4`

> Prazo perdido não volta. ⚠️
>
> CPC, art. 223: decorrido o prazo, extingue-se o direito de praticar o ato. Sem segunda chamada.
>
> O problema quase nunca é competência. É depender de memória e planilha. O Causify conta o
> prazo em dias úteis e avisa antes do vencimento.
>
> Teste grátis. Link na bio: causify.com.br
>
> #AdvocaciaDigital #PrazosProcessuais #GestãoJurídica #CPC #LegalTech #Advocacia #DireitoDigital #EscritórioDeAdvocacia #RotinaJurídica #DireitoBrasil

### Amarelo 2 — Só conta dia útil
**Vídeo:** `causify-bru-amarelo-dia-util-instagram-1080x1350.mp4`

> Só conta dia útil. 📅
>
> De quarta (18) a terça (24): sete dias no calendário, quatro no prazo. Sexta é feriado (20/11),
> sábado e domingo não contam. É o CPC, art. 219.
>
> O Causify faz essa conta sozinho, com os feriados, e avisa antes do vencimento.
>
> Teste grátis. Link na bio: causify.com.br
>
> #PrazosProcessuais #CPC #DiasÚteis #AdvocaciaDigital #GestãoJurídica #LegalTech #Advocacia #DireitoProcessualCivil #RotinaJurídica #DireitoBrasil

### Cobalto 1 — Publicou? A gente avisa.
**Vídeo:** `causify-bru-cobalto-publicou-instagram-1080x1350.mp4`

> Publicou? A gente avisa. 📰
>
> Todo dia, às 6h, o Causify lê o Diário de Justiça e traz as publicações dos seus processos, com
> o prazo calculado e aviso por e-mail. Sem entrar tribunal por tribunal.
>
> Teste grátis. Link na bio: causify.com.br
>
> #Publicações #Intimações #DiárioOficial #AdvocaciaDigital #GestãoJurídica #LegalTech #Advocacia #DireitoDigital #EscritórioDeAdvocacia #DireitoBrasil

### Cobalto 2 — Intimação lida antes do seu café
**Vídeo:** `causify-bru-cobalto-antes-do-cafe-instagram-1080x1350.mp4`

> Intimação lida antes do seu café. ☕
>
> Às 6h a publicação já está no Causify, vinculada ao processo e com o prazo calculado. Às 8h
> você só decide o que fazer com ela.
>
> Teste grátis. Link na bio: causify.com.br
>
> #Intimações #PrazosProcessuais #AdvocaciaDigital #Produtividade #LegalTech #Advocacia #RotinaJurídica #EscritórioDeAdvocacia #AdvogadoDeSucesso #DireitoBrasil

### Concreto 1 — Nenhum prazo na gaveta
**Vídeo:** `causify-bru-concreto-gaveta-instagram-1080x1350.mp4`

> Nenhum prazo na gaveta. 🚧
>
> Processo parado não avisa que está parado. No Causify cada processo tem próximo passo,
> responsável e data: kanban de tarefas, agenda de audiências e portal do cliente.
>
> Teste grátis. Link na bio: causify.com.br
>
> #GestãoDeEscritório #OrganizaçãoJurídica #AdvocaciaDigital #GestãoJurídica #LegalTech #Advocacia #EscritórioDeAdvocacia #Produtividade #RotinaJurídica #DireitoBrasil

### Concreto 2 — Cada tarefa tem dono
**Vídeo:** `causify-bru-concreto-tarefa-tem-dono-instagram-1080x1350.mp4`

> Cada tarefa tem dono. ✅
>
> "De quem é essa tarefa?" não devia ser pergunta. No kanban do Causify cada card tem
> responsável e anda de Pendente para Concluída à vista da equipe, e os prazos da agenda entram
> no mesmo quadro.
>
> Teste grátis. Link na bio: causify.com.br
>
> #GestãoDeEquipe #Kanban #AdvocaciaDigital #Produtividade #LegalTech #Advocacia #EscritórioDeAdvocacia #GestãoJurídica #RotinaJurídica #DireitoBrasil

### Madrugada 1 — Vence às 23h59
**Vídeo:** `causify-bru-madrugada-23h59-instagram-1080x1350.mp4`

> Vence às 23h59. 🌙
>
> Lei 11.419/2006, art. 3º: a petição eletrônica é tempestiva até as 24h do último dia. Melhor não
> descobrir isso às 23h41, com o sistema do tribunal fora do ar.
>
> O Causify avisa os prazos antes do último dia. Teste grátis. Link na bio: causify.com.br
>
> #PrazoFatal #ProcessoEletrônico #PJe #AdvocaciaDigital #GestãoJurídica #LegalTech #Advocacia #VidaDeAdvogado #RotinaJurídica #DireitoBrasil

### Madrugada 2 — Sem susto às 23h41
**Vídeo:** `causify-bru-madrugada-seu-dia-instagram-1080x1350.mp4`

> Sem susto às 23h41. 🕛
>
> À meia-noite chega o "Seu dia": um e-mail com os prazos, as tarefas e as audiências que vêm
> pela frente. Você começa o dia sabendo o que vence hoje.
>
> Teste grátis. Link na bio: causify.com.br
>
> #PrazoFatal #GestãoDeEquipe #AdvocaciaDigital #GestãoJurídica #LegalTech #Advocacia #VidaDeAdvogado #EscritórioDeAdvocacia #RotinaJurídica #DireitoBrasil

## Como regerar / editar

1. Editar `posts.html`. Aberto direto no navegador (sem query string), ele mostra a galeria com
   os 16 vídeos em loop; `posts.html?p=c2&f=sq` mostra um post só, no tamanho final.
2. Rodar, da raiz do repositório (precisa de `ffmpeg` no PATH e das dependências de
   `tests/frontend` instaladas):

```bash
node marketing/posts-brutalista/render.mjs
```

   Para regerar só alguns posts: `node marketing/posts-brutalista/render.mjs a1 d2`. Para conferir
   quadros soltos sem gerar vídeo: `--previa 2,4.7` (salva PNGs em `out/previa`).

As animações são CSS comum, mas ficam pausadas: o `render.mjs` fixa o tempo com `window.seek(t)`
e captura quadro a quadro, então o vídeo sai liso em qualquer máquina. As fontes Archivo e
Archivo Black vêm de `src/LegalManager.API/wwwroot/fonts`; a JetBrains Mono vem do Google Fonts.
