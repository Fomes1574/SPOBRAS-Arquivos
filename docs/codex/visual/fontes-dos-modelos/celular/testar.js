const { chromium } = require('playwright'); const fs = require('fs');
const telas = fs.readdirSync(__dirname).filter(f => /^\d-.*\.html$/.test(f)).sort();
const tamanhos = [[360, 760], [412, 915]];
(async () => {
  const b = await chromium.launch(); const resultado = [];
  for (const [w, h] of tamanhos) for (const t of telas) {
    const p = await b.newPage({ viewport: { width: w, height: h }, deviceScaleFactor: 2 });
    await p.goto('file://' + __dirname + '/' + t); await p.waitForTimeout(150);
    const r = await p.evaluate(() => {
      const prob = [], visiveis = [...document.querySelectorAll('body *')].filter(e => { const s = getComputedStyle(e); return s.display !== 'none' && s.visibility !== 'hidden' && e.getClientRects().length; });
      if (document.documentElement.scrollWidth > innerWidth + 1) prob.push('a página vaza para o lado');
      const rolaveisPorDesenho = new Set([...document.querySelectorAll('.chips')]);
      for (const e of document.querySelectorAll('.v, b, h2, h3, .chip, .selo, .r, .tipo, .sub, .mini')) {
        if (e.scrollWidth > e.clientWidth + 1 && getComputedStyle(e).overflow !== 'visible') prob.push('texto cortado: ' + e.textContent.trim().slice(0, 40));
        const pai = e.closest('.cartao, .folha, .tiles > div, .datas > div');
        if (pai && !rolaveisPorDesenho.has(e.parentElement)) { const a = e.getBoundingClientRect(), q = pai.getBoundingClientRect(); if (a.right > q.right + 1 || a.left < q.left - 1) prob.push('sai da caixa: ' + e.textContent.trim().slice(0, 40)); }
      }
      for (const e of document.querySelectorAll('.btn, .chip, .selo')) {
        const tops = []; for (const n of e.childNodes) if (n.nodeType === 3 && n.textContent.trim()) { const rg = document.createRange(); rg.selectNodeContents(n); for (const x of rg.getClientRects()) if (x.width > 1) tops.push(x.top); }
        const meio = parseFloat(getComputedStyle(e).fontSize) * 0.6; const linhas = tops.sort((a, b) => a - b).filter((t, i, v) => i === 0 || t - v[i - 1] > meio).length;
        if (linhas > 1) prob.push('botão com texto em duas linhas: ' + e.textContent.trim()); }
      for (const e of document.querySelectorAll('.tiles .v, .datas .v')) { const lh = parseFloat(getComputedStyle(e).lineHeight) || 20; if (e.getBoundingClientRect().height > lh * 1.5) prob.push('valor quebrou em duas linhas: ' + e.textContent.trim()); }
      const folha = document.querySelector('.folha');
      for (const e of document.querySelectorAll('[role=button], .chip, .op')) {
        const a = e.getBoundingClientRect(); if (!a.width) continue;
        if (a.height < 44 - 0.5 || a.width < 44 - 0.5) prob.push(`toque pequeno (${Math.round(a.width)}x${Math.round(a.height)}): ` + (e.getAttribute('aria-label') || e.textContent.trim().slice(0, 30)));
        if (folha && !folha.contains(e)) continue;
        if (a.top < 0 || a.bottom > innerHeight || e.closest('.chips') && (a.right > innerWidth || a.left < 0)) continue;
        const x = a.left + a.width / 2, y = a.top + a.height / 2, el = document.elementFromPoint(x, y);
        if (el && el !== e && !e.contains(el) && !el.contains(e)) prob.push('botão coberto: ' + e.textContent.trim().slice(0, 30));
      }
      const lum = c => { const m = c.match(/[\d.]+/g).map(Number); const f = v => { v /= 255; return v <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; }; return .2126 * f(m[0]) + .7152 * f(m[1]) + .0722 * f(m[2]); };
      const fundo = e => { for (let n = e; n; n = n.parentElement) { const c = getComputedStyle(n).backgroundColor; if (c && !c.startsWith('rgba(0, 0, 0, 0)') && c !== 'transparent') return c; } return 'rgb(255,255,255)'; };
      let menorLetra = 99, menorContraste = 99;
      for (const e of visiveis) {
        const txt = [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim()); if (!txt) continue;
        const s = getComputedStyle(e), tam = parseFloat(s.fontSize); menorLetra = Math.min(menorLetra, tam);
        if (tam < 12) prob.push(`letra pequena (${tam}px): ` + e.textContent.trim().slice(0, 30));
        if (e.closest('.veu')) continue;
        const L1 = lum(s.color), L2 = lum(fundo(e)), c = (Math.max(L1, L2) + .05) / (Math.min(L1, L2) + .05);
        const grande = tam >= 18 || (tam >= 14 && parseInt(s.fontWeight) >= 700);
        if (!folha || folha.contains(e)) { menorContraste = Math.min(menorContraste, c); if (c < (grande ? 3 : 4.5)) prob.push(`contraste baixo (${c.toFixed(2)}): ` + e.textContent.trim().slice(0, 30)); }
      }
      return { prob: [...new Set(prob)], menorLetra, menorContraste: +menorContraste.toFixed(2) };
    });
    await p.screenshot({ path: `${__dirname}/v-${t.replace('.html', '')}-${w}.png` });
    // tela inteira (sem cortar a rolagem) para o caderno
    await p.addStyleTag({ content: '.app{height:auto!important}.conteudo{overflow:visible!important}html,body{height:auto!important}.veu,.folha{position:absolute!important}' });
    await p.screenshot({ path: `${__dirname}/c-${t.replace('.html', '')}-${w}.png`, fullPage: true });
    resultado.push({ tela: t.replace('.html', ''), largura: w, ...r });
    await p.close();
  }
  fs.writeFileSync(__dirname + '/resultado.json', JSON.stringify(resultado, null, 1));
  for (const r of resultado) console.log(`${r.largura} ${r.tela.padEnd(24)} letra>=${r.menorLetra}px contraste>=${r.menorContraste} ${r.prob.length ? 'PROBLEMAS: ' + r.prob.join(' | ') : 'ok'}`);
  await b.close();
})();
