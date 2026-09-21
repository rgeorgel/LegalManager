// Navegação para o perfil do contato — usado em qualquer tela que exiba dados de
// um contato (Contatos, Honorários, Processos, Financeiro, Documentos, etc).
// O parâmetro "return" carrega a URL atual (rota + querystring) para que o botão
// "Voltar" da página de perfil leve o usuário exatamente para onde ele estava.

export function contatoPerfilUrl(contatoId) {
  if (!contatoId) return '#';
  const ret = encodeURIComponent(location.pathname + location.search);
  return `/pages/contato-detalhe.html?id=${contatoId}&return=${ret}`;
}

export function contatoLink(contatoId, label) {
  if (!contatoId) return label ?? '—';
  return `<a href="${contatoPerfilUrl(contatoId)}">${label ?? ''}</a>`;
}

export function goToContatoPerfil(contatoId) {
  if (!contatoId) return;
  location.href = contatoPerfilUrl(contatoId);
}

// Liga o botão "Voltar" de uma página de detalhe (contato, processo, etc.) para
// retornar exatamente à URL de onde o usuário veio, com fallback quando acessada
// diretamente (sem parâmetro "return").
export function setupVoltarButton(btnEl, fallbackUrl) {
  if (!btnEl) return;
  const params = new URLSearchParams(location.search);
  const ret = params.get('return');
  const target = ret ? decodeURIComponent(ret) : fallbackUrl;
  btnEl.setAttribute('href', target);
}
