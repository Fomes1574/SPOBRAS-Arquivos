// Camera do app (dentro da tela). A foto e pega no proprio toque, aqui no navegador, sem esperar o .NET:
// o que estava no visor no instante do toque e o que fica. So depois a tela pisca e a foto vai para o rascunho.
let stream = null, track = null, captura = null, video = null, frente = false;
let alvo = 4 / 3, flash = "auto", dotnet = null, botao = null, miniatura = null, visor = null, aviso = null;
let fotografando = false;
const pendentes = new Set();   // toques que ainda estao pegando a imagem (com flash, o sensor demora)
// Fotos pegas que ainda estao sendo salvas, uma por vez, na ordem dos toques. Cada uma ja aparece na lista do relatorio
// no toque ("Salvando…"); fechar a camera nao espera a fila (ela continua aqui e termina sozinha).
const fila = [];
const guardadas = new Map();   // id -> foto que falhou ao salvar (para "Tentar de novo" enquanto esta na memoria)
let processando = false, maximoNaFila = 6, contador = null, relogioSalvas = 0, ultimoId = 0;
const PRAZO_DO_SENSOR = 4000;  // com flash, se o Android nao registrar nesse tempo, vale o quadro do toque
let gx = 0, gy = 0;            // gravidade (celular deitado com a rotacao automatica desligada)
function aoMover(e) { const g = e.accelerationIncludingGravity; if (g && g.x != null) { gx = g.x; gy = g.y; } }

export async function iniciar(elemento, usarFrente) {
  await esperarPendentes(); // trocar de camera nao corta uma foto no meio
  parar();
  video = elemento;
  frente = !!usarFrente;
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    return { ok: false, erro: "Este aparelho não deixa o app usar a câmera." };
  }
  try {
    stream = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: { facingMode: frente ? "user" : { ideal: "environment" }, width: { ideal: 4096 }, height: { ideal: 3072 } }
    });
  } catch (e) {
    const negado = e && (e.name === "NotAllowedError" || e.name === "SecurityError");
    return { ok: false, erro: negado ? "Sem permissão para usar a câmera. Libere nas configurações do Android." : "Não foi possível abrir a câmera (" + (e && e.name || "erro") + ")." };
  }
  track = stream.getVideoTracks()[0];
  video.srcObject = stream;
  video.setAttribute("playsinline", "");
  video.muted = true;
  try { await video.play(); } catch (e) { /* alguns aparelhos so tocam depois do primeiro quadro */ }
  captura = (typeof ImageCapture !== "undefined" && track) ? new ImageCapture(track) : null;
  let temLanterna = false, flashes = [];
  try {
    const cap = track.getCapabilities ? track.getCapabilities() : {};
    temLanterna = !!cap.torch;
    if (captura) {
      const foto = await captura.getPhotoCapabilities();
      flashes = (foto && foto.fillLightMode) || [];
    }
  } catch (e) { /* sem informacao: segue sem flash */ }
  if (flash === "lanterna") lanterna(true);
  return { ok: true, lanterna: temLanterna, flash: flashes.indexOf("flash") >= 0 };
}

// Liga o botao de disparo direto aqui (o toque nao passa pelo .NET antes de pegar a foto).
export function ligarDisparo(elBotao, elMiniatura, elVisor, elAviso, ref, elContador, maximo) {
  botao = elBotao; miniatura = elMiniatura; visor = elVisor; aviso = elAviso; dotnet = ref;
  contador = elContador || null;
  maximoNaFila = maximo > 0 ? maximo : 6;
  botao.addEventListener("click", tocar);
  window.addEventListener("devicemotion", aoMover);
  atualizarContador();
}

function tocar() {
  if (fila.length >= maximoNaFila) return; // fila cheia: o botao fica em "Aguarde…" ate uma terminar
  const p = disparar();
  pendentes.add(p);
  p.finally(() => pendentes.delete(p));
}

async function esperarPendentes() {
  while (pendentes.size) await Promise.allSettled([...pendentes]);
}

// Saiu da camera (Pronto, fechar ou voltar): o toque que ainda pega a imagem termina; as fotos ja pegas continuam
// salvando na fila depois que a camera desliga.
export async function encerrar() {
  if (botao) botao.removeEventListener("click", tocar);
  window.removeEventListener("devicemotion", aoMover);
  botao = null;
  contador = null;
  await esperarPendentes();
  parar();
}

