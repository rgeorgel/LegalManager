// Fundo animado das telas públicas do parceiro: uma rede de pontos ligados (as indicações),
// com alguns nós em ouro. Desenha num <canvas class="pa-rede"> e fica parado com prefers-reduced-motion.
const canvas = document.querySelector('.pa-rede');
if (canvas) {
  const ctx = canvas.getContext('2d');
  const parado = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const ALCANCE = 140;
  let largura = 0, altura = 0;
  const nos = [];

  function dimensionar() {
    const r = canvas.getBoundingClientRect();
    const d = Math.min(devicePixelRatio || 1, 2);
    largura = r.width; altura = r.height;
    canvas.width = largura * d; canvas.height = altura * d;
    ctx.setTransform(d, 0, 0, d, 0, 0);
    const total = Math.round(Math.min(80, largura * altura / 14000));
    while (nos.length < total) {
      nos.push({
        x: Math.random() * largura, y: Math.random() * altura,
        vx: (Math.random() - .5) * .25, vy: (Math.random() - .5) * .25,
        ouro: Math.random() < .14,
      });
    }
    nos.length = total;
    desenhar();
  }

  function desenhar() {
    ctx.clearRect(0, 0, largura, altura);
    for (let i = 0; i < nos.length; i++) {
      for (let j = i + 1; j < nos.length; j++) {
        const a = nos[i], b = nos[j];
        const dist = Math.hypot(a.x - b.x, a.y - b.y);
        if (dist >= ALCANCE) continue;
        const ouro = a.ouro || b.ouro, forca = 1 - dist / ALCANCE;
        ctx.strokeStyle = ouro ? `rgba(240,181,77,${forca * .5})` : `rgba(160,185,255,${forca * .22})`;
        ctx.lineWidth = ouro ? 1.1 : .8;
        ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
      }
    }
    for (const p of nos) {
      ctx.fillStyle = p.ouro ? '#f0b54d' : 'rgba(200,215,255,.7)';
      ctx.beginPath(); ctx.arc(p.x, p.y, p.ouro ? 3.2 : 1.8, 0, Math.PI * 2); ctx.fill();
    }
  }

  function passo() {
    if (!document.hidden) {
      for (const p of nos) {
        p.x += p.vx; p.y += p.vy;
        if (p.x < 0 || p.x > largura) p.vx *= -1;
        if (p.y < 0 || p.y > altura) p.vy *= -1;
      }
      desenhar();
    }
    requestAnimationFrame(passo);
  }

  addEventListener('resize', dimensionar);
  dimensionar();
  if (!parado) requestAnimationFrame(passo);
}
