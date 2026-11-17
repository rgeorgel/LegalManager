// Recursos em teste liberados pelo super admin para o escritório (ver RecursosEmTeste no backend).
// Uma consulta por página: liberar ou tirar vale no próximo carregamento, sem novo login.
import { apiFetch } from './api.js';

export const RECURSOS = Object.freeze({ assinaturaDigital: 'assinatura-digital' });

let pedido = null;

/** Lista de chaves liberadas; vazia se a consulta falhar (o recurso fica escondido). */
export function recursosLiberados() {
  if (!pedido) pedido = apiFetch('/recursos').then(r => (Array.isArray(r) ? r : [])).catch(() => []);
  return pedido;
}

export async function recursoLiberado(chave) {
  return (await recursosLiberados()).includes(chave);
}
