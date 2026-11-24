// Página pública /verificador-de-citacoes.html (sem login): cola o texto, confere as citações na base
// oficial (POST /api/jurisprudencia/publico/verificar, limitado por IP) e convida a criar conta.
// O desenho do resultado é o mesmo do sistema (js/citacoes.js).
import { resumoCitacoesHtml, listaCitacoesHtml } from './citacoes.js';

const $ = id => document.getElementById(id);
const LIMITE = 20000;

// Exemplo com um pouco de tudo: tema real, tema e número CNJ inexistentes, súmula a conferir.
const EXEMPLO = `A revisão de cláusula contratual em recurso especial esbarra na Súmula 5/STJ.
A fixação dos honorários segue o Tema 1.076/STJ, e não o Tema 9.999/STJ.
No mesmo sentido decidiu o TJSP na Apelação Cível 1004521-33.2023.8.26.0100.`;

const texto = $('vpTexto');
const botao = $('vpVerificar');
const erro = $('vpErro');
const resultado = $('vpResultado');

function atualizarContador() {
  $('vpContador').textContent = `${texto.value.length.toLocaleString('pt-BR')} / ${LIMITE.toLocaleString('pt-BR')}`;
}

function mostrarErro(msg) {
  erro.textContent = msg;
  erro.hidden = !msg;
}

function capturar(evento, props) {
  try { window.posthog?.capture?.(evento, props); } catch { /* analytics opcional */ }
}

async function verificar() {
  const conteudo = texto.value.trim();
  mostrarErro('');
  if (!conteudo) {
    mostrarErro('Cole o trecho da peça com as citações.');
    texto.focus();
    return;
  }
  botao.disabled = true;
  botao.textContent = 'Verificando...';
  resultado.innerHTML = '';
  try {
    const res = await fetch('/api/jurisprudencia/publico/verificar', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ texto: conteudo }),
    });
    if (res.status === 429) {
      mostrarErro('Você atingiu o limite de verificações gratuitas por hora. Crie uma conta grátis para continuar.');
      return;
    }
    const dados = await res.json().catch(() => null);
    if (!res.ok) {
      mostrarErro(dados?.message || 'Não foi possível verificar agora. Tente de novo em instantes.');
      return;
    }
    resultado.innerHTML = `${resumoCitacoesHtml(dados)}<div class="cit-lista">${listaCitacoesHtml(dados)}</div>
      <div class="verificador-cta">
        <strong>Quer isso dentro das suas peças?</strong> No Causify, a IA gera peças citando só precedentes da base
        oficial e confere cada citação sozinha.<br>
        <a class="btn btn-primary" href="/register.html?utm_source=verificador-citacoes">Criar conta gratuita</a>
      </div>`;
    capturar('verificador_citacoes_usado', {
      citacoes: dados.citacoes?.length ?? 0,
      verificadas: dados.verificadas,
      inexistentes: dados.inexistentes,
    });
  } catch {
    mostrarErro('Não foi possível verificar agora. Confira sua conexão e tente de novo.');
  } finally {
    botao.disabled = false;
    botao.textContent = 'Verificar citações';
  }
}

texto.addEventListener('input', () => { atualizarContador(); mostrarErro(''); });
botao.addEventListener('click', verificar);
$('vpExemplo').addEventListener('click', () => {
  texto.value = EXEMPLO;
  atualizarContador();
  texto.focus();
});
atualizarContador();
