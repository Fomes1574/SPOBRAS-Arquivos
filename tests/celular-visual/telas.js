// Teste visual do app do celular no host de navegador (tests/Spobras.Celular.Navegador).
// Uso: node telas.js <pasta-das-imagens> <url-pronto> [url-novo] [url-movido] [url-semrede]
// Abre cada tela em dois tamanhos de celular, confere se algo sai da tela, texto cortado, botao pequeno,
// erro de script, e salva uma imagem de cada tela. Termina com codigo 1 se achar problema.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const [, , pasta, urlPronto, urlNovo, urlMovido, urlSemRede] = process.argv;
fs.mkdirSync(pasta, { recursive: true });
const problemas = [];

async function conferir(page, nome, tamanho) {
  const r = await page.evaluate(() => {
    const out = [];
    const app = document.querySelector('.app');
    if (!app) return ['tela sem .app'];
    const ab = app.getBoundingClientRect();
    app.querySelectorAll('*').forEach(el => {
      const b = el.getBoundingClientRect();
      if (b.width === 0 || b.height === 0) return;
      const st = getComputedStyle(el);
      if (st.visibility === 'hidden' || st.display === 'none') return;
      const rolavel = el.closest('.chips') || el.closest('.pagina-previa');
      if (!rolavel && (b.right > ab.right + 1 || b.left < ab.left - 1)) out.push('fora da largura: ' + (el.className || el.tagName) + ' "' + (el.textContent || '').trim().slice(0, 40) + '"');
      if (el.children.length === 0 && el.scrollWidth > el.clientWidth + 1 && st.overflow !== 'visible' && st.textOverflow !== 'ellipsis' && !rolavel && el.tagName !== 'INPUT' && el.tagName !== 'TEXTAREA')
        out.push('texto cortado: "' + el.textContent.trim().slice(0, 40) + '"');
      const toque = el.tagName === 'BUTTON' || (el.tagName === 'A' && (el.classList.contains('btn') || el.closest('.nav')));
      if (toque && b.height < 36 && !el.closest('.pagina-previa')) out.push('alvo de toque baixo (' + Math.round(b.height) + 'px): "' + el.textContent.trim().slice(0, 30) + '"');
    });
    if (document.querySelectorAll('.nav').length > 1) out.push('mais de uma barra de navegacao');
    const conteudo = document.querySelector('.conteudo');
    if (conteudo && conteudo.scrollWidth > conteudo.clientWidth + 1) out.push('rolagem horizontal na tela');
    const titulo = document.querySelector('.barra h1');
    if (titulo) {
      const t = titulo.getBoundingClientRect();
      const no = document.elementFromPoint(t.left + 5, t.top + 8);
      if (no && !titulo.contains(no) && !no.closest('.veu') && !no.closest('.folha') && !no.closest('.ocupado')) out.push('titulo coberto por ' + (no.className || no.tagName));
    }
    if (document.body.innerText.includes('An unhandled error') || document.querySelector('#blazor-error-ui[style*="block"]')) out.push('erro do Blazor na tela');
    return out;
  });
  r.forEach(x => problemas.push(`${tamanho} ${nome}: ${x}`));
}

async function pronto(page) {
  await page.waitForSelector('.app', { timeout: 15000 });
  await page.waitForTimeout(350);
}

async function abrir(browser, url, w, h) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: 1, permissions: ['camera'] });
  const page = await ctx.newPage();
  page.on('pageerror', e => problemas.push(`${w}x${h} erro de script: ${e.message}`));
  page.on('console', m => { if (m.type() === 'error' && !/favicon/.test(m.text())) problemas.push(`${w}x${h} console: ${m.text().slice(0, 160)}`); });
  await page.goto(url);
  await pronto(page);
  return page;
}

async function foto(page, nome, w, h, salvar) {
  await conferir(page, nome, `${w}x${h}`);
  if (salvar) await page.screenshot({ path: path.join(pasta, `${nome}.png`) });
}

async function ir(page, url, rota) {
  await page.evaluate(r => { Blazor.navigateTo(r); }, rota);
  await page.waitForTimeout(500);
}

async function texto(page, seletor, contem) {
  await page.locator(seletor, { hasText: contem }).first().click();
  await page.waitForTimeout(450);
}

