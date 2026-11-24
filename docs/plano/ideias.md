# Ideias para implementar

Backlog de ideias levantadas na comparação com concorrentes (Astrea, ADVBOX, Projuris, EasyJur, Themis,
Clio) em 2026-11-23. Marque `[x]` quando entrar em produção e ligue ao documento de detalhamento
(`docs/features/...`) quando existir.

Esforço: **P** pequeno · **M** médio · **G** grande.

---

- [ ] **Jurisprudência verificada** — M/G · prioridade · *fases 0–3 feitas em 2026-11-23/24 (recurso em teste + página pública); faltam súmulas/STF/TST*
  Hoje a jurisprudência do gerador de peças é escrita pela IA de memória
  (`IAService.BuscarJurisprudenciaAsync`), sem fonte — risco de precedente inventado, que tribunais já
  punem com multa (TJSC, TRT-18, TSE; o STJ admite multa por má-fé). A IA passa a citar só decisões
  encontradas numa base real (Portal de Dados Abertos do STJ, gratuito e com uso comercial liberado; depois
  STF e outras fontes), sempre com link. Inclui um **verificador de citações** (REsp, AgInt, Súmula...)
  para qualquer peça, gerada ou enviada pelo advogado.
  Detalhamento: [docs/features/jurisprudencia-verificada.md](../features/jurisprudencia-verificada.md).

- [ ] **Casos extrajudiciais, administrativos e consultivos** — M
  Hoje todo processo exige número CNJ. Tipo de caso sem CNJ (requerimento no INSS, inventário em
  cartório, PROCON, parecer, revisão de contrato, assessoria mensal) com prazos, tarefas, documentos,
  honorários e portal do cliente iguais aos do processo. Abre o previdenciário administrativo e o
  consultivo. Referência: Projuris (judicial e extrajudicial).

- [ ] **Preparar documentos para protocolo** — P/M
  Juntar vários arquivos, aplicar OCR (PDF pesquisável), comprimir e **dividir em blocos no limite do
  tribunal** (PJe: 10 MB por PDF; TJMG: 1,5 MB), numerados e com nome sem acento. Cuidado com a licença
  da biblioteca de PDF (evitar iText, AGPL).

- [ ] **Calculadora de custas e preparo** — P por tribunal, manutenção contínua
  Custas iniciais, preparo recursal e porte de remessa por tribunal; o valor vira despesa do processo.
  Começar por TJSP, TJMG, TJRJ, TJPR e TJRS.

- [ ] **Correspondentes jurídicos** — M
  Delegar diligência ou audiência a correspondente externo (sem conta no Causify) por link seguro com
  tarefa, prazo e documentos; ele devolve comprovante ou ata pelo link; o valor vira despesa do processo
  (reembolsável). Referências: EasyJur, Projuris; marketplaces Jurídico Certo e Juris Correspondente
  (integração futura).

- [ ] **Assistente de IA no WhatsApp do advogado** — M/G · diferencial
  O assistente que já existe (com ações confirmadas antes de gravar) pelo WhatsApp do próprio advogado:
  "quais meus prazos hoje?", "o que saiu no processo da Maria?", áudio "cria tarefa de ligar para o cliente
  amanhã às 10h". Exige a API oficial do WhatsApp Business (custo por conversa).

- [ ] **Pesquisa de satisfação (NPS) e indicação pelo portal** — P
  Ao fim de cada fase do caso (sentença, acordo, encerramento), nota de 0 a 10 no portal ou por e-mail;
  quem dá 9–10 recebe convite para indicar alguém, e a indicação entra como contato novo. Mostra
  insatisfação antes de perder o cliente e cria um canal de indicação.
