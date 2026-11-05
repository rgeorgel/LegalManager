// Seletor da biblioteca de imagens (modal) para os editores do superadmin — hoje o de guias.
// escolherImagem() devolve a imagem escolhida ({ url, nome, ... }) ou null se fechar.
// "Enviar nova" grava na pasta selecionada; nome repetido vira "nome-2" sem perguntar.
import { esc } from '/js/utils.js';
import { listarPastas, listarImagens, enviarImagem, TIPOS_ACEITOS, tamanhoLegivel } from './imagensApi.js';

const ESTILOS = `
  #seletorImagens .modal { max-width: 860px; width: 100%; display: flex; flex-direction: column; }
  .si-barra { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 12px; }
  .si-barra select, .si-barra input { flex: 1; min-width: 140px; }
  .si-grade { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 150px), 1fr)); gap: 10px; min-height: 160px; }
  .si-item { border: 1px solid var(--color-border); border-radius: 8px; overflow: hidden; background: var(--color-surface); cursor: pointer; padding: 0; text-align: left; color: var(--color-text); display: flex; flex-direction: column; }
  .si-item:hover, .si-item:focus-visible { border-color: var(--color-primary); outline: 2px solid color-mix(in srgb, var(--color-primary) 30%, transparent); }
  .si-item img { width: 100%; aspect-ratio: 4 / 3; object-fit: contain; background: var(--color-bg); display: block; }
  .si-item span { font-size: 12px; padding: 6px 8px 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .si-item small { font-size: 11px; color: var(--color-text-muted); padding: 0 8px 6px; }
  .si-vazio { grid-column: 1 / -1; color: var(--color-text-muted); font-size: 13px; text-align: center; padding: 32px 8px; }
  .si-status { font-size: 12px; color: var(--color-text-muted); min-height: 18px; margin-bottom: 6px; }
`;

const HTML = `
  <div class="modal-overlay" id="seletorImagens">
    <div class="modal" role="dialog" aria-modal="true" aria-labelledby="siTitulo">
      <div class="modal-header">
        <span class="modal-title" id="siTitulo">Biblioteca de imagens</span>
        <button type="button" class="modal-close" data-si-fechar aria-label="Fechar">✕</button>
      </div>
      <div class="modal-body">
        <div class="si-barra">
          <select class="form-control" id="siPasta" aria-label="Pasta"></select>
          <input type="search" class="form-control" id="siBusca" placeholder="Buscar por nome" aria-label="Buscar">
          <button type="button" class="btn btn-primary btn-sm" id="siEnviar">Enviar nova</button>
          <input type="file" id="siArquivo" accept="${TIPOS_ACEITOS}" hidden>
        </div>
        <div class="si-status" id="siStatus" aria-live="polite"></div>
        <div class="si-grade" id="siGrade"></div>
      </div>
      <div class="modal-footer">
        <a class="btn btn-secondary btn-sm" href="/superadmin/imagens.html" target="_blank" rel="noopener" style="margin-right:auto">Gerenciar imagens ↗</a>
        <button type="button" class="btn btn-secondary" data-si-fechar>Cancelar</button>
      </div>
    </div>
  </div>`;

let modal = null;
let imagens = [];
let resolver = null;
let pastaPadrao = 'guias';

function montar() {
  if (modal) return;
  const estilo = document.createElement('style');
  estilo.textContent = ESTILOS;
  document.head.appendChild(estilo);
  document.body.insertAdjacentHTML('beforeend', HTML);
  modal = document.getElementById('seletorImagens');

  modal.addEventListener('click', e => {
    if (e.target === modal || e.target.closest('[data-si-fechar]')) return fechar(null);
    const item = e.target.closest('.si-item');
    if (item) fechar(imagens.find(i => i.id === item.dataset.id) ?? null);
  });
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && modal.classList.contains('open')) fechar(null); });
  el('siPasta').addEventListener('change', carregar);
  el('siBusca').addEventListener('input', render);
  el('siEnviar').addEventListener('click', () => el('siArquivo').click());
  el('siArquivo').addEventListener('change', async e => {
    const arquivo = e.target.files[0];
    e.target.value = '';
    if (!arquivo) return;
    const pasta = el('siPasta').selectedOptions[0]?.dataset.nome || pastaPadrao;
    el('siStatus').textContent = `Enviando ${arquivo.name}…`;
    try {
      const img = await enviarImagem(arquivo, { pasta, conflito: 'renomear' });
      fechar(img);
    } catch (err) {
      el('siStatus').textContent = `Não foi possível enviar: ${err.message}`;
    }
  });
}

const el = id => document.getElementById(id);

function fechar(valor) {
  modal.classList.remove('open');
  const r = resolver;
  resolver = null;
  r?.(valor);
}

async function carregar() {
  el('siGrade').innerHTML = '<div class="si-vazio">Carregando…</div>';
  try {
    imagens = await listarImagens(el('siPasta').value || null);
    render();
  } catch (e) {
    el('siGrade').innerHTML = `<div class="si-vazio" style="color:var(--color-danger)">Erro: ${esc(e.message)}</div>`;
  }
}

function render() {
  const termo = el('siBusca').value.trim().toLowerCase();
  const lista = termo ? imagens.filter(i => `${i.pasta}/${i.nome}`.includes(termo)) : imagens;
  el('siGrade').innerHTML = lista.length
    ? lista.map(i => `
        <button type="button" class="si-item" data-id="${i.id}" title="${esc(i.url)}">
          <img src="${esc(i.url)}?v=${new Date(i.atualizadoEm).getTime()}" alt="" loading="lazy">
          <span>${esc(i.nome)}</span>
          <small>${esc(i.pasta)} · ${tamanhoLegivel(i.tamanho)}</small>
        </button>`).join('')
    : `<div class="si-vazio">${imagens.length ? 'Nenhuma imagem com esse nome.' : 'Nenhuma imagem aqui ainda. Use “Enviar nova”.'}</div>`;
}

/** Abre a biblioteca. Começa na pasta `pasta` (se existir); envios novos vão para a pasta escolhida. */
export async function escolherImagem({ pasta = 'guias', titulo = 'Biblioteca de imagens' } = {}) {
  montar();
  pastaPadrao = pasta;
  el('siTitulo').textContent = titulo;
  el('siBusca').value = '';
  el('siStatus').textContent = '';
  modal.classList.add('open');

  try {
    const pastas = await listarPastas();
    const inicial = pastas.find(p => p.nome === pasta);
    el('siPasta').innerHTML = '<option value="">Todas as pastas</option>' + pastas
      .map(p => `<option value="${p.id}" data-nome="${esc(p.nome)}">📁 ${esc(p.nome)} (${p.quantidade})</option>`).join('');
    el('siPasta').value = inicial?.id ?? '';
  } catch {
    el('siPasta').innerHTML = '<option value="">Todas as pastas</option>';
  }
  carregar();
  el('siBusca').focus();

  return new Promise(resolve => { resolver = resolve; });
}
