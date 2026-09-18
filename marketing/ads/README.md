# Causify — Criativos para LinkedIn Ads

Imagens geradas em **1200×627** (formato Single Image Ad do feed do LinkedIn),
usando screenshots reais do produto e a identidade visual da LP
(navy `#0f172a` + dourado `#f59e0b` + wordmark Georgia itálico).

## Arquivos

| Arquivo | Tema | Screenshot usado |
|---|---|---|
| `out/causify-ad-honorarios-1200x627.png` | Honorários & Contratos | Tela Honorários Contratos |
| `out/causify-ad-tarefas-1200x627.png` | Tarefas & Prazos (Kanban) | Tela Tarefas → Kanban |
| `out/causify-ad-calculadora-1200x627.png` | Calculadora de Honorários | Calculadora (Tabela OAB/SP 2026) |

## Textos sugeridos para a campanha

### Ad 1 — Honorários & Contratos
- **Texto introdutório:** Planilha de honorários não avisa quando o cliente atrasa. O Causify avisa. Contratos, parcelas, recebimentos e meta mensal em um painel só — atualizado sozinho.
- **Headline:** Quanto o seu escritório tem a receber hoje?
- **Descrição:** Gestão de honorários para advogados. Teste grátis.
- **CTA:** Cadastre-se

### Ad 2 — Tarefas & Prazos
- **Texto introdutório:** Prazo perdido não é falta de competência — é falta de sistema. Kanban, responsável, prazo vinculado ao processo e alerta automático de tarefa atrasada.
- **Headline:** O prazo não depende mais da sua memória.
- **Descrição:** Tarefas, prazos e agenda em um só lugar.
- **CTA:** Saiba mais

### Ad 3 — Calculadora de Honorários
- **Texto introdutório:** Tabela OAB/SP 2026 + complexidade, risco de recebimento, urgência, capacidade do cliente e probabilidade de êxito. A proposta sai pronta, com cronograma de pagamento.
- **Headline:** Pare de chutar o valor dos seus honorários.
- **Descrição:** Calculadora com Tabela OAB/SP 2026. Grátis para testar.
- **CTA:** Experimente

> Sugestão de teste A/B: rodar os 3 no mesmo grupo de anúncios com o mesmo
> público (advogados / sócios de escritório no Brasil) e deixar o LinkedIn
> otimizar. O ad 3 tende a ser o melhor gancho de topo de funil; o ad 1, o de
> maior intenção.

## Como regerar / editar

O layout fica em `ads.html` (um `<div class="ad">` por criativo, 1200×627 fixo).
Os recortes dos screenshots são controlados por `width` / `left` / `top` da `img`
dentro de `.shot` no bloco CSS de cada ad.

```bash
node marketing/ads/render.mjs
cd marketing/ads/out && for f in *.png; do sips -Z 1200 "$f" --out "$f"; done
```

O render usa o Chromium do Playwright já instalado em `tests/frontend/node_modules`
(captura em 2× e o `sips` reduz para 1200 px, garantindo texto nítido).

Screenshots de origem ficam em `src/`.
