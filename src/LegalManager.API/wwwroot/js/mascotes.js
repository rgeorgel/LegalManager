// Mascotes do Assistente de IA: o desenho que aparece no botão lateral, no cabeçalho do painel e
// no convite do plano Plus. Cada usuário escolhe o seu em /pages/assistente.html (salvo no banco,
// PUT /api/assistente/mascote, e devolvido no login em usuario.assistenteMascote); sem escolha,
// vale MASCOTE_PADRAO. Os ids são gravados no banco: não renomeie — o backend valida contra a
// mesma lista (AssistenteController.Mascotes).
// SVGs desenhados à mão em viewBox 64x64. O "Traço" usa currentColor (cor do texto ao redor).
// Rascunhos e comparação lado a lado: docs/mascote/corujas-alternativas.html.

export const MASCOTE_PADRAO = 'raposa';

const N = '#1A1A2E', G = '#C9922E', GD = '#8A5A12', C = '#F3E3C3';

// Pálpebras: um retângulo arredondado da cor do rosto sobre cada olho; o CSS do assistente
// (.asx-palpebras) as fecha por um instante de tempos em tempos (piscar).
const palpebras = (cor, olhos, r = 8) => `<g class="asx-palpebras" fill="${cor}">${
  olhos.map(([x, y]) => `<rect x="${x - r}" y="${y - r}" width="${2 * r}" height="${2 * r}" rx="${r}"/>`).join('')}</g>`;

const olhosPadrao = (lx = 24.5, rx = 39.5, y = 31, r = 7, anel = G, pupila = 3.2) => `
  <circle cx="${lx}" cy="${y}" r="${r}" fill="#fff" stroke="${anel}" stroke-width="2"/>
  <circle cx="${rx}" cy="${y}" r="${r}" fill="#fff" stroke="${anel}" stroke-width="2"/>
  <circle cx="${lx + 1}" cy="${y + .5}" r="${pupila}" fill="${N}"/>
  <circle cx="${rx - 1}" cy="${y + .5}" r="${pupila}" fill="${N}"/>
  <circle cx="${lx + 2}" cy="${y - .7}" r="1" fill="#fff"/>
  <circle cx="${rx}" cy="${y - .7}" r="1" fill="#fff"/>`;

const CABECA_ATUAL = 'M10 14 L20 24 L44 24 L54 14 L52 34 C52 50 44 58 32 58 C20 58 12 50 12 34 Z';
const ROSTO_ATUAL = 'M18 30 C18 22 24 20 32 26 C40 20 46 22 46 30 C46 40 40 46 32 46 C24 46 18 40 18 30 Z';
const svg = (corpo) => `<svg viewBox="0 0 64 64" width="100%" height="100%" focusable="false">${corpo}</svg>`;

