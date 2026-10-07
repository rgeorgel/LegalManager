import { esc } from './utils.js';
import { getContatos } from './contatos.js';

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
  root.classList.add('contato-picker');
  root.innerHTML = `
    <input type="text" class="form-control contato-picker-input" autocomplete="off" spellcheck="false"
      role="combobox" aria-autocomplete="list" aria-expanded="false"
      placeholder="${esc(placeholder || 'Digite nome, CPF/CNPJ ou e-mail...')}">
    <input type="hidden" class="contato-picker-value ${esc(valueClass)}">
    <div class="contato-picker-panel" role="listbox" hidden></div>`;

  const input = root.querySelector('.contato-picker-input');
  const hidden = root.querySelector('.contato-picker-value');
  const panel = root.querySelector('.contato-picker-panel');

  let selected = null;
  let resultados = [];
  let ativo = -1;
  let timer = null;
  let seq = 0;

  function setValue(v, { notify = false } = {}) {
    selected = v && v.id ? v : null;
    hidden.value = selected?.id ?? '';
    input.value = selected?.label ?? '';
    root.classList.toggle('has-value', !!selected);
    if (notify) onChange?.(selected);
  }

  function posicionar() {
    // position: fixed — o painel não fica cortado pelo overflow do modal.
    const r = input.getBoundingClientRect();
    const abaixo = window.innerHeight - r.bottom;
    panel.style.left = `${r.left}px`;
    panel.style.width = `${r.width}px`;
    if (abaixo < 240 && r.top > abaixo) {
      panel.style.top = '';
      panel.style.bottom = `${window.innerHeight - r.top + 4}px`;
    } else {
      panel.style.bottom = '';
      panel.style.top = `${r.bottom + 4}px`;
    }
  }

  function abrir() {
    if (!panel.hidden) return;
    panel.hidden = false;
    input.setAttribute('aria-expanded', 'true');
    posicionar();
    window.addEventListener('scroll', posicionar, true);
    window.addEventListener('resize', posicionar);
    document.addEventListener('mousedown', onFora, true);
  }

  function fechar() {
    if (panel.hidden) return;
    panel.hidden = true;
    input.setAttribute('aria-expanded', 'false');
    window.removeEventListener('scroll', posicionar, true);
    window.removeEventListener('resize', posicionar);
    document.removeEventListener('mousedown', onFora, true);
  }

  function restaurarTexto() {
    input.value = selected?.label ?? '';
  }

  function onFora(e) {
    if (root.contains(e.target)) return;
    fechar();
    restaurarTexto();
  }

  function detalhe(c) {
    return [TIPO_CONTATO[c.tipoContato] || c.tipoContato, c.cpfCnpj, c.email].filter(Boolean).map(esc).join(' · ');
  }

  function render(mensagem) {
    if (mensagem) {
      panel.innerHTML = `<div class="contato-picker-msg">${mensagem}</div>`;
      return;
    }
    const termo = input.value.trim();
    if (!resultados.length) {
      panel.innerHTML = `<div class="contato-picker-msg">
        Nenhum contato encontrado${termo ? ` para "${esc(termo)}"` : ''}.
        <a href="/pages/contatos.html?novo=1" target="_blank">Cadastrar novo contato</a>
      </div>`;
      return;
    }
    panel.innerHTML = resultados.map((c, i) => `
      <div class="contato-picker-option ${i === ativo ? 'active' : ''} ${c.id === selected?.id ? 'selected' : ''}"
        role="option" data-i="${i}" aria-selected="${c.id === selected?.id}">
        <div class="contato-picker-nome">${esc(c.nome)}</div>
        ${detalhe(c) ? `<div class="contato-picker-detalhe">${detalhe(c)}</div>` : ''}
      </div>`).join('')
      + (!termo && resultados.length >= 20
        ? '<div class="contato-picker-msg">Digite para buscar entre todos os contatos.</div>' : '');
  }

  async function buscar() {
    const termo = input.value.trim();
    const minha = ++seq;
    render('Buscando...');
    try {
      const res = await getContatos({ busca: termo, ativo: true, sortBy: 'nome', sortDir: 'asc', pageSize: 20 });
      if (minha !== seq) return; // resposta de uma busca antiga
      resultados = res?.items ?? [];
      ativo = resultados.length ? 0 : -1;
      render();
    } catch {
      if (minha === seq) render('Erro ao buscar contatos. Tente novamente.');
    }
  }

  function agendarBusca() {
    abrir();
    clearTimeout(timer);
    timer = setTimeout(buscar, 250);
  }

  function escolher(i) {
    const c = resultados[i];
    if (!c) return;
    setValue({ ...c, id: c.id, label: c.nome }, { notify: true });
    fechar();
  }

  function moverAtivo(delta) {
    if (!resultados.length) return;
    ativo = (ativo + delta + resultados.length) % resultados.length;
    render();
    panel.querySelector('.contato-picker-option.active')?.scrollIntoView({ block: 'nearest' });
  }

  input.addEventListener('focus', () => { input.select(); agendarBusca(); });
  input.addEventListener('input', agendarBusca);
  input.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); panel.hidden ? agendarBusca() : moverAtivo(1); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); moverAtivo(-1); }
    else if (e.key === 'Enter') { if (!panel.hidden) { e.preventDefault(); escolher(ativo); } }
    else if (e.key === 'Escape') {
      if (!panel.hidden) { e.preventDefault(); e.stopPropagation(); fechar(); restaurarTexto(); }
    }
    else if (e.key === 'Tab') { fechar(); restaurarTexto(); }
  });
  // mousedown (e não click) para escolher antes do blur do input.
  panel.addEventListener('mousedown', (e) => {
    const opt = e.target.closest('.contato-picker-option');
    if (!opt) return;
    e.preventDefault();
    escolher(Number(opt.dataset.i));
  });

  setValue(value);

  return {
    getValue: () => selected?.id ?? '',
    getLabel: () => selected?.label ?? '',
    setValue: (v) => setValue(v),
    focus: () => input.focus(),
  };
}
