// Teste visual do app do celular no host de navegador (tests/Spobras.Celular.Navegador).
// Uso: node telas.js <pasta-das-imagens> <url-pronto> [url-novo] [url-movido] [url-semrede]
// Abre cada tela em dois tamanhos de celular, confere se algo sai da tela, texto cortado, botao pequeno,
// erro de script, e salva uma imagem de cada tela. Termina com codigo 1 se achar problema.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const [, , pasta, urlPronto, urlNovo, urlMovido, urlSemRede, urlColuna, urlGrande] = process.argv;
const tempos = [];
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
      const rolavel = el.closest('.chips') || el.closest('.pagina-previa') || el.closest('.previa');
      if (!rolavel && (b.right > ab.right + 1 || b.left < ab.left - 1)) out.push('fora da largura: ' + (el.className || el.tagName) + ' "' + (el.textContent || '').trim().slice(0, 40) + '"');
      if (el.children.length === 0 && el.scrollWidth > el.clientWidth + 1 && st.overflow !== 'visible' && st.textOverflow !== 'ellipsis' && !rolavel && el.tagName !== 'INPUT' && el.tagName !== 'TEXTAREA')
        out.push('texto cortado: "' + el.textContent.trim().slice(0, 40) + '"');
      const toque = el.tagName === 'BUTTON' || (el.tagName === 'A' && (el.classList.contains('btn') || el.closest('.nav')));
      if (toque && b.height < 36 && !el.closest('.pagina-previa')) out.push('alvo de toque baixo (' + Math.round(b.height) + 'px): "' + el.textContent.trim().slice(0, 30) + '"');
    });
    if (document.querySelectorAll('.nav').length > 1) out.push('mais de uma barra de navegacao');
    // botao de folha ou janela por cima de tudo nao pode estar coberto por outra coisa (ex.: aviso de desfazer)
    document.querySelectorAll('.folha button, .folha a.btn, .dialogo button').forEach(b => {
      const r = b.getBoundingClientRect();
      if (!r.width || !r.height || b.disabled) return;
      const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
      if (cy < 0 || cy > innerHeight) return; // fora da vista (a folha rola)
      const no = document.elementFromPoint(cx, cy);
      if (no && !b.contains(no) && !no.contains(b)) out.push('botao coberto: "' + b.textContent.trim().slice(0, 30) + '" por ' + (no.className || no.tagName));
    });
    const conteudo = document.querySelector('.conteudo');
    if (conteudo && conteudo.scrollWidth > conteudo.clientWidth + 1) out.push('rolagem horizontal na tela');
    const titulo = document.querySelector('.barra h1');
    if (titulo) {
      const t = titulo.getBoundingClientRect();
      const no = document.elementFromPoint(t.left + 5, t.top + 8);
      if (no && !titulo.contains(no) && !no.closest('.veu') && !no.closest('.folha') && !no.closest('.ocupado')) out.push('titulo coberto por ' + (no.className || no.tagName));
    }
    const alvosResumo = '.resumo-acao, .folha-resumo label.opcao, .escola-resumo > a';
    document.querySelectorAll(alvosResumo).forEach(el => {
      const r = el.getBoundingClientRect();
      if (!r.width || !r.height) return;
      if (r.width < 43.5 || r.height < 43.5) out.push('toque do resumo menor que 44 px: ' + el.textContent.trim());
      if (r.top < 0 || r.bottom > innerHeight || r.left < 0 || r.right > innerWidth || el.closest('.base-contratos') && document.querySelector('.folha')) return;
      const topo = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
      const area = el.closest('.conteudo');
      const janela = area?.getBoundingClientRect();
      if (topo && !el.contains(topo) && !topo.contains(el) && (!janela || r.top >= janela.top && r.bottom <= janela.bottom))
        out.push('alvo do resumo coberto: ' + el.textContent.trim());
    });
    const luminancia = cor => {
      const [r, g, b] = (cor.match(/[\d.]+/g) || []).map(Number).map(v => (v /= 255) <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4);
      return .2126 * r + .7152 * g + .0722 * b;
    };
    const fundo = el => {
      for (let atual = el; atual; atual = atual.parentElement) {
        const cor = getComputedStyle(atual).backgroundColor;
        if (cor !== 'transparent' && cor !== 'rgba(0, 0, 0, 0)') return cor;
      }
      return 'rgb(255, 255, 255)';
    };
    const contraste = (cor, fundo) => {
      const l = luminancia(cor), f = luminancia(fundo);
      return (Math.max(l, f) + .05) / (Math.min(l, f) + .05);
    };
    document.querySelectorAll('.base-contratos input[placeholder], .folha-resumo input[placeholder]').forEach(el => {
      const estilo = getComputedStyle(el, '::placeholder');
      if (parseFloat(estilo.fontSize) < 12 || contraste(estilo.color, fundo(el)) < 4.5)
        out.push('placeholder do resumo sem tamanho ou contraste: ' + el.placeholder);
    });
    document.querySelectorAll('.contrato-resumo *, .escolas-resumo *, .folha-resumo *, .resumo-salvo *, .resumo-acao, .selos-contrato *, .valores-resumo *, .pizza-resumo *').forEach(el => {
      if (!el.getClientRects().length) return;
      const nos = [...el.childNodes].filter(n => n.nodeType === Node.TEXT_NODE && n.textContent.trim());
      if (!nos.length) return;
      const estilo = getComputedStyle(el);
      if (parseFloat(estilo.fontSize) < 12) out.push('letra do resumo menor que 12 px: ' + el.textContent.trim());
      if (contraste(estilo.color, fundo(el)) < 4.5)
        out.push('contraste do resumo abaixo de 4,5: ' + el.textContent.trim());
      if (el.matches('.resumo-acao, .selo-contrato, .valores-resumo > div > b, .datas-resumo > div > b')) {
        const tops = new Set(nos.flatMap(no => {
          const range = document.createRange();
          range.selectNodeContents(no);
          return [...range.getClientRects()].map(r => Math.round(r.top));
        }));
        if (tops.size > 1) out.push('texto do resumo quebrou linha: ' + el.textContent.trim());
      }
    });
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

