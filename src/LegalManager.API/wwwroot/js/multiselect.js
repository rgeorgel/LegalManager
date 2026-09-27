import { esc } from './utils.js';

/**
 * Dropdown de seleção múltipla com checkboxes, no lugar de um <select> simples.
 * Nada selecionado = "todos" (o texto do botão mostra `placeholder`).
 *
 *   const ms = createMultiSelect(document.getElementById('filtTag'), {
 *     placeholder: 'Todas as tags',
 *     plural: 'tags',
 *     options: [{ value: 'vip', label: 'vip' }],
 *     onChange: (values) => { ... },
 *   });
 *   ms.setOptions([...]); ms.setValues(['vip']); ms.getValues();
 *
 * `setValues` não dispara `onChange` (usado para restaurar estado/filtros salvos).
 */
export function createMultiSelect(root, { placeholder, plural, options = [], values = [], onChange } = {}) {
  let baseOpts = [];
  let opts = [];
  let selected = new Set(values);

  root.classList.add('multiselect');
  root.innerHTML = `
    <button type="button" class="form-control multiselect-toggle" aria-haspopup="true" aria-expanded="false">
      <span class="multiselect-label"></span>
    </button>
    <div class="multiselect-panel" hidden>
      <input type="text" class="form-control multiselect-search" placeholder="Buscar..." autocomplete="off" hidden>
      <div class="multiselect-options"></div>
      <div class="multiselect-footer">
        <button type="button" class="multiselect-clear">Limpar seleção</button>
      </div>
    </div>`;

  const toggle = root.querySelector('.multiselect-toggle');
  const label = root.querySelector('.multiselect-label');
  const panel = root.querySelector('.multiselect-panel');
  const search = root.querySelector('.multiselect-search');
  const list = root.querySelector('.multiselect-options');

  function labelOf(value) {
    return opts.find(o => o.value === value)?.label ?? value;
  }

  function renderLabel() {
    const vals = [...selected];
    label.textContent = !vals.length ? placeholder
      : vals.length === 1 ? labelOf(vals[0])
      : `${vals.length} ${plural}`;
    root.classList.toggle('has-value', vals.length > 0);
    toggle.title = vals.map(labelOf).join(', ');
  }

  function renderOptions() {
    const termo = search.value.trim().toLocaleLowerCase('pt-BR');
    const visiveis = termo ? opts.filter(o => o.label.toLocaleLowerCase('pt-BR').includes(termo)) : opts;
    search.hidden = opts.length <= 8;
    list.innerHTML = visiveis.length
      ? visiveis.map(o => `
          <label class="multiselect-option">
            <input type="checkbox" value="${esc(o.value)}" ${selected.has(o.value) ? 'checked' : ''}>
            <span>${esc(o.label)}</span>
          </label>`).join('')
      : `<div class="multiselect-empty">${opts.length ? 'Nada encontrado' : 'Nenhuma opção'}</div>`;
  }

  function open() {
    panel.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    document.addEventListener('click', onOutside, true);
    document.addEventListener('keydown', onKey);
  }

  function close() {
    panel.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
    document.removeEventListener('click', onOutside, true);
    document.removeEventListener('keydown', onKey);
  }

  function onOutside(e) { if (!root.contains(e.target)) close(); }
  function onKey(e) { if (e.key === 'Escape') { close(); toggle.focus(); } }

  function changed() {
    renderLabel();
    onChange?.([...selected]);
  }

  toggle.addEventListener('click', () => (panel.hidden ? open() : close()));
  search.addEventListener('input', renderOptions);
  list.addEventListener('change', (e) => {
    const cb = e.target.closest('input[type="checkbox"]');
    if (!cb) return;
    if (cb.checked) selected.add(cb.value); else selected.delete(cb.value);
    changed();
  });
  root.querySelector('.multiselect-clear').addEventListener('click', () => {
    if (!selected.size) return;
    selected.clear();
    renderOptions();
    changed();
  });

  const api = {
    setOptions(newOptions) {
      baseOpts = newOptions.map(o => (typeof o === 'string' ? { value: o, label: o } : o));
      // Valores selecionados que não estão na lista (ex.: filtro salvo com tag que
      // não existe mais) continuam visíveis para poderem ser desmarcados.
      const extras = [...selected].filter(v => !baseOpts.some(o => o.value === v));
      opts = [...baseOpts, ...extras.map(v => ({ value: v, label: v }))];
      renderOptions();
      renderLabel();
    },
    setValues(newValues) {
      selected = new Set(newValues || []);
      api.setOptions(baseOpts);
    },
    getValues: () => [...selected],
  };

  api.setOptions(options);
  return api;
}
