// Camera do app (dentro da tela, sem abrir outro app). Formato da foto, flash e lanterna.
// Usa getUserMedia e, quando o aparelho permite, ImageCapture.takePhoto (foto na resolucao do sensor e flash).
let stream = null, track = null, captura = null, video = null, frente = false;

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
  return { ok: true, lanterna: temLanterna, flash: flashes.indexOf("flash") >= 0 };
}

export async function lanterna(ligar) {
  if (!track) return false;
  try { await track.applyConstraints({ advanced: [{ torch: !!ligar }] }); return true; } catch (e) { return false; }
}

function retrato() { return window.innerHeight >= window.innerWidth; }

// proporcao: largura/altura com o celular deitado (4/3, 16/9, 1). 0 = tela cheia (formato do visor).
export function formatoDoVisor(proporcao, visor) {
  if (proporcao > 0) return retrato() ? 1 / proporcao : proporcao;
  const r = visor.getBoundingClientRect();
  return r.width / Math.max(1, r.height);
}

async function quadro(flash) {
  // Foto do sensor (melhor qualidade e com flash) quando der; senao, o quadro do video.
  if (captura && flash !== "lanterna") {
    try {
      const modo = flash === "ligado" ? "flash" : flash === "auto" ? "auto" : "off";
      const blob = await captura.takePhoto({ fillLightMode: modo });
      const img = await createImageBitmap(blob);
      const videoRetrato = video.videoHeight > video.videoWidth;
      const imgRetrato = img.height > img.width;
      return { fonte: img, largura: img.width, altura: img.height, girar: videoRetrato !== imgRetrato };
    } catch (e) { /* cai para o quadro do video */ }
  }
  return { fonte: video, largura: video.videoWidth, altura: video.videoHeight, girar: false };
}

// Tira a foto ja recortada no formato escolhido. Devolve os bytes do JPEG.
export async function capturar(alvo, flash) {
  if (!video || !video.videoWidth) throw new Error("A câmera ainda não está pronta.");
  const q = await quadro(flash);
  let w = q.girar ? q.altura : q.largura, h = q.girar ? q.largura : q.altura;
  // recorte central no formato do visor
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
  if (q.girar) { ctx.translate(w, 0); ctx.rotate(Math.PI / 2); }
  if (frente) { ctx.translate(q.girar ? q.largura : w, 0); ctx.scale(-1, 1); }
  ctx.drawImage(q.fonte, 0, 0);
  ctx.restore();
  const blob = await new Promise(r => canvas.toBlob(r, "image/jpeg", 0.88));
  return new Uint8Array(await blob.arrayBuffer());
}

export function parar() {
  if (stream) stream.getTracks().forEach(t => t.stop());
  stream = null; track = null; captura = null;
  if (video) video.srcObject = null;
}

// Rola o elemento ate a vista (usado nas listas longas).
export function rolarAte(el) { if (el) el.scrollIntoView({ block: "nearest" }); }