// proporcao: largura/altura com o celular deitado (4/3, 16/9, 1). 0 = tela cheia (formato do visor).
export function configurar(proporcao, modoFlash) {
  const mudouFlash = modoFlash !== flash;
  flash = modoFlash;
  alvo = formatoDoVisor(proporcao);
  if (mudouFlash) lanterna(flash === "lanterna");
  return alvo;
}

export async function lanterna(ligar) {
  if (!track) return false;
  try { await track.applyConstraints({ advanced: [{ torch: !!ligar }] }); return true; } catch (e) { return false; }
}

function retrato() { return window.innerHeight >= window.innerWidth; }

export function formatoDoVisor(proporcao) {
  if (proporcao > 0) return retrato() ? 1 / proporcao : proporcao;
  const r = (visor || video).getBoundingClientRect();
  return r.width / Math.max(1, r.height);
}

function piscar() {
  const f = document.querySelector(".camera .flashou");
  if (!f) return;
  f.classList.remove("on"); void f.offsetWidth; f.classList.add("on");
  setTimeout(() => f.classList.remove("on"), 140);
}

function segurar(sim) {
  if (aviso) aviso.hidden = !sim;
  if (botao) { botao.classList.toggle("esperando", sim); botao.setAttribute("aria-busy", sim ? "true" : "false"); }
}

// Tela em pe, mas o celular deitado (rotacao automatica desligada): a foto e girada para sair na horizontal.
// 1 = topo do celular para a esquerda, -1 = para a direita, 0 = em pe.
function deitado() {
  if (!retrato() || frente) return 0;
  if (Math.abs(gx) > 6 && Math.abs(gx) > Math.abs(gy) * 1.5) return gx > 0 ? 1 : -1;
  return 0;
}

function prazo(promessa, ms) {
  return Promise.race([promessa, new Promise((_, nao) => setTimeout(() => nao(new Error("demorou")), ms))]);
}

async function disparar() {
  if (!video || !video.videoWidth || fotografando) return; // durante o "Segure firme" o botao fica esperando
  const inicio = performance.now();
  const usarSensor = captura && (flash === "auto" || flash === "ligado");
  const lado = deitado();
  // O instante do toque fica guardado sempre: e a foto (sem flash) ou a reserva (se o sensor falhar ou demorar).
  let quadro = recortar(video, video.videoWidth, video.videoHeight, false, lado);
  if (usarSensor) {
    // Com flash a foto precisa do sensor: o Android ajusta foco, luz e flash antes de registrar.
    fotografando = true;
    segurar(true);
    try {
      const blob = await prazo(captura.takePhoto({ fillLightMode: flash === "ligado" ? "flash" : "auto" }), PRAZO_DO_SENSOR);
      const img = await createImageBitmap(blob);
      const girar = (video.videoHeight > video.videoWidth) !== (img.height > img.width);
      quadro = recortar(img, img.width, img.height, girar, lado);
    } catch (e) {
      // fica o quadro do toque
    } finally {
      fotografando = false;
      segurar(false);
    }
  }
  const tempoQuadro = Math.round(performance.now() - inicio);
  window.__spobrasTempoDoQuadro = tempoQuadro; // do toque ate a imagem pega (conferido nos testes)
  piscar();
  // O lugar da foto na lista aparece agora, com a imagem pequena do visor; a foto inteira vai salvando na fila.
  const id = novoId();
  const previa = reduzir(quadro, 240);
  if (miniatura) { miniatura.src = previa; miniatura.hidden = false; }
  try { dotnet.invokeMethodAsync("FotoPega", id, previa); } catch (e) { /* a foto continua na fila */ }
  fila.push({ id, quadro, tempoQuadro });
  atualizarContador();
  processar();
}

function novoId() {
  ultimoId = Math.max(ultimoId + 1, Date.now() * 10); // unico mesmo entre aberturas da camera
  return ultimoId;
}

// Copia pequena (para a lista e a miniatura da camera): leve e rapida.
function reduzir(fonte, lado) {
  const escala = Math.min(1, lado / Math.max(fonte.width, fonte.height));
  const c = document.createElement("canvas");
  c.width = Math.max(1, Math.round(fonte.width * escala));
  c.height = Math.max(1, Math.round(fonte.height * escala));
  c.getContext("2d").drawImage(fonte, 0, 0, c.width, c.height);
  return c.toDataURL("image/jpeg", 0.7);
}

