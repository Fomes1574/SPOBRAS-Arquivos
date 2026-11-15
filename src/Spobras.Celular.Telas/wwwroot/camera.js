// Camera do app (dentro da tela). A foto e pega no proprio toque, aqui no navegador, sem esperar o .NET:
// o que estava no visor no instante do toque e o que fica. So depois a tela pisca e a foto vai para o rascunho.
let stream = null, track = null, captura = null, video = null, frente = false;
let alvo = 4 / 3, flash = "auto", dotnet = null, botao = null, miniatura = null, visor = null, aviso = null;
let fotografando = false, salvando = 0;

export async function iniciar(elemento, usarFrente) {
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
export function ligarDisparo(elBotao, elMiniatura, elVisor, elAviso, ref) {
  botao = elBotao; miniatura = elMiniatura; visor = elVisor; aviso = elAviso; dotnet = ref;
  botao.addEventListener("click", disparar);
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

function segurar(sim) { if (aviso) aviso.hidden = !sim; }

async function disparar() {
  if (!video || !video.videoWidth || fotografando) return;
  const inicio = performance.now();
  const usarSensor = captura && (flash === "auto" || flash === "ligado");
  let quadro;
  if (usarSensor) {
    // Com flash a foto precisa do sensor: o Android ajusta foco, luz e flash antes de registrar.
    fotografando = true;
    segurar(true);
    try {
      const blob = await captura.takePhoto({ fillLightMode: flash === "ligado" ? "flash" : "auto" });
      const img = await createImageBitmap(blob);
      const girar = (video.videoHeight > video.videoWidth) !== (img.height > img.width);
      quadro = recortar(img, img.width, img.height, girar);
    } catch (e) {
      quadro = recortar(video, video.videoWidth, video.videoHeight, false);
    } finally {
      fotografando = false;
      segurar(false);
    }
  } else {
    quadro = recortar(video, video.videoWidth, video.videoHeight, false); // o instante do toque
  }
  window.__spobrasTempoDoQuadro = performance.now() - inicio; // do toque ate a imagem pega (conferido nos testes)
  piscar();
  salvando++;
  try {
    const blob = await new Promise(r => quadro.toBlob(r, "image/jpeg", 0.88));
    if (miniatura) {
      const antigo = miniatura.dataset.url;
      miniatura.dataset.url = URL.createObjectURL(blob);
      miniatura.src = miniatura.dataset.url;
      miniatura.hidden = false;
      if (antigo) URL.revokeObjectURL(antigo);
    }
    const bytes = new Uint8Array(await blob.arrayBuffer());
    await dotnet.invokeMethodAsync("FotoTirada", bytes);
  } catch (e) {
    try { await dotnet.invokeMethodAsync("FalhaAoTirar", String(e && e.message || e)); } catch (x) { }
  } finally {
    salvando--;
  }
}

// Recorte central no formato do visor, com o maior lado limitado (o relatorio usa ate 1600 px).
function recortar(fonte, largura, altura, girar) {
  let w = girar ? altura : largura, h = girar ? largura : altura;
  let cw = w, ch = h;
  if (w / h > alvo) cw = Math.round(h * alvo); else ch = Math.round(w / alvo);
  const lado = 2400, escala = Math.min(1, lado / Math.max(cw, ch));
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
  return canvas;
}

export function emAndamento() { return salvando + (fotografando ? 1 : 0); }

export function desligar() {
  if (botao) botao.removeEventListener("click", disparar);
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
