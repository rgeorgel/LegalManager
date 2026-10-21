import { esc } from './utils.js';

/**
 * Campo de busca com autocomplete no servidor — base de createContatoPicker e createProcessoPicker.
 * Usa as classes .contato-picker-* (css/styles.css), compartilhadas por todos os pickers.
 *
 *   createSearchPicker(root, {
 *     buscar: async (termo) => [...],              // itens da página de resultados
 *     opcao: (item) => ({ id, label, detalhe }),   // detalhe: HTML já escapado (opcional)
 *     vazio: (termo) => 'HTML',                    // mensagem sem resultados
 *     dica: 'HTML', tamanhoPagina: 20,             // dica exibida sem termo e com a página cheia
 *     erro: 'Erro ao buscar...',
 *     valueClass, value: { id, label }, placeholder, onChange,
 *     permitirLimpar: false,                       // true: apagar o texto e sair desfaz a seleção
 *     criar: { rotulo: (termo) => 'HTML', acao: async (termo) => item | null },
 *                                                  // (opcional) botão no fim da lista para cadastrar
 *                                                  // um item novo; o item devolvido fica selecionado
 *   });
 *
 * Digitar não apaga a seleção: ao sair do campo sem escolher outro, o texto selecionado volta.
 */
export function createSearchPicker(root, {
  buscar: buscarItens, opcao, vazio, dica, tamanhoPagina = 20, erro = 'Erro ao buscar. Tente novamente.',
  valueClass = '', value = null, placeholder = '', permitirLimpar = false, criar = null, onChange,
} = {}) {
  root.classList.add('contato-picker');
  root.innerHTML = `
    <input type="text" class="form-control contato-picker-input" autocomplete="off" spellcheck="false"
      role="combobox" aria-autocomplete="list" aria-expanded="false"
      placeholder="${esc(placeholder)}">
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
    // permitirLimpar: apagar o texto e sair do campo desfaz a seleção (vínculo opcional).
    if (permitirLimpar && selected && !input.value.trim()) { setValue(null, { notify: true }); return; }
    input.value = selected?.label ?? '';
  }

  function onFora(e) {
    if (root.contains(e.target)) return;
    fechar();
    restaurarTexto();
  }

  function render(mensagem) {
    if (mensagem) {
      panel.innerHTML = `<div class="contato-picker-msg">${mensagem}</div>`;
      return;
    }
    const termo = input.value.trim();
    const botaoCriar = criar
      ? `<button type="button" class="contato-picker-criar">${criar.rotulo(termo)}</button>`
      : '';
    if (!resultados.length) {
      panel.innerHTML = `<div class="contato-picker-msg">${vazio ? vazio(termo) : 'Nenhum resultado.'}</div>${botaoCriar}`;
      return;
    }
    panel.innerHTML = resultados.map((item, i) => {
      const o = opcao(item);
      return `
      <div class="contato-picker-option ${i === ativo ? 'active' : ''} ${o.id === selected?.id ? 'selected' : ''}"
        role="option" data-i="${i}" aria-selected="${o.id === selected?.id}">
        <div class="contato-picker-nome">${esc(o.label)}</div>
        ${o.detalhe ? `<div class="contato-picker-detalhe">${o.detalhe}</div>` : ''}
      </div>`;
    }).join('')
      + (dica && !termo && resultados.length >= tamanhoPagina ? `<div class="contato-picker-msg">${dica}</div>` : '')
      + botaoCriar;
  }

  async function buscar() {
    const termo = input.value.trim();
    const minha = ++seq;
    render('Buscando...');
    try {
      const itens = await buscarItens(termo);
      if (minha !== seq) return; // resposta de uma busca antiga
      resultados = itens ?? [];
      ativo = resultados.length ? 0 : -1;
      render();
    } catch {
      if (minha === seq) render(esc(erro));
    }
  }

  function agendarBusca() {
    abrir();
    clearTimeout(timer);
    timer = setTimeout(buscar, 250);
  }

  function escolher(i) {
    const item = resultados[i];
    if (!item) return;
    const o = opcao(item);
    setValue({ ...item, id: o.id, label: o.label }, { notify: true });
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
  panel.addEventListener('mousedown', async (e) => {
    if (criar && e.target.closest('.contato-picker-criar')) {
      e.preventDefault();
      const termo = input.value.trim();
      fechar();
      restaurarTexto();
      const item = await criar.acao(termo);
      if (item) {
        const o = opcao(item);
        setValue({ ...item, id: o.id, label: o.label }, { notify: true });
      }
      return;
    }
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