export const MASCOTES = [
  {
    id: 'coruja', nome: 'Coruja',
    desc: 'O mascote da Causify, sábia e sempre atenta.',
    svg: svg(`<path d="${CABECA_ATUAL}" fill="${N}"/><path d="${ROSTO_ATUAL}" fill="${C}"/>${olhosPadrao()}
      ${palpebras(N, [[24.5, 31], [39.5, 31]])}<path d="M29 38 L35 38 L32 43 Z" fill="${G}"/>`),
  },
  {
    id: 'coruja-classica', nome: 'Coruja arredondada',
    desc: 'A coruja em versão mais fofinha, de contorno macio.',
    svg: svg(`<path d="M14 18 Q12 7 21 12 Q26 9 32 10 Q38 9 43 12 Q52 7 50 18 Q56 26 55 36 Q54 57 32 57 Q10 57 9 36 Q8 26 14 18 Z" fill="${N}"/>
      <circle cx="24" cy="32" r="11" fill="${C}"/><circle cx="40" cy="32" r="11" fill="${C}"/>
      ${olhosPadrao(24, 40, 32, 7.5, G, 3.6)}${palpebras(N, [[24, 32], [40, 32]], 8.5)}
      <path d="M30 40 L34 40 L32 45 Z" fill="${G}"/>
      <path d="M26 51 q3 2 6 0 q3 2 6 0" stroke="${C}" stroke-width="1.5" fill="none" opacity=".5" stroke-linecap="round"/>`),
  },
  {
    id: 'coruja-doutora', nome: 'Coruja doutora',
    desc: 'De óculos e gravata-borboleta, pronta para a audiência.',
    svg: svg(`<path d="${CABECA_ATUAL}" fill="${N}"/><path d="${ROSTO_ATUAL}" fill="${C}"/>${olhosPadrao(24.5, 39.5, 31, 6, '#fff', 3)}
      ${palpebras(C, [[24.5, 31], [39.5, 31]], 6.5)}
      <circle cx="24.5" cy="31" r="8.5" fill="#fff" fill-opacity=".15" stroke="${G}" stroke-width="2.5"/>
      <circle cx="39.5" cy="31" r="8.5" fill="#fff" fill-opacity=".15" stroke="${G}" stroke-width="2.5"/>
      <path d="M30 29.5 Q32 27.5 34 29.5" stroke="${G}" stroke-width="2" fill="none"/>
      <path d="M29.5 38.5 L34.5 38.5 L32 43 Z" fill="${G}"/>
      <path d="M25 50 L32 53.5 L25 57 Z M39 50 L32 53.5 L39 57 Z" fill="${G}"/><circle cx="32" cy="53.5" r="2" fill="${GD}"/>`),
  },
  {
    id: 'coruja-capelo', nome: 'Coruja formanda',
    desc: 'Com capelo e borla dourada: estudou tudo sobre o seu escritório.',
    svg: svg(`<path d="M14 28 Q14 16 32 16 Q50 16 50 28 L50 38 Q50 58 32 58 Q14 58 14 38 Z" fill="${N}"/>
      <path d="${ROSTO_ATUAL}" fill="${C}" transform="translate(0 2)"/>
      <g transform="translate(0 2)">${olhosPadrao()}${palpebras(N, [[24.5, 31], [39.5, 31]])}<path d="M29 38 L35 38 L32 43 Z" fill="${G}"/></g>
      <rect x="22" y="14" width="20" height="7" rx="1.5" fill="#0d0d18"/>
      <path d="M8 13 L32 4 L56 13 L32 22 Z" fill="#0d0d18"/>
      <path d="M32 13 L48 16 L48 25" stroke="${G}" stroke-width="1.8" fill="none" stroke-linecap="round"/>
      <path d="M46 25 L50 25 L49 30 L47 30 Z" fill="${G}"/><circle cx="32" cy="13" r="1.6" fill="${G}"/>`),
  },
  {
    id: 'coruja-geometrica', nome: 'Coruja geométrica',
    desc: 'Traços retos e facetados, com visual moderno.',
    svg: svg(`<path d="M10 9 L24 20 L40 20 L54 9 L54 40 L32 59 L10 40 Z" fill="${N}"/>
      <path d="M10 40 L32 59 L32 50 Z" fill="#11111f"/><path d="M54 40 L32 59 L32 50 Z" fill="#26263f"/>
      <path d="M15 25 L32 29 L32 47 L17 40 Z" fill="${C}"/><path d="M49 25 L32 29 L32 47 L47 40 Z" fill="#E8D3A8"/>
      <circle cx="24" cy="33" r="6" fill="#fff" stroke="${G}" stroke-width="2"/><circle cx="40" cy="33" r="6" fill="#fff" stroke="${G}" stroke-width="2"/>
      <rect x="22.5" y="31.5" width="4" height="4" fill="${N}" transform="rotate(45 24.5 33.5)"/>
      <rect x="37.5" y="31.5" width="4" height="4" fill="${N}" transform="rotate(45 39.5 33.5)"/>
      ${palpebras(C, [[24, 33], [40, 33]], 7)}
      <path d="M29 38 L35 38 L32 44 Z" fill="${G}"/>`),
  },
  {
    id: 'coruja-piscadinha', nome: 'Coruja piscadinha',
    desc: 'Pisca um olho e fica corada: cúmplice nas suas tarefas.',
    svg: svg(`<path d="${CABECA_ATUAL}" fill="${N}"/><path d="${ROSTO_ATUAL}" fill="${C}"/>
      <circle cx="24.5" cy="31" r="7" fill="#fff" stroke="${G}" stroke-width="2"/>
      <circle cx="25.5" cy="31.5" r="3.4" fill="${N}"/><circle cx="26.7" cy="30.2" r="1.1" fill="#fff"/>
      ${palpebras(N, [[24.5, 31]])}
      <path d="M33.5 32 Q39.5 26 45.5 32" stroke="${N}" stroke-width="2.6" fill="none" stroke-linecap="round"/>
      <path d="M18 22.5 Q23 19 29 22" stroke="${N}" stroke-width="2" fill="none" stroke-linecap="round"/>
      <ellipse cx="42" cy="39" rx="3.5" ry="2" fill="#F2A3A3" opacity=".8"/>
      <path d="M29 38 L35 38 L32 43 Z" fill="${G}"/>`),
  },
  {
    id: 'coruja-kawaii', nome: 'Corujinha',
    desc: 'Filhote de olhos enormes e bochechas rosadas.',
    svg: svg(`<path d="M13 20 L17 7 L25 15 Z M51 20 L47 7 L39 15 Z" fill="${N}"/>
      <circle cx="32" cy="36" r="24" fill="${N}"/>
      <ellipse cx="32" cy="37" rx="20" ry="16" fill="${C}"/>
      <circle cx="23" cy="34" r="9" fill="#fff"/><circle cx="41" cy="34" r="9" fill="#fff"/>
      <circle cx="24" cy="35" r="6.2" fill="${N}"/><circle cx="40" cy="35" r="6.2" fill="${N}"/>
      <circle cx="26.2" cy="32.4" r="2.3" fill="#fff"/><circle cx="42.2" cy="32.4" r="2.3" fill="#fff"/>
      <circle cx="22.4" cy="37.4" r="1" fill="#fff"/><circle cx="38.4" cy="37.4" r="1" fill="#fff"/>
      ${palpebras(C, [[23, 34], [41, 34]], 9.5)}
      <ellipse cx="15.5" cy="44" rx="3.5" ry="2" fill="#F4A6A6" opacity=".75"/><ellipse cx="48.5" cy="44" rx="3.5" ry="2" fill="#F4A6A6" opacity=".75"/>
      <path d="M30.5 42.5 L33.5 42.5 L32 45 Z" fill="${G}"/>`),
  },
  {
    id: 'coruja-dourada', nome: 'Coruja dourada',
    desc: 'Penas douradas que brilham ainda mais nos temas escuros.',
    svg: svg(`<path d="${CABECA_ATUAL}" fill="${G}"/><path d="${ROSTO_ATUAL}" fill="#FFF4DD"/>
      <path d="M10 14 L20 24 L16 25 Z M54 14 L44 24 L48 25 Z" fill="${GD}"/>
      ${olhosPadrao(24.5, 39.5, 31, 7, N, 3.2)}${palpebras(G, [[24.5, 31], [39.5, 31]])}
      <path d="M29 38 L35 38 L32 43 Z" fill="${N}"/>
      <path d="M24 50 l2.5 2.5 l2.5 -2.5 M30 52 l2 2 l2 -2 M35 50 l2.5 2.5 l2.5 -2.5" stroke="${GD}" stroke-width="1.6" fill="none" stroke-linecap="round" stroke-linejoin="round"/>`),
  },
  {
    id: 'coruja-espiando', nome: 'Coruja espiã',
    desc: 'Agarrada à borda, espiando a tela para não perder nada.',
    svg: svg(`<path d="M14 22 Q12 9 21 14 Q32 8 43 14 Q52 9 50 22 Q54 30 52 42 Q50 53 32 53 Q14 53 12 42 Q10 30 14 22 Z" fill="${N}"/>
      <circle cx="24" cy="32" r="10" fill="${C}"/><circle cx="40" cy="32" r="10" fill="${C}"/>
      <circle cx="24" cy="32" r="7" fill="#fff" stroke="${G}" stroke-width="2"/><circle cx="40" cy="32" r="7" fill="#fff" stroke="${G}" stroke-width="2"/>
      <circle cx="22.2" cy="34" r="3.3" fill="${N}"/><circle cx="38.2" cy="34" r="3.3" fill="${N}"/>
      <circle cx="23.2" cy="32.8" r="1" fill="#fff"/><circle cx="39.2" cy="32.8" r="1" fill="#fff"/>
      ${palpebras(N, [[24, 32], [40, 32]])}
      <path d="M29.5 40 L34.5 40 L32 44.5 Z" fill="${G}"/>
      <path d="M5 54 Q5 48 12 48 Q19 48 19 54 Z M45 54 Q45 48 52 48 Q59 48 59 54 Z" fill="${N}"/>
      <path d="M8.5 54 v-2.5 M12 54 v-3 M15.5 54 v-2.5 M48.5 54 v-2.5 M52 54 v-3 M55.5 54 v-2.5" stroke="${C}" stroke-width="1.3" stroke-linecap="round" opacity=".7"/>`),
  },
  {
    id: 'coruja-traco', nome: 'Coruja traço',
    desc: 'Só o contorno, discreta e elegante.',
    svg: svg(`<g fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
      <path d="${CABECA_ATUAL}"/><path d="${ROSTO_ATUAL}" stroke-width="2"/>
      <circle cx="24.5" cy="31" r="6"/><circle cx="39.5" cy="31" r="6"/><path d="M29.5 39 L32 43 L34.5 39"/></g>
      <circle cx="25" cy="31.5" r="2.4" fill="currentColor"/><circle cx="39" cy="31.5" r="2.4" fill="currentColor"/>
      <circle cx="24.5" cy="31" r="6.6" fill="${G}" opacity=".25"/><circle cx="39.5" cy="31" r="6.6" fill="${G}" opacity=".25"/>`),
  },
  {
    id: 'coruja-headset', nome: 'Coruja atendente',
    desc: 'De fone e microfone, sempre pronta para atender.',
    svg: svg(`<path d="${CABECA_ATUAL}" fill="${N}"/><path d="${ROSTO_ATUAL}" fill="${C}"/>${olhosPadrao()}
      ${palpebras(N, [[24.5, 31], [39.5, 31]])}<path d="M29 38 L35 38 L32 43 Z" fill="${G}"/>
      <path d="M9 30 Q9 7 32 7 Q55 7 55 30" stroke="#6b7280" stroke-width="3" fill="none" stroke-linecap="round"/>
      <rect x="4.5" y="25" width="9" height="14" rx="3.5" fill="${G}"/><rect x="50.5" y="25" width="9" height="14" rx="3.5" fill="${G}"/>
      <rect x="6.5" y="28" width="5" height="8" rx="2" fill="${GD}"/><rect x="52.5" y="28" width="5" height="8" rx="2" fill="${GD}"/>
      <path d="M9 39 Q10 50 22 50" stroke="#6b7280" stroke-width="2.4" fill="none" stroke-linecap="round"/>
      <rect x="21" y="47.5" width="7" height="5" rx="2.5" fill="#fff"/>`),
  },
  {
    id: 'gato', nome: 'Gato',
    desc: 'Curioso e atento, de olho nos seus prazos.',
    svg: svg(`<path d="M12 22 L14 6 L26 16 Q32 14 38 16 L50 6 L52 22 Q56 30 54 40 Q52 56 32 56 Q12 56 10 40 Q8 30 12 22 Z" fill="${N}"/>
      <path d="M16 18 L17 10 L23 15 Z M48 18 L47 10 L41 15 Z" fill="#F2A3A3"/>
      <ellipse cx="32" cy="44" rx="10" ry="7" fill="${C}"/>
      <ellipse cx="23" cy="32" rx="5.5" ry="5" fill="${G}"/><ellipse cx="41" cy="32" rx="5.5" ry="5" fill="${G}"/>
      <ellipse cx="23" cy="32" rx="1.4" ry="4" fill="${N}"/><ellipse cx="41" cy="32" rx="1.4" ry="4" fill="${N}"/>
      <circle cx="24.6" cy="30.4" r="1" fill="#fff"/><circle cx="42.6" cy="30.4" r="1" fill="#fff"/>
      ${palpebras(N, [[23, 32], [41, 32]], 6)}
      <path d="M30 40.5 L34 40.5 L32 43 Z" fill="#E88A8A"/>
      <path d="M32 43 q-2 3 -4.5 1.2 M32 43 q2 3 4.5 1.2" stroke="${N}" stroke-width="1.4" fill="none" stroke-linecap="round"/>
      <path d="M21 44 L9 42 M21 46.5 L9 48 M43 44 L55 42 M43 46.5 L55 48" stroke="${C}" stroke-width="1.2" stroke-linecap="round" opacity=".8"/>`),
  },
  {
    id: 'cachorro', nome: 'Cachorro',
    desc: 'Fiel escudeiro, sempre disposto a ajudar.',
    svg: svg(`<ellipse cx="32" cy="35" rx="18" ry="19" fill="${G}"/>
      <path d="M32 16 Q28 30 21 42 Q32 55 43 42 Q36 30 32 16 Z" fill="#FFF4DD"/>
      <path d="M16 19 Q3 21 5 40 Q7 49 14 45 Q16 32 19 23 Z M48 19 Q61 21 59 40 Q57 49 50 45 Q48 32 45 23 Z" fill="${GD}"/>
      <circle cx="24" cy="32" r="4" fill="${N}"/><circle cx="40" cy="32" r="4" fill="${N}"/>
      <circle cx="25.3" cy="30.7" r="1.3" fill="#fff"/><circle cx="41.3" cy="30.7" r="1.3" fill="#fff"/>
      ${palpebras(G, [[24, 32], [40, 32]], 4.6)}
      <path d="M20 26 Q24 24 27 26 M37 26 Q40 24 44 26" stroke="${GD}" stroke-width="1.8" fill="none" stroke-linecap="round"/>
      <path d="M29.5 48 Q32 55 34.5 48 Z" fill="#F28B8B"/>
      <ellipse cx="32" cy="42.5" rx="4.5" ry="3" fill="${N}"/>
      <path d="M32 45.5 q-3 3 -6 1 M32 45.5 q3 3 6 1" stroke="${N}" stroke-width="1.5" fill="none" stroke-linecap="round"/>`),
  },
  {
    id: 'raposa', nome: 'Raposa',
    desc: 'Esperta e ágil, encontra o que você precisa num instante.',
    svg: svg(`<path d="M8 9 L24 22 L40 22 L56 9 L53 33 L32 56 L11 33 Z" fill="#D9822B"/>
      <path d="M12 15 L21 22 L15 26 Z M52 15 L43 22 L49 26 Z" fill="${N}"/>
      <path d="M11 33 Q20 41 32 41 Q44 41 53 33 L32 56 Z" fill="#FFF4DD" stroke="#C26F1E" stroke-width="1.2" stroke-linejoin="round"/>
      <ellipse cx="24" cy="31" rx="3" ry="3.6" fill="${N}"/><ellipse cx="40" cy="31" rx="3" ry="3.6" fill="${N}"/>
      <circle cx="25" cy="29.8" r="1" fill="#fff"/><circle cx="41" cy="29.8" r="1" fill="#fff"/>
      ${palpebras('#D9822B', [[24, 31], [40, 31]], 4.2)}
      <circle cx="32" cy="53.5" r="2.8" fill="${N}"/>`),
  },
  {
    id: 'panda', nome: 'Panda',
    desc: 'Calmo e simpático, mesmo nos dias de prazo apertado.',
    svg: svg(`<circle cx="15" cy="17" r="7.5" fill="${N}"/><circle cx="49" cy="17" r="7.5" fill="${N}"/>
      <circle cx="32" cy="35" r="21" fill="#fff" stroke="#d8d4cc" stroke-width="1.5"/>
      <ellipse cx="23" cy="33" rx="6" ry="8" fill="${N}" transform="rotate(30 23 33)"/>
      <ellipse cx="41" cy="33" rx="6" ry="8" fill="${N}" transform="rotate(-30 41 33)"/>
      <circle cx="24" cy="32" r="2.9" fill="#fff"/><circle cx="40" cy="32" r="2.9" fill="#fff"/>
      <circle cx="24.4" cy="32.3" r="1.6" fill="${N}"/><circle cx="39.6" cy="32.3" r="1.6" fill="${N}"/>
      ${palpebras(N, [[24, 32], [40, 32]], 3.4)}
      <ellipse cx="32" cy="42" rx="3.5" ry="2.4" fill="${N}"/>
      <path d="M32 44 q-2 2.6 -4.5 1 M32 44 q2 2.6 4.5 1" stroke="${N}" stroke-width="1.4" fill="none" stroke-linecap="round"/>
      <ellipse cx="17" cy="43" rx="3" ry="1.8" fill="#F4A6A6" opacity=".6"/><ellipse cx="47" cy="43" rx="3" ry="1.8" fill="#F4A6A6" opacity=".6"/>`),
  },
  {
    id: 'coelho', nome: 'Coelho',
    desc: 'Rápido nas respostas, de orelhas sempre em pé.',
    svg: svg(`<ellipse cx="23" cy="15" rx="5.5" ry="13" fill="${N}" transform="rotate(-12 23 15)"/>
      <ellipse cx="41" cy="15" rx="5.5" ry="13" fill="${N}" transform="rotate(12 41 15)"/>
      <ellipse cx="23" cy="15" rx="2.6" ry="9" fill="#F2A3A3" transform="rotate(-12 23 15)"/>
      <ellipse cx="41" cy="15" rx="2.6" ry="9" fill="#F2A3A3" transform="rotate(12 41 15)"/>
      <circle cx="32" cy="41" r="17" fill="${N}"/>
      <ellipse cx="32" cy="46" rx="10" ry="8" fill="${C}"/>
      <circle cx="25" cy="37" r="4.5" fill="#fff"/><circle cx="39" cy="37" r="4.5" fill="#fff"/>
      <circle cx="25.6" cy="37.6" r="2.6" fill="${N}"/><circle cx="38.4" cy="37.6" r="2.6" fill="${N}"/>
      <circle cx="26.4" cy="36.4" r=".9" fill="#fff"/><circle cx="39.2" cy="36.4" r=".9" fill="#fff"/>
      ${palpebras(N, [[25, 37], [39, 37]], 5)}
      <path d="M30 43.5 L34 43.5 L32 46 Z" fill="#E88A8A"/>
      <rect x="30.4" y="47" width="3.2" height="3.6" rx=".8" fill="#fff" stroke="#d8d4cc" stroke-width=".6"/>
      <ellipse cx="21" cy="47" rx="2.6" ry="1.6" fill="#F4A6A6" opacity=".7"/><ellipse cx="43" cy="47" rx="2.6" ry="1.6" fill="#F4A6A6" opacity=".7"/>`),
  },
  {
    id: 'pinguim', nome: 'Pinguim',
    desc: 'Já nasceu de terno e gravata, o mais formal da turma.',
    svg: svg(`<ellipse cx="32" cy="35" rx="20" ry="22" fill="${N}"/>
      <path d="M17 30 C17 21 25 19 32 25 C39 19 47 21 47 30 C47 44 40 52 32 52 C24 52 17 44 17 30 Z" fill="#fff"/>
      <circle cx="25" cy="31" r="3" fill="${N}"/><circle cx="39" cy="31" r="3" fill="${N}"/>
      <circle cx="26" cy="30" r="1" fill="#fff"/><circle cx="40" cy="30" r="1" fill="#fff"/>
      ${palpebras('#fff', [[25, 31], [39, 31]], 3.6)}
      <path d="M27.5 37 L36.5 37 L32 42 Z" fill="${G}"/>
      <ellipse cx="20.5" cy="39" rx="2.6" ry="1.6" fill="#F4A6A6" opacity=".7"/><ellipse cx="43.5" cy="39" rx="2.6" ry="1.6" fill="#F4A6A6" opacity=".7"/>
      <path d="M25 46 L32 49.5 L25 53 Z M39 46 L32 49.5 L39 53 Z" fill="${G}"/><circle cx="32" cy="49.5" r="2" fill="${GD}"/>`),
  },
  {
    id: 'leao', nome: 'Leão',
    desc: 'Juba dourada e olhar sereno, símbolo de justiça.',
    svg: svg(`${Array.from({ length: 12 }, (_, k) => {
        const a = (k / 12) * Math.PI * 2;
        return `<circle cx="${(32 + Math.cos(a) * 19).toFixed(1)}" cy="${(34 + Math.sin(a) * 19).toFixed(1)}" r="8" fill="${GD}"/>`;
      }).join('')}
      <circle cx="32" cy="34" r="19" fill="${GD}"/>
      <circle cx="19" cy="21" r="4.5" fill="${G}"/><circle cx="45" cy="21" r="4.5" fill="${G}"/>
      <circle cx="32" cy="35" r="15" fill="${G}"/>
      <ellipse cx="32" cy="42" rx="8" ry="6" fill="#FFF4DD"/>
      <circle cx="26" cy="33" r="2.7" fill="${N}"/><circle cx="38" cy="33" r="2.7" fill="${N}"/>
      <circle cx="26.9" cy="32.1" r=".9" fill="#fff"/><circle cx="38.9" cy="32.1" r=".9" fill="#fff"/>
      ${palpebras(G, [[26, 33], [38, 33]], 3.2)}
      <path d="M29.5 38.5 L34.5 38.5 L32 41.5 Z" fill="${N}"/>
      <path d="M32 41.5 v2 M32 43.5 q-2 2 -4 .8 M32 43.5 q2 2 4 .8" stroke="${N}" stroke-width="1.3" fill="none" stroke-linecap="round"/>`),
  },
  {
    id: 'guaxinim', nome: 'Guaxinim',
    desc: 'Detetive mascarado, ótimo para achar processos e publicações.',
    svg: svg(`<path d="M10 18 L17 7 L26 16 Q32 14 38 16 L47 7 L54 18 Q58 30 54 41 Q48 55 32 55 Q16 55 10 41 Q6 30 10 18 Z" fill="#8A8F99"/>
      <path d="M14 14 L17 10 L21 15 Z M50 14 L47 10 L43 15 Z" fill="${N}"/>
      <path d="M9 31 Q20 24 32 30 Q44 24 55 31 Q51 41 41 39 Q35 37 32 39 Q29 37 23 39 Q13 41 9 31 Z" fill="${N}"/>
      <path d="M15 26 Q23 21 30 26 M49 26 Q41 21 34 26" stroke="#fff" stroke-width="2.4" fill="none" stroke-linecap="round"/>
      <ellipse cx="32" cy="46" rx="9" ry="7" fill="#fff"/>
      <circle cx="23" cy="32.5" r="3.6" fill="#fff"/><circle cx="41" cy="32.5" r="3.6" fill="#fff"/>
      <circle cx="23.4" cy="32.8" r="2" fill="${N}"/><circle cx="40.6" cy="32.8" r="2" fill="${N}"/>
      ${palpebras(N, [[23, 32.5], [41, 32.5]], 4)}
      <ellipse cx="32" cy="42.5" rx="3.2" ry="2.2" fill="${N}"/>
      <path d="M32 44.5 q-2 2.4 -4 1 M32 44.5 q2 2.4 4 1" stroke="${N}" stroke-width="1.3" fill="none" stroke-linecap="round"/>`),
  },
  {
    id: 'urso', nome: 'Urso',
    desc: 'Grandão e acolhedor: pode deixar com ele.',
    svg: svg(`<circle cx="16" cy="17" r="7.5" fill="#7A4E1D"/><circle cx="48" cy="17" r="7.5" fill="#7A4E1D"/>
      <circle cx="16" cy="17" r="3.8" fill="#E9B872"/><circle cx="48" cy="17" r="3.8" fill="#E9B872"/>
      <circle cx="32" cy="36" r="20" fill="#7A4E1D"/>
      <ellipse cx="32" cy="44" rx="10" ry="8" fill="#E9C690"/>
      <circle cx="24" cy="32" r="3" fill="${N}"/><circle cx="40" cy="32" r="3" fill="${N}"/>
      <circle cx="25" cy="31" r="1" fill="#fff"/><circle cx="41" cy="31" r="1" fill="#fff"/>
      ${palpebras('#7A4E1D', [[24, 32], [40, 32]], 3.6)}
      <ellipse cx="32" cy="40.5" rx="4" ry="2.8" fill="${N}"/>
      <path d="M32 43.3 v1.7 M32 45 q-2.4 2.4 -4.6 .8 M32 45 q2.4 2.4 4.6 .8" stroke="${N}" stroke-width="1.4" fill="none" stroke-linecap="round"/>`),
  },
  {
    id: 'coruja-gato', nome: 'Coruja-gato',
    desc: 'Metade coruja, metade gato, com bigodes e orelhas rosadas.',
    svg: svg(`<path d="${CABECA_ATUAL}" fill="${N}"/>
      <path d="M13.5 18 L19 23.5 L15 24.5 Z M50.5 18 L45 23.5 L49 24.5 Z" fill="#F2A3A3"/>
      <path d="${ROSTO_ATUAL}" fill="${C}"/>
      <circle cx="24.5" cy="31" r="7" fill="${G}" stroke="${GD}" stroke-width="1.5"/>
      <circle cx="39.5" cy="31" r="7" fill="${G}" stroke="${GD}" stroke-width="1.5"/>
      <ellipse cx="24.5" cy="31" rx="1.6" ry="5.2" fill="${N}"/><ellipse cx="39.5" cy="31" rx="1.6" ry="5.2" fill="${N}"/>
      <circle cx="26.5" cy="28.8" r="1.1" fill="#fff"/><circle cx="41.5" cy="28.8" r="1.1" fill="#fff"/>
      ${palpebras(N, [[24.5, 31], [39.5, 31]])}
      <path d="M29 38 L35 38 L32 43 Z" fill="${G}"/>
      <path d="M26 41 L13 39 M26 43 L13 45 M38 41 L51 39 M38 43 L51 45" stroke="${N}" stroke-width="1.2" stroke-linecap="round" opacity=".55"/>`),
  },
];

export function getMascote(id) {
  return MASCOTES.find(m => m.id === id) ?? MASCOTES.find(m => m.id === MASCOTE_PADRAO);
}