async function processar() {
  if (processando) return;
  processando = true;
  try {
    while (fila.length) {
      await salvar(fila[0]);
      fila.shift();
      atualizarContador();
    }
  } finally {
    processando = false;
  }
}

async function salvar(item) {
  const ref = dotnet;
  try {
    const t0 = performance.now();
    const blob = await new Promise((ok, erro) => item.quadro.toBlob(b => b ? ok(b) : erro(new Error("o celular não conseguiu gerar a imagem")), "image/jpeg", 0.88));
    const bytes = new Uint8Array(await blob.arrayBuffer());
    const t1 = performance.now();
    await ref.invokeMethodAsync("FotoTirada", item.id, bytes, item.tempoQuadro, Math.round(t1 - t0));
    try { ref.invokeMethodAsync("Enviado", item.id, Math.round(performance.now() - t1)); } catch (e) { }
  } catch (e) {
    guardadas.set(item.id, item);
    try { await ref.invokeMethodAsync("FalhaAoTirar", item.id, String(e && e.message || e)); } catch (x) { }
  }
}

function atualizarContador() {
  const n = fila.length;
  if (botao) {
    const cheia = n >= maximoNaFila;
    botao.classList.toggle("cheia", cheia);
    botao.setAttribute("aria-label", cheia ? "Aguarde, fotos salvando" : "Tirar foto");
  }
  if (!contador) return;
  clearTimeout(relogioSalvas);
  if (n > 0) {
    contador.textContent = n >= maximoNaFila ? `Aguarde… ${n} salvando` : `Salvando ${n}…`;
    contador.classList.remove("ok");
    contador.style.display = "";
  } else if (contador.dataset.teve === "1") {
    contador.textContent = "✓ Salvas";
    contador.classList.add("ok");
    contador.style.display = "";
    relogioSalvas = setTimeout(() => { if (contador) contador.style.display = "none"; }, 1000);
  } else {
    contador.style.display = "none";
  }
  if (n > 0) contador.dataset.teve = "1";
}

// "Tentar de novo" na lista: a foto que falhou volta para a fila (so enquanto ainda esta na memoria).
export function tentarDeNovo(id, ref) {
  const item = guardadas.get(id);
  if (!item) return false;
  guardadas.delete(id);
  if (ref) dotnet = ref;
  fila.push(item);
  atualizarContador();
  processar();
  return true;
}

export function esquecer(id) { guardadas.delete(id); }

// Recorte central no formato do visor, com o maior lado limitado (o relatorio usa ate 1600 px).
function recortar(fonte, largura, altura, girar, lado) {
  let w = girar ? altura : largura, h = girar ? largura : altura;
  let cw = w, ch = h;
  if (w / h > alvo) cw = Math.round(h * alvo); else ch = Math.round(w / alvo);
  const maximo = 2400, escala = Math.min(1, maximo / Math.max(cw, ch));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(cw * escala);
  canvas.height = Math.round(ch * escala);
  const ctx = canvas.getContext("2d");
  ctx.save();
  ctx.scale(escala, escala);
  ctx.translate(-(w - cw) / 2, -(h - ch) / 2);
  if (girar) { ctx.translate(w, 0); ctx.rotate(Math.PI / 2); }
  if (frente) { ctx.translate(girar ? altura : w, 0); ctx.scale(-1, 1); }
  ctx.drawImage(fonte, 0, 0);
  ctx.restore();
  if (!lado) return canvas;
  // celular deitado com a tela em pe: gira a foto para a horizontal
  const deitada = document.createElement("canvas");
  deitada.width = canvas.height;
  deitada.height = canvas.width;
  const d = deitada.getContext("2d");
  if (lado > 0) { d.translate(0, deitada.height); d.rotate(-Math.PI / 2); }
  else { d.translate(deitada.width, 0); d.rotate(Math.PI / 2); }
  d.drawImage(canvas, 0, 0);
  return deitada;
}

export function emAndamento() { return fila.length + (fotografando ? 1 : 0); }

export function desligar() {
  if (botao) botao.removeEventListener("click", tocar);
  window.removeEventListener("devicemotion", aoMover);
  botao = null;
  parar();
}

export function parar() {
  if (stream) stream.getTracks().forEach(t => t.stop());
  stream = null; track = null; captura = null;
  if (video) video.srcObject = null;
}

// Rola o elemento ate a vista (usado nas listas longas).
export function rolarAte(el) { if (el) el.scrollIntoView({ block: "nearest" }); }
