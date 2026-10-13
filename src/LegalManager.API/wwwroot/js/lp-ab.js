// Teste A/B de tema das landing pages de campanha, controlado por uma feature flag multivariada do
// PostHog com variantes A (visual original), B (clara) e C (verde) — ver /css/lp-temas.css.
//
// Uso, no <head>, depois do snippet do PostHog e antes do conteúdo (script clássico, síncrono):
//   <link rel="stylesheet" href="/css/lp-temas.css">
//   <script src="/js/lp-ab.js" data-flag="lp-facebook-ab"></script>
//
// Diferente da home, aqui não há redirect: o tema é aplicado na própria página (<html data-tema>),
// então a URL do anúncio, o Pixel e o login do Google continuam iguais.
// - A variante fica em cache no localStorage (por flag): da 2ª visita em diante o tema é imediato.
// - 1ª visita: esconde a página até a flag chegar (máx. ESPERA_MS). Se estourar, fica na A sem
//   registrar exposição nem cache — o visitante não entra no experimento.
// - A exposição ($feature_flag_called) só é registrada quando a flag bate com o tema exibido.
// - Robôs veem a A. QA: ?tema=A|B|C força neste navegador (fora do experimento); ?tema=sorteio volta para a flag.
(function () {
  var raiz = document.documentElement;
  var script = document.currentScript;
  var FLAG = (script && script.getAttribute('data-flag')) || '';
  var CHAVE = 'ab_' + FLAG;
  var CHAVE_QA = 'lp_ab_qa';
  var ESPERA_MS = 1000;
  var TEMAS = { A: null, B: 'clara', C: 'verde' };
  var COR_BARRA = { B: '#ffffff', C: '#0d3b2e' };

  window.LP_TEMA = 'A';

  // Eventos que precisam do tema certo (ex.: lp_view) esperam a decisão: window.quandoTemaDefinido(fn).
  var definido = false, pendentes = [];
  window.quandoTemaDefinido = function (fn) { if (definido) fn(window.LP_TEMA); else pendentes.push(fn); };
  function definir() {
    raiz.classList.remove('ab-espera');
    if (definido) return;
    definido = true;
    pendentes.splice(0).forEach(function (fn) { try { fn(window.LP_TEMA); } catch (e) {} });
  }

  function ler(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
  function gravar(k, v) { try { if (v === null) localStorage.removeItem(k); else localStorage.setItem(k, v); } catch (e) {} }
  function valida(v) { return TEMAS.hasOwnProperty(v) ? v : null; }

  function aplicar(v) {
    window.LP_TEMA = v;
    if (TEMAS[v]) raiz.setAttribute('data-tema', TEMAS[v]);
    else raiz.removeAttribute('data-tema');
    if (COR_BARRA[v]) {
      var meta = document.querySelector('meta[name="theme-color"]');
      if (meta) meta.setAttribute('content', COR_BARRA[v]);
    }
    try { if (window.posthog && typeof window.posthog.register === 'function') window.posthog.register({ lp_tema: v }); } catch (e) {}
  }

  // window.posthog sempre lido na hora: ao carregar, a biblioteca troca o stub do snippet por outra instância.
  function ph() { return window.posthog; }

  try {
    if (!FLAG) { definir(); return; }

    var params = new URLSearchParams(location.search);
    var forcado = (params.get('tema') || '').toUpperCase();
    if (forcado === 'SORTEIO') gravar(CHAVE_QA, null);
    else if (valida(forcado)) gravar(CHAVE_QA, forcado);
    var qa = valida(ler(CHAVE_QA));
    if (qa) { aplicar(qa); definir(); return; }

    var robo = /bot|crawl|spider|slurp|facebookexternalhit|lighthouse|headless|preview/i.test(navigator.userAgent || '');
    if (robo) { definir(); return; }

    var cache = valida(ler(CHAVE));
    if (cache) aplicar(cache);

    var temFlags = ph() && typeof ph().onFeatureFlags === 'function';
    if (!temFlags) { definir(); return; } // localhost ou PostHog bloqueado: tema do cache ou A, sem exposição

    var esperando = !cache;
    var estourou = false;
    var timer = null;
    if (esperando) {
      raiz.classList.add('ab-espera');
      var st = document.createElement('style');
      st.textContent = 'html.ab-espera{visibility:hidden}';
      document.head.appendChild(st);
      timer = setTimeout(function () { estourou = true; definir(); }, ESPERA_MS);
    } else {
      definir(); // tema do cache já aplicado; a flag só atualiza o cache e registra a exposição
    }

    var decidido = false;
    ph().onFeatureFlags(function (flags, variantes, ctx) {
      try {
        if (decidido) return; // onFeatureFlags pode disparar de novo quando as flags recarregam
        if (estourou || (ctx && ctx.errorsLoading)) { definir(); return; }
        decidido = true;
        clearTimeout(timer);
        var v = valida(ph().getFeatureFlag(FLAG, { send_event: false })) || 'A';
        gravar(CHAVE, v);
        if (esperando) aplicar(v);
        // Exposição só se o visitante está vendo o tema que a flag atribuiu a ele.
        if (v === window.LP_TEMA) ph().getFeatureFlag(FLAG);
        definir();
      } catch (e) { definir(); }
    });
  } catch (e) { definir(); }
})();
