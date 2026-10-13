// Centraliza o empilhamento vertical dos menus flutuantes da lateral direita
// (assistente, tarefas, tutorial, perguntas). Cada um deles aparece ou não dependendo do
// plano/estado do usuário — e o de perguntas só decide depois de uma busca
// assíncrona — então a composição exata do grupo só é conhecida em tempo de
// execução. Em vez de cada widget aplicar um deslocamento fixo one-way (o que
// empurra o grupo inteiro pra baixo conforme mais widgets aparecem, ficando
// baixo demais na tela com os três presentes), este módulo mede quais ids
// estão no DOM e centraliza o conjunto como um bloco em torno do meio
// vertical da viewport, aplicando o resultado via custom property
// `--widget-offset` (lido pelo `transform` de cada widget).
// As alturas são medidas (o do assistente, com o mascote, é mais alto que os outros) e
// remedidas quando algum muda de tamanho (ex.: o badge de perguntas aparece depois).
const ORDER = ['assistenteWidget', 'tarefasWidget', 'tourWidget', 'perguntasWidget'];
const GAP = 14; // px entre o fim de um widget e o começo do próximo
const ALTURA_PADRAO = 116; // se ainda não dá para medir (ex.: elemento escondido)
const GAP_MOBILE = 10;

const observador = typeof ResizeObserver === 'function' ? new ResizeObserver(() => posicionar()) : null;

export function layoutWidgetStack() {
  posicionar();
  // Observar o mesmo elemento de novo não duplica nada; o callback só mexe em posição, não em tamanho.
  ORDER.forEach(id => { const el = document.getElementById(id); if (el) observador?.observe(el); });
}

function posicionar() {
  const present = ORDER.map(id => document.getElementById(id)).filter(Boolean);
  const alturas = present.map(el => el.offsetHeight || ALTURA_PADRAO);
  const total = alturas.reduce((s, h) => s + h, 0) + GAP * Math.max(0, present.length - 1);

  let topo = -total / 2;
  present.forEach((el, i) => {
    el.style.setProperty('--widget-offset', `${topo + alturas[i] / 2}px`);
    topo += alturas[i] + GAP;
  });

  // Respiro à direita do conteúdo (.main-content, ver styles.css) do tamanho do widget mais largo
  // (a aba do assistente com o mascote), para os botões laterais não cobrirem a tela.
  const largura = Math.max(0, ...present.map(el => el.offsetWidth));
  document.body.classList.toggle('com-widgets-laterais', present.length > 0);
  document.body.style.setProperty('--widgets-laterais-largura', `${largura}px`);

  // Celular: sem noção de "centro" na tela pequena, o grupo se empilha a partir do rodapé, na
  // mesma ordem de cima pra baixo (assistente no topo), cada um acima da altura real dos de baixo
  // (`--widget-bottom-mobile`, lido no @media de cada widget). Alturas medidas, não um "andar"
  // fixo: a aba do assistente e a de perguntas (com badge) são mais altas que as outras.
  let base = 0;
  for (let i = present.length - 1; i >= 0; i--) {
    present[i].style.setProperty('--widget-bottom-mobile', `${base}px`);
    base += alturas[i] + GAP_MOBILE;
  }
}