// Espera a tela chegar ao estado esperado; se nao chegar em 4 s, registra o problema.
async function esperar(page, condicao, arg, problema, ms = 4000) {
  try { await page.waitForFunction(condicao, arg, { timeout: ms }); } catch { problemas.push(problema); }
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
    const contratoChip = page.locator('.chips .chip:not(.limpar-chip)').first();
    const pesquisaContrato = page.getByRole('searchbox', { name: 'Pesquisar opções' });
    const fecharFiltro = () => page.getByRole('button', { name: 'Pronto', exact: true }).click();
    const limparContrato = () => page.getByRole('button', { name: 'Limpar Contrato', exact: true }).click();
    const marcarContrato = codigo => page.locator('.opcao-filtro', { hasText: codigo }).locator('input').check();

    await contratoChip.click();
    await pesquisaContrato.fill('216');
    await page.waitForFunction(() => document.querySelectorAll('.folha-resumo .opcao-filtro').length === 3);
    await foto(page, `resumo-1-filtro-${w}`, w, h, true);
    await marcarContrato('044/EX/2026');
    await fecharFiltro();
    await page.locator('.contrato-resumo h2', { hasText: '044/EX/2026' }).waitFor();
    await foto(page, `resumo-2-contrato-${w}`, w, h, true);
    await page.locator('.escolas-resumo h2').scrollIntoViewIfNeeded();
    await foto(page, `resumo-3-escolas-${w}`, w, h, true);
    await contratoChip.click();
    await pesquisaContrato.fill('239');
    await marcarContrato('060/EX/2026');
    await fecharFiltro();
    const exportarDois = page.getByRole('button', { name: 'Exportar PDF dos 2 contratos' });
    await exportarDois.waitFor();
    await foto(page, `resumo-4-dois-${w}`, w, h, true);
    await exportarDois.scrollIntoViewIfNeeded();
    await foto(page, `resumo-4b-exportar-dois-${w}`, w, h, true);
    await page.locator('.sem-pizza').scrollIntoViewIfNeeded();
    await foto(page, `resumo-6-nao-iniciado-${w}`, w, h, true);
    await limparContrato();
    await contratoChip.click();
    await pesquisaContrato.fill('007');
    await marcarContrato('007/EX/2022');
    await pesquisaContrato.fill('052/EX');
    await marcarContrato('052/EX/2026');
    await fecharFiltro();
    await page.locator('.selo-contrato', { hasText: 'Venceu há' }).waitFor();
    await page.getByText('R$ 99.999.999,99', { exact: true }).first().scrollIntoViewIfNeeded();
    await foto(page, `resumo-5b-beta-valor-e-nomes-${w}`, w, h, true);
    await page.locator('.selo-contrato', { hasText: 'Encerrado em' }).scrollIntoViewIfNeeded();
    await foto(page, `resumo-5-encerrado-${w}`, w, h, true);
    await limparContrato();
    await texto(page, 'a.obra', 'EMEI Exemplo Alfa');
    await foto(page, `resumo-7-ficha-${w}`, w, h, true);
    const exportarEscola = page.getByRole('button', { name: 'Exportar PDF desta escola' });
    await exportarEscola.evaluate(botao => { botao.click(); botao.click(); });
    await page.getByText('PDF salvo', { exact: true }).waitFor();
    await foto(page, `resumo-8-salvo-${w}`, w, h, true);
    await page.getByRole('link', { name: 'Voltar à Base', exact: true }).click();
    if (await page.locator('a.obra', { hasText: 'Zeta' }).count() > 0) problemas.push('obra sob gestao da SIURB aparece na lista');

    await ir(page, urlPronto, 'relatorio'); await foto(page, '10-rel-dados', w, h, salvar);
    for (const rotulo of ['Unidade educacional', 'Contrato', 'Data do relatório', 'Processo SEI', 'Data da OS', 'Objeto', 'Descrição geral']) {
      if (await page.getByLabel(rotulo, { exact: true }).count() !== 1)
        problemas.push(`${w}x${h} rotulo do relatorio incorreto: ${rotulo}`);
    }
    const unidade = page.getByLabel('Unidade educacional', { exact: true });
    await unidade.fill('exemplo alfa');
    const sugestao = page.locator('.sugestoes-rel button', { hasText: 'EMEI Exemplo Alfa' });
    await sugestao.waitFor();
    if (!await unidade.evaluate(e => e === document.activeElement)) problemas.push('sugestao apareceu so depois de sair do campo');
    await foto(page, '10b-rel-sugestoes', w, h, salvar);
    await sugestao.click();
    await foto(page, '11-rel-escolha', w, h, salvar);
    await texto(page, '.opcao', '044/EX/2026');
    if ((await page.inputValue('input >> nth=1')) !== '044/EX/2026') problemas.push(`${w}x${h} escolher o contrato nao preencheu o campo`);

    const lote = page.getByLabel('Lote', { exact: true });
    const apagarLote = page.getByRole('button', { name: 'Apagar Lote' });
    await apagarLote.click();
    await apagarLote.waitFor({ state: 'detached' });
    if (await lote.inputValue() !== '') problemas.push('a borracha nao apagou o lote');
    await foto(page, '11b-rel-lote-apagado', w, h, salvar);
    await page.getByRole('button', { name: 'Atualizar dados pela base' }).click();
    await apagarLote.waitFor();
    if (await lote.inputValue() !== 'LOTE 216') problemas.push('atualizar dados nao restaurou o lote');
    await ir(page, urlPronto, 'relatorio/fotos');
    // miniaturas sao feitas em segundo plano: a tela abre na hora e elas aparecem em seguida
    await page.waitForFunction(() => { const b = [...document.querySelectorAll('button[data-foto]')]; return b.length > 0 && b.every(x => x.querySelector('img')); }, null, { timeout: 15000 });
    await foto(page, '12-rel-fotos', w, h, salvar);
    const fotos = page.locator('button[data-foto]');
    const ultima = fotos.last();
    const legendaUltima = await ultima.locator('.leg').textContent();
    const miniaturaUltima = await ultima.locator('img').getAttribute('src');
    await ultima.scrollIntoViewIfNeeded();
    const inicioArraste = await ultima.boundingBox();
    await page.mouse.move(inicioArraste.x + inicioArraste.width / 2, inicioArraste.y + inicioArraste.height / 2);
    await page.mouse.down();
    await page.locator('.foto-flutuante').waitFor();
    const areaFotos = page.locator('.conteudo');
    const caixaArea = await areaFotos.boundingBox();
    await page.mouse.move(caixaArea.x + 60, caixaArea.y + 8, { steps: 12 });
    await page.waitForTimeout(500);
    const primeira = await fotos.first().evaluate(el => {
      const pai = el.offsetParent.getBoundingClientRect();
      return { x: pai.left + el.offsetLeft, y: pai.top + el.offsetTop, width: el.offsetWidth, height: el.offsetHeight };
    });
    await page.mouse.move(primeira.x + primeira.width / 2, primeira.y + primeira.height / 2, { steps: 10 });
    await page.mouse.up();
    await page.waitForFunction(({ legenda, miniatura }) => {
      const primeira = document.querySelector('button[data-foto]');
      return primeira?.querySelector('.leg')?.textContent === legenda &&
        primeira.querySelector('img')?.getAttribute('src') === miniatura &&
        !document.querySelector('.foto-flutuante') &&
        [...document.querySelectorAll('button[data-foto]')].every(f => getComputedStyle(f).transform === 'none');
    }, { legenda: legendaUltima, miniatura: miniaturaUltima });
    await foto(page, '12b-rel-fotos-reordenadas', w, h, salvar);
    await page.locator('button.foto').first().click(); await page.waitForTimeout(400);
    await foto(page, '15-rel-legenda', w, h, salvar);
    await page.locator('.folha .btn.primario').click(); await page.waitForTimeout(300);

    await page.getByRole('button', { name: 'Selecionar', exact: true }).click();
    await page.getByRole('button', { name: 'Marcar todas', exact: true }).waitFor();
    await fotos.first().click();
    await fotos.nth(1).click();
    const legendaEmLote = page.locator('.legenda-varias').getByLabel('Legenda', { exact: true });
    // texto diferente em cada largura: na segunda, as fotos ja tem o da primeira e o app diria que nada mudou
    await legendaEmLote.fill(`Vistoria em lote ${w}`);
    await foto(page, '15b-rel-legenda-varias', w, h, salvar);
    await page.getByRole('button', { name: 'Aplicar às 2 marcadas', exact: true }).click();
    await page.getByText('Texto aplicado a 2 fotos.', { exact: true }).waitFor();
    await page.waitForFunction(t => [...document.querySelectorAll('button[data-foto] .leg')].filter(e => e.textContent === t).length === 2, `Vistoria em lote ${w}`);
    // continua selecionando: marcacoes e campo limpos para o proximo grupo
    if (!(await page.getByText('0 marcadas', { exact: true }).isVisible())) problemas.push(`${w}x${h} depois de aplicar a legenda nao continuou no modo selecao`);
    await page.getByRole('button', { name: 'Marcar todas', exact: true }).click();
    await esperar(page, () => document.querySelectorAll('.foto-marcada').length === document.querySelectorAll('button[data-foto]').length, null, `${w}x${h} marcar todas nao marcou todas`);
    await page.getByRole('button', { name: 'Desmarcar todas', exact: true }).click();
    await esperar(page, () => document.querySelectorAll('.foto-marcada').length === 0 && document.body.innerText.includes('0 marcadas'), null, `${w}x${h} desmarcar todas nao desmarcou`);
    if (await page.locator('.fab').count() > 0) problemas.push(`${w}x${h} botao Tirar foto aparece no modo selecao`);
    // tirar varias com uma confirmacao e desfazer
    const total = await fotos.count();
    await fotos.nth(1).click(); await fotos.nth(2).click();
    await page.getByRole('button', { name: 'Tirar as 2 do relatório', exact: true }).click();
    await page.getByRole('dialog').waitFor();
    await foto(page, '15e-rel-tirar-varias', w, h, salvar);
    await page.getByRole('button', { name: 'Tirar 2', exact: true }).click();
    await page.locator('.desfazer').waitFor();
    if (await fotos.count() !== total - 2) problemas.push(`${w}x${h} tirar 2 fotos nao tirou`);
    if (!(await page.locator('.desfazer .txt').textContent()).includes('2 fotos removidas')) problemas.push(`${w}x${h} aviso de remover varias errado`);
    await page.getByRole('button', { name: 'Desfazer', exact: true }).click();
    await page.locator('.desfazer').waitFor({ state: 'detached' });
    if (await fotos.count() !== total) problemas.push(`${w}x${h} desfazer nao devolveu as 2 fotos`);
    // mover as marcadas para o fim (na ordem em que estavam) e desfazer; depois "Deitadas primeiro, em pe no fim"
    const ordem = () => page.evaluate(() => [...document.querySelectorAll('button[data-foto] img')].map(i => i.getAttribute('src')));
    const ordemAntes = await ordem();
    await page.getByRole('button', { name: 'Selecionar', exact: true }).click();
    await fotos.nth(0).click(); await fotos.nth(1).click();
    await page.getByRole('button', { name: 'Mover para o fim', exact: true }).click();
    await page.locator('.desfazer').waitFor();
    if (!(await page.locator('.desfazer .txt').textContent()).includes('2 fotos foram para o fim')) problemas.push(`${w}x${h} aviso de mover para o fim errado`);
    const ordemFim = await ordem();
    if (JSON.stringify(ordemFim.slice(-2)) !== JSON.stringify(ordemAntes.slice(0, 2)) || ordemFim.length !== ordemAntes.length) problemas.push(`${w}x${h} mover para o fim nao levou as marcadas, na ordem, para o fim`);
    if (await page.locator('.foto-marcada').count() !== 2) problemas.push(`${w}x${h} depois de mover, as fotos deixaram de estar marcadas`);
    await foto(page, '15i-rel-mover-fim', w, h, salvar);
    await page.getByRole('button', { name: 'Desfazer', exact: true }).click();
    await page.locator('.desfazer').waitFor({ state: 'detached' });
    if (JSON.stringify(await ordem()) !== JSON.stringify(ordemAntes)) problemas.push(`${w}x${h} desfazer nao voltou a ordem de antes`);
    await page.getByRole('button', { name: 'Cancelar', exact: true }).click();
    await page.getByRole('button', { name: 'Ordenar ▾', exact: true }).click();
    await page.getByRole('button', { name: 'Deitadas primeiro, em pé no fim', exact: true }).click();
    await page.waitForFunction(() => document.querySelector('.desfazer') || [...document.querySelectorAll('.aviso')].some(a => a.textContent.includes('deitadas primeiro')), null, { timeout: 5000 })
      .catch(() => problemas.push(`${w}x${h} deitadas primeiro nao respondeu`));
    if (await page.locator('.desfazer').count() > 0) {
      await page.getByRole('button', { name: 'Desfazer', exact: true }).click();
      await page.locator('.desfazer').waitFor({ state: 'detached' });
    }
    if (JSON.stringify(await ordem()) !== JSON.stringify(ordemAntes)) problemas.push(`${w}x${h} depois de desfazer o deitadas primeiro a ordem nao voltou`);
    const antes = await page.locator('button.foto').count();
    await ir(page, urlPronto, 'relatorio/camera'); await page.waitForTimeout(1500);
    await foto(page, '13-rel-camera', w, h, salvar);
    // flash desligado: a foto e o quadro do instante do toque
    await page.locator('.topo-cam .bt-cam', { hasText: '⚡' }).click();
    await page.locator('.folha .btn', { hasText: 'Desligado' }).click();
    await page.locator('.folha .btn.primario').click(); await page.waitForTimeout(300);
    const contaAntes = await page.locator('.pronto-cam .pronto-n').textContent();
    await page.locator('.disparo').click();
    await page.waitForFunction(t => document.querySelector('.pronto-cam .pronto-n').textContent !== t, contaAntes, { timeout: 5000 });
    const tempoQuadro = await page.evaluate(() => window.__spobrasTempoDoQuadro);
    if (!(tempoQuadro < 150)) problemas.push(`${w}x${h} do toque ate pegar a foto: ${tempoQuadro} ms`);
    if (!(await page.locator('.pronto-cam img.ultima').isVisible())) problemas.push(`${w}x${h} miniatura da ultima foto nao apareceu na camera`);
    // fotos seguidas
    await page.locator('.disparo').click(); await page.locator('.disparo').click();
    await page.waitForTimeout(1200);
    await foto(page, '13b-rel-camera-depois', w, h, salvar);
    await page.locator('.topo-cam .bt-cam', { hasText: '⚡' }).click(); await page.waitForTimeout(400);
    await foto(page, '14-rel-camera-opcoes', w, h, salvar);
    await page.locator('.folha .btn.primario').click(); await page.waitForTimeout(300);
    await ir(page, urlPronto, 'relatorio/fotos');
    const depois = await page.locator('button.foto').count();
    if (depois !== antes + 3) problemas.push(`${w}x${h} as 3 fotos da camera nao entraram no rascunho (${antes} -> ${depois})`);
    // tirar uma foto: sem confirmacao, com Desfazer por 5 s
    await page.locator('button.foto').last().click(); await page.waitForTimeout(300);
    await texto(page, '.folha .link', 'Tirar esta foto');
    await page.locator('.desfazer').waitFor();
    await foto(page, '15d-rel-desfazer', w, h, salvar);
    if (await page.locator('button.foto').count() !== antes + 2) problemas.push(`${w}x${h} tirar uma foto pediu confirmacao ou nao tirou`);
    await page.getByRole('button', { name: 'Desfazer', exact: true }).click();
    await esperar(page, n => document.querySelectorAll('button.foto').length === n && !document.querySelector('.desfazer'), antes + 3, `${w}x${h} desfazer nao devolveu a foto`);
    // tira as 3 da camera de novo (para o relatorio ficar igual nos dois tamanhos) e deixa o tempo acabar
    for (let k = 0; k < 3; k++) {
      const n = await page.locator('button.foto').count();
      await page.locator('button.foto').last().evaluate(e => e.scrollIntoView({ block: 'center' }));
      await page.locator('button.foto').last().click(); await page.waitForTimeout(400);
      if (k > 0) await conferir(page, 'folha com o aviso de desfazer', `${w}x${h}`);
      await texto(page, '.folha .link', 'Tirar esta foto');
      await esperar(page, n => document.querySelectorAll('button.foto').length === n - 1, n, `${w}x${h} tirar foto ${k + 1} nao tirou`);
    }
    await esperar(page, () => (document.querySelector('.desfazer .txt')?.textContent || '').includes('3 fotos removidas'), null, `${w}x${h} remocoes seguidas nao se juntaram: ` + (await page.locator(".desfazer .txt").textContent().catch(() => "sem aviso")));
    await page.waitForTimeout(5600);
    if (await page.locator('.desfazer').count() > 0) problemas.push(`${w}x${h} aviso de desfazer nao sumiu em 5 s`);
    if (await page.locator('button.foto').count() !== antes) problemas.push(`${w}x${h} fotos voltaram depois do tempo`);
    await page.getByRole('button', { name: 'Ordenar ▾', exact: true }).click();
    await page.getByRole('button', { name: 'Por nome do arquivo', exact: true }).waitFor();
    await foto(page, '15c-rel-ordenar-fotos', w, h, salvar);
    await page.getByRole('button', { name: 'Por data da foto', exact: true }).click();
    await page.getByRole('dialog', { name: 'Ordenar fotos', exact: true }).waitFor({ state: 'detached' });
    // modo com descricao: a foto aberta ganha o campo da descricao; a selecao aplica legenda e descricao
    await page.getByRole('radio', { name: /Legenda e descrição do serviço/ }).click();
    await page.waitForFunction(() => document.querySelector('.modo.on')?.textContent.includes('descrição'));
    await page.evaluate(() => window.scrollTo(0, 0)); await page.locator('.conteudo').evaluate(e => e.scrollTop = 0);
    await foto(page, '15f-rel-modo-descricao', w, h, salvar);
    await page.locator('button.foto').first().click(); await page.waitForTimeout(400);
    const descricao = page.locator('.folha textarea');
    if (await descricao.count() !== 1) problemas.push(`${w}x${h} modo com descricao: a foto aberta nao tem o campo da descricao`);
    await descricao.fill('d'.repeat(320));
    if ((await descricao.inputValue()).length !== 300) problemas.push(`${w}x${h} descricao passou de 300 caracteres`);
    await descricao.fill('Concreto usinado fck 30 MPa lançado nos blocos B1 a B4, com adensamento mecânico e cura úmida por sete dias.');
    await foto(page, '15g-rel-foto-aberta-descricao', w, h, salvar);
    await page.locator('.folha .btn.primario', { hasText: 'Pronto' }).click(); await page.waitForTimeout(300);
    await page.getByRole('button', { name: 'Selecionar', exact: true }).click();
    await fotos.nth(1).click(); await fotos.nth(2).click();
    await page.locator('.legenda-varias textarea').fill(`Aço CA-50, cobrimento conferido e espaçadores posicionados antes da concretagem (${w}).`);
    await foto(page, '15h-rel-marcadas-descricao', w, h, salvar);
    await page.getByRole('button', { name: 'Aplicar às 2 marcadas', exact: true }).click();
    await page.getByText('Texto aplicado a 2 fotos.', { exact: true }).waitFor();
    await page.getByRole('button', { name: 'Cancelar', exact: true }).click();
    await ir(page, urlPronto, 'relatorio/conferir'); await page.waitForTimeout(600);
    const apagadas = await page.locator('.chips .chip.apagado').allTextContents();
    if (apagadas.join(',') !== '6,8') problemas.push(`${w}x${h} com descricao, 6 e 8 deviam estar apagadas (${apagadas.join(',')})`);
    if (!(await page.locator('.chips .chip.on').textContent()).startsWith('4')) problemas.push(`${w}x${h} com descricao a capacidade marcada devia ser 4`);
    await foto(page, '16b-rel-conferir-descricao', w, h, salvar);
    // volta para so legenda (as descricoes ficam guardadas) e confere como antes
    await ir(page, urlPronto, 'relatorio/fotos');
    await page.getByRole('radio', { name: /Só legenda/ }).click();
    await page.locator('.aviso', { hasText: 'descrição guardada' }).waitFor();
    await ir(page, urlPronto, 'relatorio/conferir'); await page.waitForTimeout(600);
    await foto(page, '16-rel-conferir', w, h, salvar);
    // assinaturas: liga, escreve os nomes e confere que a previa mostra a assinatura na pagina da ultima foto
    await page.getByRole('button', { name: /Espaço para assinaturas no fim/ }).click();
    await page.getByLabel('Nome sob “Fiscalização SPOBRAS” (opcional)').fill(`Fiscal ${w}`);
    await page.getByLabel('Nome sob “Fiscalização SPOBRAS” (opcional)').blur(); await page.waitForTimeout(400);
    await foto(page, '16c-rel-conferir-assinaturas', w, h, salvar);
    await page.getByRole('button', { name: /Espaço para assinaturas no fim/ }).click(); await page.waitForTimeout(300);
    // dois toques rapidos em Gerar: um relatorio so, sem erro
    await page.evaluate(() => { const b = [...document.querySelectorAll('.btn.primario')].find(x => x.textContent.includes('Gerar relatório')); b.click(); b.click(); });
    await page.waitForSelector('text=Pronto!', { timeout: 60000 }).catch(() => problemas.push(`${w}x${h} dois toques em Gerar: nao chegou ao Pronto`));
    await foto(page, '17-rel-pronto', w, h, salvar);
    // excluir um arquivo gerado pela tela inicial: lixeira, Desfazer devolve, depois de 5 s sai de vez
    await ir(page, urlPronto, '');
    const gerados = page.locator('.arquivo');
    const nGerados = await gerados.count();
    if (nGerados < 2) problemas.push(`${w}x${h} inicio: ultimos gerados nao apareceram`);
    await page.locator('.excluir-arquivo').first().click();
    await page.locator('.desfazer', { hasText: 'Arquivo excluído' }).waitFor({ timeout: 3000 }).catch(() => problemas.push(`${w}x${h} excluir: sem aviso de desfazer`));
    if (await gerados.count() !== nGerados - 1) problemas.push(`${w}x${h} excluir: o arquivo nao saiu da lista`);
    await foto(page, '17b-inicio-excluido', w, h, salvar);
    await page.getByRole('button', { name: 'Desfazer', exact: true }).click();
    await esperar(page, n => document.querySelectorAll('.arquivo').length === n, nGerados, `${w}x${h} excluir: desfazer nao devolveu o arquivo`);
    await page.locator('.excluir-arquivo').first().click();
    await page.waitForTimeout(5800);
    if (await gerados.count() !== nGerados - 1) problemas.push(`${w}x${h} excluir: depois de 5 s o arquivo voltou ou nao saiu`);
    if (await page.locator('.aviso.erro').count() > 0) problemas.push(`${w}x${h} excluir: deu erro ao apagar`);

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

    // Configuracoes da base: engrenagem discreta, tres abas
    {
      const p = await abrir(browser, urlPronto, w, h);
      await ir(p, urlPronto, 'base');
      const g = await p.locator('a.engrenagem').boundingBox(), ico = await p.locator('a.engrenagem svg').boundingBox();
      if (!(g.height >= 44 && ico.width <= 20)) problemas.push(`${w}x${h} engrenagem nao esta discreta com toque bom`);
      await p.locator('a.engrenagem').click(); await p.waitForSelector('.aviso-config');
      await foto(p, '26-config-base', w, h, salvar);
      await p.getByRole('tab', { name: 'Status' }).click(); await p.waitForTimeout(300);
      await foto(p, '27-config-status', w, h, salvar);
      await p.getByRole('tab', { name: 'Diagnóstico' }).click(); await p.waitForTimeout(300);
      await foto(p, '28-config-diagnostico', w, h, salvar);
      await p.getByRole('tab', { name: 'Planilha' }).click(); await p.waitForTimeout(200);
      await p.locator('.btn.padrao').click(); await p.getByRole('dialog', { name: 'Voltar ao padrão?' }).waitFor();
      await foto(p, '29-config-padrao', w, h, salvar);
      await p.getByRole('dialog').getByRole('button', { name: 'Cancelar' }).click();
      await p.context().close();
    }
    if (urlColuna) {
      // planilha com coluna renomeada: escolher a coluna nova, testar e salvar
      const p = await abrir(browser, urlColuna, w, h);
      await ir(p, urlColuna, 'base/configuracoes');
      try { await p.waitForSelector('.item-col.falta', { timeout: 15000 }); }
      catch (e) { await p.screenshot({ path: path.join(pasta, 'erro-coluna.png') }); throw new Error('sem coluna faltando: ' + (await p.locator('.app').innerText()).slice(0, 300)); }
      await foto(p, '30-config-coluna-falta', w, h, salvar);
      if (!(await p.locator('button.primario', { hasText: 'Salvar' }).isDisabled())) problemas.push(`${w}x${h} salvar liberado sem testar`);
      await p.getByRole('button', { name: 'Escolher Data da OS' }).click();
      await p.getByLabel('Pesquisar coluna').fill('inicio obra'); await p.waitForTimeout(250);
      await foto(p, '31-config-escolher-coluna', w, h, salvar);
      await p.locator('.folha .opcao', { hasText: 'DATA INÍCIO OBRA' }).click(); await p.waitForTimeout(400);
      if (await p.locator('.item-col.falta').count() !== 0) problemas.push(`${w}x${h} coluna escolhida continua faltando`);
      await p.getByRole('button', { name: 'Testar leitura' }).click();
      await p.getByText('Leitura de teste ok', { exact: false }).waitFor({ timeout: 15000 });
      await foto(p, '32-config-teste', w, h, salvar);
      await p.locator('button.primario', { hasText: 'Salvar' }).click();
      await p.getByText('Colunas salvas', { exact: false }).waitFor({ timeout: 15000 });
      await ir(p, urlColuna, 'base'); await p.waitForTimeout(400);
      if (await p.locator('a.obra').count() === 0) problemas.push(`${w}x${h} a base nao voltou a ler depois de salvar as colunas`);
      // voltar ao padrao desfaz o ajuste: a coluna volta a faltar (e deixa o cenario pronto para o proximo tamanho)
      await ir(p, urlColuna, 'base/configuracoes'); await p.waitForSelector('.btn.padrao');
      await p.locator('.btn.padrao').click();
      await p.getByRole('dialog').getByRole('button', { name: 'Voltar ao padrão' }).click();
      try { await p.waitForSelector('.item-col.falta', { timeout: 15000 }); } catch { problemas.push(`${w}x${h} voltar ao padrao nao desfez o ajuste da coluna`); }
      await p.context().close();
    }
    for (const [url, nome] of [[urlNovo, '01-primeira'], [urlMovido, '25-movido'], [urlSemRede, '24-sem-internet']]) {
      if (!url) continue;
      const p = await abrir(browser, url, w, h);
      await foto(p, nome, w, h, salvar);
      await p.context().close();
    }
  }
  // Uso intenso: 2.000 obras e 60 fotos, com tempo medido de cada acao
  if (urlGrande) {
    const p = await abrir(browser, urlGrande, 412, 915);
    const medir = async (nome, limite, acao) => {
      const t = Date.now(); await acao(); const ms = Date.now() - t;
      tempos.push(`${nome}: ${ms} ms`);
      if (ms > limite) problemas.push(`uso intenso: ${nome} levou ${ms} ms (máximo ${limite})`);
    };
    await medir('abrir a Base com 2.000 obras', 3000, async () => { await ir(p, urlGrande, 'base'); await p.waitForSelector('a.obra'); });
    await medir('abrir o filtro Contrato (2.000 obras)', 2000, async () => { await p.locator('.chip', { hasText: 'Contrato' }).click(); await p.waitForSelector('.opcao-filtro'); });
    if (await p.locator('.opcao-filtro').count() > 100) problemas.push('uso intenso: o filtro desenhou mais de 100 opções');
    for (let k = 0; k < 3; k++) {
      const antes = await p.locator('.mini').first().textContent();
      await medir(`marcar o contrato ${k + 1}`, 1500, async () => {
        await p.locator('.opcao-filtro input').nth(k).click();
        await p.waitForFunction(k => document.querySelectorAll('.opcao-filtro input:checked').length === k + 1, k);
      });
    }
    await medir('pesquisar no filtro', 1500, async () => { await p.getByLabel('Pesquisar opções').fill('LOTE 19'); await p.waitForFunction(() => document.querySelectorAll('.opcao-filtro').length < 100); });
    await p.getByRole('button', { name: 'Pronto' }).click();
    await medir('abrir as fotos (60) até todas as miniaturas', 30000, async () => {
      await ir(p, urlGrande, 'relatorio/fotos');
      await p.waitForFunction(() => { const b = [...document.querySelectorAll('button[data-foto]')]; return b.length >= 60 && b.every(x => x.querySelector('img')); }, null, { timeout: 30000 });
    });
    await medir('abrir as fotos de novo (miniaturas guardadas)', 2500, async () => {
      await ir(p, urlGrande, 'relatorio');
      await ir(p, urlGrande, 'relatorio/fotos');
      await p.waitForFunction(() => { const b = [...document.querySelectorAll('button[data-foto]')]; return b.length >= 60 && b.every(x => x.querySelector('img')); });
    });
    await medir('conferir as páginas com 60 fotos', 5000, async () => { await ir(p, urlGrande, 'relatorio/conferir'); await p.waitForSelector('.pagina-previa, .chips'); });
    await p.screenshot({ path: path.join(pasta, '40-uso-intenso-conferir.png') });
    await p.context().close();
  }

  // Camera e fotos, tentando quebrar: sair no meio da foto, flash lento ou travado, legenda gigante, foto repetida
  if (urlGrande) {
    const p = await abrir(browser, urlGrande, 412, 915);
    const contar = async () => { await p.waitForTimeout(3000); return p.locator('button.foto').count(); };
    const abrirCamera = async () => { await p.locator('a.fab').click(); await p.waitForSelector('.camera'); await p.waitForTimeout(1500); };
    const flash = async nome => {
      await p.locator('.topo-cam .bt-cam', { hasText: '⚡' }).click();
      await p.locator('.folha .btn', { hasText: nome }).click();
      await p.locator('.folha .btn.primario').click(); await p.waitForTimeout(300);
    };
    await ir(p, urlGrande, 'relatorio/fotos');
    let n = await contar();
    await abrirCamera();
    if (!(await p.locator('.topo-cam .bt-cam', { hasText: '⚡' }).textContent()).includes('Desligado')) problemas.push('camera: o flash padrao nao e Desligado');
    // engrenagem abre as configuracoes da camera: proporcao, flash e galeria juntos
    await p.getByRole('button', { name: 'Configurações da câmera', exact: true }).click();
    await p.locator('.folha').waitFor();
    for (const t of ['Proporção da foto', 'Flash', 'Galeria', 'Guardar também na galeria']) {
      if (await p.locator('.folha', { hasText: t }).count() === 0) problemas.push(`camera: configuracoes sem "${t}"`);
    }
    await foto(p, '44-camera-configuracoes', 412, 915, true);
    await p.locator('.folha .btn.primario').click(); await p.waitForTimeout(300);
    // tirar e voltar na hora (botao voltar do Android)
    await p.locator('.disparo').click(); await p.goBack();
    if (await contar() !== n + 1) problemas.push('camera: tirar e voltar na hora perdeu a foto');
    n++;
    // flash Auto lento (1,5 s): tocar em Pronto durante o "Segure firme"
    await abrirCamera(); await flash('Auto');
    await p.evaluate(() => { const o = ImageCapture.prototype.takePhoto; ImageCapture.prototype.takePhoto = function (x) { return new Promise(r => setTimeout(r, 1500)).then(() => o.call(this, x)); }; });
    await p.locator('.disparo').click(); await p.waitForTimeout(300);
    if (!(await p.locator('.segure').isVisible())) problemas.push('camera: "Segure firme" nao apareceu com flash');
    if (await p.locator('.disparo.esperando').count() !== 1) problemas.push('camera: o botao nao mostrou que esta esperando');
    await p.screenshot({ path: path.join(pasta, '41-camera-segure-firme.png') });
    await p.locator('.pronto-cam').click();
    if (await contar() !== n + 1) problemas.push('camera: tocar em Pronto durante o "Segure firme" perdeu a foto');
    n++;
    // sensor travado: depois de 4 s vale o quadro do toque
    await abrirCamera();
    await p.evaluate(() => { ImageCapture.prototype.takePhoto = () => new Promise(() => {}); });
    await p.locator('.disparo').click(); await p.waitForTimeout(4800);
    if (await p.locator('.segure').isVisible()) problemas.push('camera: ficou presa no "Segure firme" com o sensor travado');
    await p.locator('.pronto-cam').click();
    if (await contar() !== n + 1) problemas.push('camera: com o sensor travado a foto do toque nao entrou');
    n++;
    await abrirCamera(); await flash('Desligado'); await p.locator('.pronto-cam').click(); await p.waitForTimeout(600);
    // legenda: no maximo 100 caracteres, com contador
    await p.locator('button.foto').first().click();
    const campo = p.locator('.folha input[type=text]').first();
    await campo.fill('x'.repeat(110));
    if ((await campo.inputValue()).length !== 100) problemas.push('legenda: passou de 100 caracteres');
    // o contador ja aparece antes de digitar: espera ele refletir o que foi digitado
    await p.waitForFunction(() => (document.querySelector('.folha .contador')?.textContent || '').includes('100 de 100'), null, { timeout: 5000 }).catch(() => {});
    const contador = await p.locator('.folha .contador').first().textContent().catch(() => '');
    if (!contador.includes('100 de 100')) problemas.push(`legenda: contador errado (${contador})`);
    await foto(p, '42-legenda-limite', 412, 915, true);
    await campo.fill('Legenda curta'); await campo.blur(); await p.waitForTimeout(300);
    await p.locator('.folha .btn.primario', { hasText: 'Pronto' }).click(); await p.waitForTimeout(300);
    // mesma foto da galeria de novo: entra assim mesmo e um aviso so
    const antesGaleria = await p.locator('button.foto').count();
    await p.getByRole('button', { name: 'Da galeria', exact: true }).click();
    await p.waitForFunction(n => document.querySelectorAll('button.foto').length > n, antesGaleria, { timeout: 30000 }).catch(() => {});
    const avisos = await p.locator('.aviso', { hasText: 'já estavam no relatório' }).count();
    if (avisos !== 1) problemas.push(`galeria: esperava 1 aviso de fotos repetidas, apareceram ${avisos}`);
    if (await p.locator('button.foto').count() <= antesGaleria) problemas.push('galeria: fotos repetidas nao entraram');
    await foto(p, '43-galeria-repetidas', 412, 915, true);
    // digitar e sair sem confirmar o campo (voltar do Android, menu): nada se perde
    await p.locator('button.foto').nth(1).click();
    await p.locator('.folha input[type=text]').first().fill('Digitei e voltei');
    await p.goBack(); await p.waitForTimeout(800);
    await ir(p, urlGrande, 'relatorio/fotos');
    if (!((await p.locator('button.foto').nth(1).getAttribute('aria-label')) || '').includes('Digitei e voltei')) problemas.push('legenda: digitar e voltar perdeu o texto');
    await ir(p, urlGrande, 'relatorio');
    await p.getByRole('textbox', { name: 'Fiscal' }).fill('Fiscal digitado');
    await ir(p, urlGrande, 'base'); await ir(p, urlGrande, 'relatorio');
    if (await p.getByRole('textbox', { name: 'Fiscal' }).inputValue() !== 'Fiscal digitado') problemas.push('dados: digitar e ir para outra tela perdeu o texto');
    await p.context().close();
  }

  // Fotos "Salvando…", fila cheia, foto que nao salvou, carimbo (editor, foto, paginas), endereco pelo GPS e diagnostico
  if (urlGrande) {
    for (const [w, h] of [[412, 915], [360, 640]]) {
      const p = await abrir(browser, urlGrande, w, h);
      const grande = w === 412;
      const tela = `${w}x${h}`;
      const reais = () => p.locator('button.foto[data-foto]').count();
      const abrirCamera = async () => { await ir(p, urlGrande, 'relatorio/camera'); await p.waitForSelector('.camera'); await p.waitForTimeout(1500); };
      const lento = ms => p.evaluate(ms => {
        const orig = window.__toBlobOriginal || (window.__toBlobOriginal = HTMLCanvasElement.prototype.toBlob);
        HTMLCanvasElement.prototype.toBlob = function (cb, ...r) { setTimeout(() => orig.call(this, cb, ...r), ms); };
      }, ms);
      const normal = () => p.evaluate(() => { if (window.__toBlobOriginal) HTMLCanvasElement.prototype.toBlob = window.__toBlobOriginal; });
      await ir(p, urlGrande, 'relatorio/fotos'); await p.waitForTimeout(800);
      let n = await reais();

      // 1) tres fotos seguidas com a gravacao lenta: a lista ja mostra as tres no lugar, com "Salvando…"
      await abrirCamera();
      await lento(1800);
      for (let k = 0; k < 3; k++) await p.locator('.disparo').click();
      await p.waitForFunction(() => (document.querySelector('.salvando-cam')?.textContent || '').includes('Salvando'), null, { timeout: 4000 })
        .catch(() => problemas.push(`${tela} camera: contador "Salvando" nao apareceu`));
      await p.waitForFunction(t => (document.querySelector('.pronto-cam .pronto-n')?.textContent || '').includes(t), `(${n + 3})`, { timeout: 4000 })
        .catch(async () => problemas.push(`${tela} camera: "Pronto" nao contou as fotos salvando (${await p.locator('.pronto-cam .pronto-n').textContent()}, esperava ${n + 3})`));
      await foto(p, '45-camera-salvando', w, h, grande);
      await p.locator('.pronto-cam').click(); await p.waitForTimeout(400);
      const naHora = await p.evaluate(() => ({ salvando: document.querySelectorAll('.foto.salvando').length, reais: document.querySelectorAll('button.foto[data-foto]').length, sub: document.querySelector('.barra')?.textContent || '' }));
      if (naHora.salvando + naHora.reais !== n + 3) problemas.push(`${tela} lista: logo depois da camera mostrou ${naHora.reais} + ${naHora.salvando} salvando (esperava ${n + 3})`);
      if (naHora.salvando > 0 && !naHora.sub.includes('salvando')) problemas.push(`${tela} lista: o topo nao disse quantas estao salvando`);
      if (naHora.salvando > 0) {
        await p.locator('.foto.salvando').last().scrollIntoViewIfNeeded();
        await foto(p, '46-lista-salvando', w, h, grande);
      }
      await esperar(p, n => document.querySelectorAll('button.foto[data-foto]').length === n && !document.querySelector('.foto.salvando'), n + 3, `${tela} lista: as 3 fotos nao terminaram de salvar`, 15000);
      n += 3;

      // 2) fila cheia: com a gravacao bem lenta, o setimo toque espera ("Aguarde…") e nenhuma foto se perde
      await abrirCamera();
      await lento(2500);
      for (let k = 0; k < 7; k++) await p.locator('.disparo').click({ force: true });
      if (await p.locator('.disparo.cheia').count() !== 1) problemas.push(`${tela} camera: com 6 salvando o botao nao pediu para esperar`);
      if (!((await p.locator('.salvando-cam').textContent()) || '').includes('Aguarde')) problemas.push(`${tela} camera: fila cheia sem "Aguarde…"`);
      await foto(p, '47-camera-fila-cheia', w, h, grande);
      await p.locator('.pronto-cam').click();
      await esperar(p, n => document.querySelectorAll('button.foto[data-foto]').length === n && !document.querySelector('.foto.salvando'), n + 6, `${tela} fila cheia: esperava 6 fotos novas`, 30000);
      n += 6;
      await normal();

      // 3) foto que nao salvou: fica com aviso, "Tentar de novo" salva
      await abrirCamera();
      await p.evaluate(() => {
        const orig = window.__toBlobOriginal || (window.__toBlobOriginal = HTMLCanvasElement.prototype.toBlob);
        let falhar = true;
        HTMLCanvasElement.prototype.toBlob = function (cb, ...r) { if (falhar && this.width > 300) { falhar = false; cb(null); return; } orig.call(this, cb, ...r); };
      });
      await p.locator('.disparo').click(); await p.waitForTimeout(800);
      if (await p.locator('.aviso', { hasText: 'não foi salva' }).count() === 0) problemas.push(`${tela} camera: foto que falhou sem aviso`);
      await p.locator('.pronto-cam').click(); await p.waitForTimeout(500);
      if (await p.locator('.foto.nao-salva').count() !== 1) problemas.push(`${tela} lista: a foto que falhou sumiu`);
      else {
        await p.locator('.foto.nao-salva').scrollIntoViewIfNeeded();
        await foto(p, '48-lista-nao-salva', w, h, grande);
        await p.locator('.foto.nao-salva').click(); await p.waitForTimeout(300);
        await foto(p, '49-folha-nao-salva', w, h, grande);
        await normal();
        await p.getByRole('button', { name: 'Tentar de novo', exact: true }).click();
        await esperar(p, n => document.querySelectorAll('button.foto[data-foto]').length === n && !document.querySelector('.foto.nao-salva, .foto.salvando'), n + 1, `${tela} tentar de novo nao salvou a foto`, 8000);
        n++;
      }
      await normal();

      // 4) editor do carimbo: liga, itens, arrastar para o canto de cima a direita, tamanho, foto em pe
      await ir(p, urlGrande, 'relatorio/fotos'); await p.waitForTimeout(500);
      await p.getByRole('button', { name: /^Carimbo nas fotos/ }).click();
      await p.locator('.folha').waitFor();
      if (await p.locator('.folha .opcao.on', { hasText: 'Carimbar as fotos' }).count() === 0) await p.locator('.folha .opcao', { hasText: 'Carimbar as fotos' }).click();
      await p.locator('.folha svg.previa-carimbo g.carimbo').waitFor({ timeout: 5000 }).catch(() => problemas.push(`${tela} carimbo: a previa nao mostrou o carimbo`));
      const dentro = () => p.evaluate(() => {
        const s = document.querySelector('.folha svg.previa-carimbo'), g = s?.querySelector('g.carimbo');
        if (!s || !g) return null;
        const a = s.getBoundingClientRect(), b = g.getBBox(), l = +s.dataset.largura, h = +s.dataset.altura;
        const letra = parseFloat(g.querySelector('text')?.getAttribute('font-size') || '0');
        return { ok: b.x >= 0 && b.y >= 0 && b.x + b.width <= l + 1 && b.y + b.height <= h + 1, x: +g.dataset.x, y: +g.dataset.y, w: +g.dataset.w, l, h, letra, tela: a.width > 0 };
      });
      let d = await dentro();
      if (!d?.ok) problemas.push(`${tela} carimbo: saiu da foto na previa`);
      await foto(p, '50-carimbo-editor', w, h, grande);
      // arrasta com o mouse para o canto de cima a direita
      const g = p.locator('.folha svg.previa-carimbo g.carimbo rect').first();
      const caixa = await g.boundingBox(), svgCaixa = await p.locator('.folha svg.previa-carimbo').boundingBox();
      if (caixa && svgCaixa) {
        await p.mouse.move(caixa.x + caixa.width / 2, caixa.y + caixa.height / 2);
        await p.mouse.down();
        await p.mouse.move(svgCaixa.x + svgCaixa.width - caixa.width / 2 - 4, svgCaixa.y + caixa.height / 2 + 4, { steps: 8 });
        await p.mouse.up();
        await p.waitForTimeout(600);
        d = await dentro();
        if (!d || !(d.x + d.w / 2 > d.l / 2 && d.y < d.h / 2)) problemas.push(`${tela} carimbo: arrastar para o canto de cima a direita nao grudou la (${JSON.stringify(d)})`);
        if (!d?.ok) problemas.push(`${tela} carimbo: depois de arrastar saiu da foto`);
      }
      const letraAntes = d?.letra || 0, larguraAntes = d?.w || 0;
      await p.locator('#tamanho-carimbo').fill('80'); await p.locator('#tamanho-carimbo').dispatchEvent('change'); await p.waitForTimeout(400);
      d = await dentro();
      // maior, ou ja no limite de 60% da largura da foto (ai a letra nao cresce mais)
      if (!(d && (d.letra > letraAntes || larguraAntes >= 0.59 * d.l))) problemas.push(`${tela} carimbo: a barra de tamanho nao aumentou o carimbo (${letraAntes} -> ${d?.letra})`);
      if (!d?.ok) problemas.push(`${tela} carimbo: no tamanho maximo saiu da foto`);
      if (await p.getByRole('button', { name: 'Ver em pé', exact: true }).count() > 0) {
        await p.getByRole('button', { name: 'Ver em pé', exact: true }).click(); await p.waitForTimeout(400);
        d = await dentro();
        if (!(d && d.h > d.l)) problemas.push(`${tela} carimbo: "Ver em pé" nao mostrou foto em pé`);
        if (!d?.ok) problemas.push(`${tela} carimbo: na foto em pé saiu da foto`);
        await p.locator('.folha svg.previa-carimbo').scrollIntoViewIfNeeded();
        if (grande) await p.screenshot({ path: path.join(pasta, '51-carimbo-em-pe.png') }); // folha rolada: a conferencia de botoes cobertos nao se aplica
      }
      await p.locator('#tamanho-carimbo').fill('45'); await p.locator('#tamanho-carimbo').dispatchEvent('change'); await p.waitForTimeout(300);
      await p.getByRole('button', { name: 'Padrão', exact: true }).click(); await p.waitForTimeout(300);
      await p.locator('.folha .btn.primario', { hasText: 'Pronto' }).click(); await p.waitForTimeout(300);

      // 5) ficha da foto: previa com carimbo e "Carimbo nesta foto"
      await p.locator('button.foto[data-foto]').first().click(); await p.waitForTimeout(400);
      if (await p.locator('.folha .previa-foto-carimbo svg g.carimbo').count() !== 1) problemas.push(`${tela} ficha da foto sem o carimbo na previa`);
      if (await p.locator('.folha .opcao', { hasText: 'Carimbo nesta foto' }).count() !== 1) problemas.push(`${tela} ficha da foto sem "Carimbo nesta foto"`);
      await foto(p, '52-foto-com-carimbo', w, h, grande);
      await p.locator('.folha .btn.primario', { hasText: 'Pronto' }).click(); await p.waitForTimeout(300);

      // 6) endereco nos dados: GPS desligado no comeco (nada pedido), ligar explica antes, procurando, lido, editar
      await ir(p, urlGrande, 'relatorio'); await p.waitForTimeout(400);
      const cartao = p.locator('.cartao-endereco');
      if (await cartao.count() !== 1) problemas.push(`${tela} dados: sem o cartao do endereco com o carimbo ligado`);
      else {
        await cartao.scrollIntoViewIfNeeded();
        const gps = cartao.locator('.gps-opcao');
        if (await gps.count() !== 1) problemas.push(`${tela} dados: sem o interruptor "Usar o endereço pelo GPS"`);
        if (await gps.getAttribute('aria-pressed') !== 'true') {
          await foto(p, '53a-endereco-gps-desligado', w, h, grande);
          if (await cartao.locator('.lendo-endereco').count() > 0) problemas.push(`${tela} dados: com o GPS desligado o app procurou a posicao`);
          await gps.click(); await p.waitForTimeout(300);
          if (await p.locator('.folha', { hasText: 'Usar o endereço pelo GPS?' }).count() !== 1) problemas.push(`${tela} dados: ligar o GPS nao explicou antes de pedir a permissao`);
          await foto(p, '53b-endereco-gps-explica', w, h, grande);
          await p.locator('.folha .btn.primario', { hasText: 'Continuar' }).click();
        } else {
          await cartao.getByRole('button', { name: /Ler de novo|Ler agora|Tentar de novo/ }).click();
        }
        await p.waitForTimeout(400);
        if (await cartao.locator('.lendo-endereco').count() === 1) await foto(p, '53-endereco-procurando', w, h, grande);
        await esperar(p, () => !!document.querySelector('.cartao-endereco .endereco-atual'), null, `${tela} dados: o endereco do GPS nao apareceu`, 8000);
        await foto(p, '54-endereco-lido', w, h, grande);
        await cartao.getByRole('button', { name: 'Editar', exact: true }).click();
        const campo = cartao.locator('input[type=text]').first();
        await campo.fill('Rua Digitada pelo Fiscal, 45 – Vila Teste'); await campo.blur(); await p.waitForTimeout(300);
        await cartao.getByRole('button', { name: 'Pronto', exact: true }).click(); await p.waitForTimeout(300);
        if (!((await cartao.locator('.endereco-atual').textContent()) || '').includes('Rua Digitada')) problemas.push(`${tela} dados: editar o endereco nao guardou`);
      }

      // 7) camera com o selo do endereco (sem cobrir botoes) e as paginas com o carimbo
      await abrirCamera();
      const selo = p.locator('.selo-endereco');
      if (await selo.count() !== 1 || !((await selo.textContent()) || '').includes('Rua Digitada')) problemas.push(`${tela} camera: selo do endereco ausente ou errado`);
      else {
        const sobrepoe = await p.evaluate(() => {
          const s = document.querySelector('.selo-endereco').getBoundingClientRect();
          return [...document.querySelectorAll('.camera button, .camera .pronto-cam')].some(b => { const r = b.getBoundingClientRect(); return r.width && !(r.right <= s.left || r.left >= s.right || r.bottom <= s.top || r.top >= s.bottom); });
        });
        if (sobrepoe) problemas.push(`${tela} camera: o selo do endereco cobre um botao`);
      }
      await foto(p, '55-camera-endereco', w, h, grande);
      await p.locator('.pronto-cam').click(); await p.waitForTimeout(300);
      await ir(p, urlGrande, 'relatorio/conferir');
      await esperar(p, () => document.querySelectorAll('.previa-pagina svg rect[fill="#000"]').length > 0, null, `${tela} conferir: as paginas nao mostram o carimbo`, 10000);
      await foto(p, '56-conferir-carimbo', w, h, grande);

      // 8) diagnostico da camera nas configuracoes
      await ir(p, urlGrande, 'config'); await p.waitForTimeout(300);
      await p.getByRole('button', { name: 'Diagnóstico da câmera', exact: true }).click(); await p.waitForTimeout(300);
      if (await p.locator('.tabela-diagnostico tbody tr').count() !== 4) problemas.push(`${tela} diagnostico: tabela sem os 4 passos`);
      await foto(p, '57-diagnostico-camera', w, h, grande);
      await p.locator('.folha .btn.primario').click();

      // deixa o GPS e o carimbo desligados como estavam (o proximo tamanho comeca do mesmo estado)
      await ir(p, urlGrande, 'relatorio'); await p.waitForTimeout(400);
      if (await p.locator('.cartao-endereco .gps-opcao[aria-pressed="true"]').count() === 1) {
        await p.locator('.cartao-endereco .gps-opcao').click(); await p.waitForTimeout(300);
        if (await p.locator('.cartao-endereco .gps-opcao[aria-pressed="false"]').count() !== 1) problemas.push(`${tela} dados: desligar o GPS nao desligou`);
      }

      await ir(p, urlGrande, 'relatorio/fotos'); await p.waitForTimeout(400);
      await p.getByRole('button', { name: /^Carimbo nas fotos/ }).click();
      await p.locator('.folha .opcao.on', { hasText: 'Carimbar as fotos' }).click(); await p.waitForTimeout(300);
      await p.locator('.folha .btn.primario', { hasText: 'Pronto' }).click();
      await p.context().close();
    }
  }

  await browser.close();
  if (tempos.length) console.log('Tempos (uso intenso):\n  ' + tempos.join('\n  '));
  console.log(problemas.length ? problemas.join('\n') : 'nenhum problema');
  process.exit(problemas.length ? 1 : 0);
})();
