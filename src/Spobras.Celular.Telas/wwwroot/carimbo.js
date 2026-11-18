// Editor do carimbo: arrastar com um dedo (ou o mouse) e mudar o tamanho com dois dedos, como o controle de jogo no
// celular. Durante o gesto so o desenho se move (resposta na hora, nada e gravado); ao soltar, o .NET recebe a posicao
// em pixels da foto e gruda o carimbo no canto mais perto.
const ligados = new WeakMap();

export function ligar(svg, ref) {
  desligar(svg);
  const toques = new Map();
  let arraste = null, pinca = null;

  const grupo = () => svg.querySelector("g.carimbo");
  const escala = () => { const m = svg.getScreenCTM(); return m ? m.a : 1; };
  const caixa = g => ({ x: +g.dataset.x, y: +g.dataset.y, w: +g.dataset.w, h: +g.dataset.h });
  const foto = () => ({ w: +svg.dataset.largura, h: +svg.dataset.altura });

  function distancia() { const [a, b] = [...toques.values()]; return Math.hypot(a.x - b.x, a.y - b.y) || 1; }

  function baixo(e) {
    const g = grupo();
    if (!g) return;
    toques.set(e.pointerId, { x: e.clientX, y: e.clientY });
    svg.setPointerCapture(e.pointerId);
    if (toques.size === 2) {
      arraste = null;
      pinca = { inicio: distancia(), fator: 1 };
    } else if (toques.size === 1 && (e.target.closest("g.carimbo") || e.pointerType === "touch")) {
      arraste = { x: e.clientX, y: e.clientY, dx: 0, dy: 0, dentro: !!e.target.closest("g.carimbo") };
    }
    if (arraste?.dentro || pinca) e.preventDefault();
  }

  function mover(e) {
    if (!toques.has(e.pointerId)) return;
    toques.set(e.pointerId, { x: e.clientX, y: e.clientY });
    const g = grupo();
    if (!g) return;
    if (pinca && toques.size >= 2) {
      pinca.fator = Math.min(2.5, Math.max(0.4, distancia() / pinca.inicio));
      const c = caixa(g), cx = c.x + c.w / 2, cy = c.y + c.h / 2;
      g.setAttribute("transform", `translate(${cx} ${cy}) scale(${pinca.fator}) translate(${-cx} ${-cy})`);
      e.preventDefault();
    } else if (arraste && arraste.dentro) {
      const s = escala(), c = caixa(g), f = foto();
      // nunca sai da foto
      arraste.dx = Math.min(f.w - c.x - c.w, Math.max(-c.x, (e.clientX - arraste.x) / s));
      arraste.dy = Math.min(f.h - c.y - c.h, Math.max(-c.y, (e.clientY - arraste.y) / s));
      g.setAttribute("transform", `translate(${arraste.dx} ${arraste.dy})`);
      e.preventDefault();
    }
  }

  async function cima(e) {
    if (!toques.has(e.pointerId)) return;
    toques.delete(e.pointerId);
    const g = grupo();
    if (pinca && toques.size < 2) {
      const fator = pinca.fator;
      pinca = null;
      arraste = null;
      if (g && Math.abs(fator - 1) > 0.02) await ref.invokeMethodAsync("CarimboTamanho", fator);
      return;
    }
    if (arraste && toques.size === 0) {
      const a = arraste;
      arraste = null;
      if (g && a.dentro && (Math.abs(a.dx) > 0.5 || Math.abs(a.dy) > 0.5)) {
        const c = caixa(g);
        await ref.invokeMethodAsync("CarimboSolto", c.x + a.dx, c.y + a.dy, c.w, c.h);
      }
    }
  }

  const opcoes = { passive: false };
  svg.addEventListener("pointerdown", baixo, opcoes);
  svg.addEventListener("pointermove", mover, opcoes);
  svg.addEventListener("pointerup", cima);
  svg.addEventListener("pointercancel", cima);
  svg.style.touchAction = "none";
  ligados.set(svg, () => {
    svg.removeEventListener("pointerdown", baixo, opcoes);
    svg.removeEventListener("pointermove", mover, opcoes);
    svg.removeEventListener("pointerup", cima);
    svg.removeEventListener("pointercancel", cima);
  });
}

export function desligar(svg) {
  const fim = svg && ligados.get(svg);
  if (fim) { fim(); ligados.delete(svg); }
}
