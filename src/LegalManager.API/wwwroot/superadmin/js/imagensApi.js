// Biblioteca de imagens públicas (superadmin → Imagens; API em SuperAdminImagensController).
// Usado pela tela de imagens e pelo seletor do editor de guias.
import { saFetch, getToken } from './superAdminApi.js';

export const TIPOS_ACEITOS = 'image/png,image/jpeg,image/gif,image/webp';

export const listarPastas = () => saFetch('/superadmin/imagens/pastas');
export const criarPasta = nome => saFetch('/superadmin/imagens/pastas', { method: 'POST', body: JSON.stringify({ nome }) });
export const excluirPasta = id => saFetch(`/superadmin/imagens/pastas/${id}`, { method: 'DELETE' });
export const listarImagens = pastaId => saFetch('/superadmin/imagens' + (pastaId ? `?pastaId=${pastaId}` : ''));
export const renomearImagem = (id, nome) => saFetch(`/superadmin/imagens/${id}`, { method: 'PATCH', body: JSON.stringify({ nome }) });
export const excluirImagem = id => saFetch(`/superadmin/imagens/${id}`, { method: 'DELETE' });

// Multipart não passa pelo saFetch (ele fixa Content-Type JSON).
async function enviarForm(url, method, form) {
  const res = await fetch(url, { method, headers: { Authorization: `Bearer ${getToken()}` }, body: form });
  const corpo = await res.json().catch(() => ({}));
  if (res.status === 401) {
    const err = new Error('Sessão expirada. Entre de novo no superadmin.');
    err.status = 401;
    throw err;
  }
  if (!res.ok) {
    const err = new Error(corpo.message || `HTTP ${res.status}`);
    err.status = res.status;
    err.corpo = corpo;
    throw err;
  }
  return corpo;
}

/** conflito: 'erro' (409 com nomeSugerido), 'substituir' (mesma URL) ou 'renomear' (nome-2). */
export function enviarImagem(arquivo, { pasta, nome = null, conflito = 'erro' }) {
  const form = new FormData();
  form.append('arquivo', arquivo);
  form.append('pasta', pasta);
  if (nome) form.append('nome', nome);
  form.append('conflito', conflito);
  return enviarForm('/api/superadmin/imagens', 'POST', form);
}

export function substituirImagem(id, arquivo) {
  const form = new FormData();
  form.append('arquivo', arquivo);
  return enviarForm(`/api/superadmin/imagens/${id}/arquivo`, 'PUT', form);
}

/**
 * Envio da tela de imagens: se o nome já existir na pasta, pergunta se substitui (mesma URL)
 * ou salva com o nome sugerido. Devolve a imagem salva, ou null se a pessoa desistiu.
 */
export async function enviarPerguntandoConflito(arquivo, pasta) {
  try {
    return await enviarImagem(arquivo, { pasta });
  } catch (e) {
    if (e.status !== 409) throw e;
    const { nomeExistente, nomeSugerido } = e.corpo;
    const substituir = confirm(
      `Já existe "${nomeExistente}" na pasta "${pasta}".\n\n` +
      `OK: substituir a imagem atual (o endereço continua o mesmo).\n` +
      `Cancelar: salvar como "${nomeSugerido}".`);
    return enviarImagem(arquivo, { pasta, conflito: substituir ? 'substituir' : 'renomear' });
  }
}

export const urlAbsoluta = url => location.origin + url;

export function markdownDe(img) {
  const descricao = img.nome.replace(/\.[a-z]+$/, '').replace(/-/g, ' ');
  return `![${descricao}](${img.url})`;
}

export function tamanhoLegivel(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB`;
}