(async () => {
  const browser = await chromium.launch({ args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] });
  for (const [w, h] of [[360, 760], [412, 915]]) {
    const salvar = w === 412;
    const page = await abrir(browser, urlPronto, w, h);
    await foto(page, '03-inicio', w, h, salvar);
    await ir(page, urlPronto, 'base'); await foto(page, '04-base', w, h, salvar);
    await texto(page, '.chip', 'Lote'); await foto(page, '05-base-filtro', w, h, salvar);
    await texto(page, '.opcao', 'Lote 216'); await foto(page, '06-base-lote', w, h, salvar);
    await texto(page, 'a.obra', 'EMEI Exemplo Alfa'); await foto(page, '07-ficha', w, h, salvar);
    await page.locator('.conteudo').evaluate(e => e.scrollTop = e.scrollHeight); await page.waitForTimeout(200);
    await foto(page, '08-ficha-2', w, h, salvar);
    await texto(page, 'a.obra', '007/EX/2022'); await foto(page, '09-ficha-concluida', w, h, salvar);
    // limpa o filtro de lote para os proximos
    await ir(page, urlPronto, 'base'); await texto(page, '.chip.on', 'Lote');
    if (await page.locator('a.obra', { hasText: 'Zeta' }).count() > 0) problemas.push('obra sob gestao da SIURB aparece na lista');

    await ir(page, urlPronto, 'relatorio'); await foto(page, '10-rel-dados', w, h, salvar);
    for (const rotulo of ['Unidade educacional', 'Contrato', 'Data do relatório', 'Processo SEI', 'Data da OS', 'Objeto', 'Descrição geral']) {
      if (await page.getByLabel(rotulo, { exact: true }).count() !== 1)
        problemas.push(`${w}x${h} rotulo do relatorio incorreto: ${rotulo}`);
    }
    await page.fill('input >> nth=1', ''); await page.locator('input >> nth=1').dispatchEvent('change'); await page.waitForTimeout(250);
    await page.fill('input >> nth=0', 'emei exemplo alfa'); await page.locator('input >> nth=0').dispatchEvent('change'); await page.waitForTimeout(450);
    await foto(page, '11-rel-escolha', w, h, salvar);
    await texto(page, '.opcao', '044/EX/2026');
    if ((await page.inputValue('input >> nth=1')) !== '044/EX/2026') problemas.push(`${w}x${h} escolher o contrato nao preencheu o campo`);

    await ir(page, urlPronto, 'relatorio/fotos'); await foto(page, '12-rel-fotos', w, h, salvar);
    await page.locator('button.foto').first().click(); await page.waitForTimeout(400);
    await foto(page, '15-rel-legenda', w, h, salvar);
    await page.locator('.folha .btn.primario').click(); await page.waitForTimeout(300);

    const antes = await page.locator('button.foto').count();
    await ir(page, urlPronto, 'relatorio/camera'); await page.waitForTimeout(1500);
    await foto(page, '13-rel-camera', w, h, salvar);
    await page.locator('.disparo').click(); await page.waitForTimeout(1500);
    await page.locator('.topo-cam .bt-cam', { hasText: '⚡' }).click(); await page.waitForTimeout(400);
    await foto(page, '14-rel-camera-opcoes', w, h, salvar);
    await page.locator('.folha .btn.primario').click(); await page.waitForTimeout(300);
    await ir(page, urlPronto, 'relatorio/fotos');
    const depois = await page.locator('button.foto').count();
    if (depois !== antes + 1) problemas.push(`${w}x${h} a foto da camera nao entrou no rascunho (${antes} -> ${depois})`);
    // tira a foto da camera de novo, para o relatorio ficar igual nos dois tamanhos
    await page.locator('button.foto').last().click(); await page.waitForTimeout(300);
    await texto(page, '.folha .link', 'Tirar esta foto'); await texto(page, '.folha .btn', 'Tirar do relatório');
    await page.keyboard.press('Escape');
    await ir(page, urlPronto, 'relatorio/conferir'); await page.waitForTimeout(600);
    await foto(page, '16-rel-conferir', w, h, salvar);
    await texto(page, '.btn.primario', 'Gerar relatório'); await page.waitForSelector('text=Pronto!', { timeout: 60000 });
    await foto(page, '17-rel-pronto', w, h, salvar);

    await ir(page, urlPronto, 'pdf'); await foto(page, '18-pdf', w, h, salvar);
    await ir(page, urlPronto, 'pdf/juntar'); await texto(page, '.btn', 'Escolher PDFs'); await foto(page, '19-pdf-juntar', w, h, salvar);
    await texto(page, '.btn.primario', 'Juntar'); await page.waitForSelector('text=Pronto!', { timeout: 30000 });
    await foto(page, '22-pdf-pronto', w, h, salvar);
    await ir(page, urlPronto, 'pdf/separar'); await texto(page, '.btn', 'Escolher o PDF');
    await page.fill('input', '1'); await page.locator('input').dispatchEvent('change'); await page.waitForTimeout(250);
    await foto(page, '20-pdf-separar', w, h, salvar);
    await ir(page, urlPronto, 'pdf/numerar'); await foto(page, '21-pdf-numerar', w, h, salvar);
    await ir(page, urlPronto, 'config'); await foto(page, '23-config', w, h, salvar);
    await page.context().close();

    for (const [url, nome] of [[urlNovo, '01-primeira'], [urlMovido, '25-movido'], [urlSemRede, '24-sem-internet']]) {
      if (!url) continue;
      const p = await abrir(browser, url, w, h);
      await foto(p, nome, w, h, salvar);
      await p.context().close();
    }
  }
  await browser.close();
  console.log(problemas.length ? problemas.join('\n') : 'nenhum problema');
  process.exit(problemas.length ? 1 : 0);
})();
