import { esc } from './utils.js';
import { getContatos } from './contatos.js';
import { createSearchPicker } from './search-picker.js';

const TIPO_CONTATO = {
  Cliente: 'Cliente', ParteContraria: 'Parte contrária', Testemunha: 'Testemunha', Perito: 'Perito', Outro: 'Outro',
};

/**
 * Campo de busca de contato (autocomplete no servidor), no lugar de um <select> com todos os
 * contatos — que fica inviável com muitos cadastros e só trazia a primeira página.
 *
 *   const picker = createContatoPicker(document.getElementById('pContatoPicker'), {
 *     valueClass: 'parte-contato',            // classe do <input hidden> que guarda o id
 *     value: { id, label },                   // seleção inicial (opcional)
 *     onChange: (contato) => { ... },         // { id, label, ... } ou null
 *   });
 *   picker.getValue(); picker.getLabel(); picker.setValue({ id, label }); picker.focus();
 *
 * Busca por nome, CPF/CNPJ ou e-mail (mesmo filtro da tela de contatos), só contatos ativos.
 * Digitar não apaga a seleção: ao sair do campo sem escolher outro, o nome selecionado volta.
 */
export function createContatoPicker(root, { valueClass = '', value = null, placeholder, onChange } = {}) {
  return createSearchPicker(root, {
    valueClass, value, onChange,
    placeholder: placeholder || 'Digite nome, CPF/CNPJ ou e-mail...',
    buscar: async (termo) =>
      (await getContatos({ busca: termo, ativo: true, sortBy: 'nome', sortDir: 'asc', pageSize: 20 }))?.items,
    opcao: (c) => ({
      id: c.id,
      label: c.nome,
      detalhe: [TIPO_CONTATO[c.tipoContato] || c.tipoContato, c.cpfCnpj, c.email].filter(Boolean).map(esc).join(' · '),
    }),
    vazio: (termo) => `
      Nenhum contato encontrado${termo ? ` para "${esc(termo)}"` : ''}.
      <a href="/pages/contatos.html?novo=1" target="_blank">Cadastrar novo contato</a>`,
    dica: 'Digite para buscar entre todos os contatos.',
    erro: 'Erro ao buscar contatos. Tente novamente.',
  });
}
