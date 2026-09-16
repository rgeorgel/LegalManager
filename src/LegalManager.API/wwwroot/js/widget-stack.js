// Centraliza o empilhamento vertical dos menus flutuantes da lateral direita
// (tarefas, tutorial, perguntas). Cada um deles aparece ou não dependendo do
// plano/estado do usuário — e o de perguntas só decide depois de uma busca
// assíncrona — então a composição exata do grupo só é conhecida em tempo de
// execução. Em vez de cada widget aplicar um deslocamento fixo one-way (o que
// empurra o grupo inteiro pra baixo conforme mais widgets aparecem, ficando
// baixo demais na tela com os três presentes), este módulo mede quais ids
// estão no DOM e centraliza o conjunto como um bloco em torno do meio
// vertical da viewport, aplicando o resultado via custom property
// `--widget-offset` (lido pelo `transform` de cada widget).
const ORDER = ['tarefasWidget', 'tourWidget', 'perguntasWidget'];
const SPACING = 130; // px entre o centro de um widget e o do próximo (~altura de cada botão)

export function layoutWidgetStack() {
  const present = ORDER.map(id => document.getElementById(id)).filter(Boolean);
  const n = present.length;

  present.forEach((el, i) => {
    const offset = (i - (n - 1) / 2) * SPACING;
    el.style.setProperty('--widget-offset', `${offset}px`);
  });
}
