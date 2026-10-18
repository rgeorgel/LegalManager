import { esc } from './utils.js';
import { getProcessos } from './processos.js';
import { createSearchPicker } from './search-picker.js';

// Encerrado fica de fora: não faz sentido vincular tarefa, documento ou lançamento novo a ele.
const STATUS_SELECIONAVEIS = ['Ativo', 'Suspenso', 'Arquivado'];

/** Texto do processo no campo: número CNJ e, quando houver, o cliente. */
export function processoLabel(numeroCNJ, nomeCliente) {
  return numeroCNJ + (nomeCliente ? ` — ${nomeCliente}` : '');
}

/**
 * Campo de busca de processo (autocomplete no servidor), no lugar de um <select> com os
 * primeiros 200 processos. Mesmo comportamento do picker de contatos (js/contato-picker.js).
 *
 *   const picker = createProcessoPicker(document.getElementById('fProcessoPicker'), {
 *     value: { id, label },                   // seleção inicial (opcional) — veja processoLabel()
 *     onChange: (processo) => { ... },        // { id, label, ... } ou null
 *   });
 *   picker.getValue(); picker.setValue({ id, label }); picker.setValue(null);
 *
 * Busca por número CNJ, tribunal, comarca, tipo de ação ou nome das partes; lista só processos
 * não encerrados — ou todos, com incluirEncerrados (filtros de listagem). Vínculo opcional: apagar
 * o texto e sair do campo remove o processo.
 */
export function createProcessoPicker(root, {
  valueClass = '', value = null, placeholder, incluirEncerrados = false, onChange,
} = {}) {
  return createSearchPicker(root, {
    valueClass, value, onChange,
    permitirLimpar: true,
    placeholder: placeholder || 'Nenhum processo — digite nº CNJ, cliente ou comarca...',
    buscar: async (termo) =>
      (await getProcessos({ busca: termo, status: incluirEncerrados ? [] : STATUS_SELECIONAVEIS, pageSize: 20 }))?.items,
    opcao: (p) => ({
      id: p.id,
      label: processoLabel(p.numeroCNJ, p.nomeCliente),
      detalhe: [p.tribunal, p.comarca, p.status !== 'Ativo' ? p.status : null]
        .filter(Boolean).map(esc).join(' · '),
    }),
    vazio: (termo) => `Nenhum processo${incluirEncerrados ? '' : ' em andamento'} encontrado${termo ? ` para "${esc(termo)}"` : ''}.`,
    dica: 'Digite para buscar entre todos os processos.',
    erro: 'Erro ao buscar processos. Tente novamente.',
  });
}
