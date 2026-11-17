// Recursos em teste liberados pelo super admin para o escritório (ver RecursosEmTeste no backend).
// A lista fica 2 minutos na sessão da aba (o menu precisa dela em toda página); liberar ou tirar
// vale em até 2 minutos, sem novo login. A API barra quem não está liberado de qualquer forma.
import { apiFetch, getUser } from './api.js';

export const RECURSOS = Object.freeze({ assinaturaDigital: 'assinatura-digital' });

const VALIDADE_MS = 2 * 60 * 1000;
let pedido = null;

function chave() {
  return `recursos_liberados:${getUser()?.tenantId ?? ''}`;
}

function lerCache() {
  try {
    const c = JSON.parse(sessionStorage.getItem(chave()) || 'null');
    return Array.isArray(c?.lista) ? c : null;
  } catch { return null; }
}

/** O que já se sabe sem ir ao servidor (para desenhar o menu sem esperar). */
export function recursosEmCache() {
  return lerCache()?.lista ?? [];
}

/** Lista de chaves liberadas; vazia se a consulta falhar (o recurso fica escondido). */
export function recursosLiberados() {
  if (!pedido) {
    const c = lerCache();
    pedido = c && Date.now() - c.em < VALIDADE_MS
      ? Promise.resolve(c.lista)
      : apiFetch('/recursos')
        .then(r => {
          const lista = Array.isArray(r) ? r : [];
          try { sessionStorage.setItem(chave(), JSON.stringify({ em: Date.now(), lista })); } catch { /* storage bloqueado */ }
          return lista;
        })
        .catch(() => []);
  }
  return pedido;
}

export async function recursoLiberado(chaveRecurso) {
  return (await recursosLiberados()).includes(chaveRecurso);
}
