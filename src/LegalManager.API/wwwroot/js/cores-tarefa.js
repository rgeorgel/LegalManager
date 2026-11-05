// Cores que o usuário escolhe para agrupar tarefas e prazos. Aparecem na listagem (lista,
// blocos, kanban, widget "Minhas Tarefas") e na agenda. Gravadas como "#rrggbb"
// (backend: Services/CoresTarefa.cs); tons médios, legíveis com texto branco (chips da agenda)
// nos temas claro e escuro.
import { esc } from './utils.js';

export const CORES_TAREFA = [
  { valor: '#dc2626', nome: 'Vermelho' },
  { valor: '#ea580c', nome: 'Laranja' },
  { valor: '#ca8a04', nome: 'Amarelo' },
  { valor: '#16a34a', nome: 'Verde' },
  { valor: '#0891b2', nome: 'Turquesa' },
  { valor: '#2563eb', nome: 'Azul' },
  { valor: '#7c3aed', nome: 'Roxo' },
  { valor: '#db2777', nome: 'Rosa' },
];

/** Valor do filtro para "tarefas sem cor" (mesmo do backend). */
export const SEM_COR = 'sem';

const HEX = /^#[0-9a-f]{6}$/i;

/** Cor válida ("#rrggbb", minúsculas) ou null. */
export function corValida(cor) {
  return typeof cor === 'string' && HEX.test(cor) ? cor.toLowerCase() : null;
}

export function nomeCor(cor) {
  const c = corValida(cor);
  return CORES_TAREFA.find(x => x.valor === c)?.nome ?? c ?? 'Sem cor';
}

/** Bolinha da cor da tarefa (vazio quando não tem cor). */
export function marcaCor(cor) {
  const c = corValida(cor);
  return c ? `<span class="cor-tarefa" style="--cor-tarefa:${c}" title="Cor: ${esc(nomeCor(c))}" aria-label="Cor: ${esc(nomeCor(c))}"></span>` : '';
}

/** Opções para o filtro de seleção múltipla (js/multiselect.js). */
export const opcoesFiltroCor = () => [
  ...CORES_TAREFA.map(c => ({ value: c.valor, label: c.nome })),
  { value: SEM_COR, label: 'Sem cor' },
];

/** Bolinhas coloridas antes do rótulo de cada opção do filtro de cor. */
export function estilizarFiltroCor(root) {
  if (!root.id || document.getElementById(`${root.id}-cores`)) return;
  const estilo = document.createElement('style');
  estilo.id = `${root.id}-cores`;
  estilo.textContent = CORES_TAREFA.map(c =>
    `#${root.id} .multiselect-option input[value="${c.valor}"] + span::before { background:${c.valor}; }`).join('\n')
    + `\n#${root.id} .multiselect-option input + span::before { content:''; display:inline-block; width:10px; height:10px; border-radius:50%; margin-right:6px; vertical-align:-1px; border:1px solid var(--color-border); }`
    + `\n#${root.id} .multiselect-option input[value="${SEM_COR}"] + span::before { background:transparent; }`;
  document.head.appendChild(estilo);
}
