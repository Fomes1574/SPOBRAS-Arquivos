const ligados = new WeakMap();

export function ligar(grade, dotnet) {
    const area = grade.closest('.conteudo');
    const listeners = new AbortController();
    let arraste, timer, quadro, bloquearClique = false;
    const fotos = () => [...grade.querySelectorAll('[data-foto]')];

    function mover() {
        if (!arraste?.sombra) return;
        const { x, y, origem, sombra } = arraste;
        const limite = area.getBoundingClientRect();
        if (y < limite.top + 60) area.scrollTop -= 12;
        else if (y > limite.bottom - 60) area.scrollTop += 12;
        sombra.style.transform = `translate(${x - arraste.offsetX}px,${y - arraste.offsetY}px) scale(1.05)`;
        const itens = fotos();
        const caixas = itens.map(el => {
            const pai = el.offsetParent.getBoundingClientRect();
            return new DOMRect(pai.left + el.offsetLeft, pai.top + el.offsetTop, el.offsetWidth, el.offsetHeight);
        });
        let destino = origem;
        let distancia = Infinity;
        caixas.forEach((caixa, i) => {
            const medida = Math.hypot(x - caixa.left - caixa.width / 2, y - caixa.top - caixa.height / 2);
            if (medida < distancia) {
                destino = i;
                distancia = medida;
            }
        });
        arraste.destino = destino;
        itens.forEach((el, i) => {
            const desloca = origem < destino && i > origem && i <= destino ? -1
                : origem > destino && i < origem && i >= destino ? 1 : 0;
            if (desloca) {
                const de = caixas[i], para = caixas[i + desloca];
                el.style.transform = `translate(${para.left - de.left}px,${para.top - de.top}px)`;
            } else {
                el.style.transform = '';
            }
        });
    }

    function iniciar() {
        if (!arraste) return;
        const foto = arraste.foto;
        const r = foto.getBoundingClientRect();
        arraste.sombra = foto.cloneNode(true);
        arraste.sombra.classList.add('foto-flutuante');
        arraste.sombra.removeAttribute('data-foto');
        arraste.sombra.style.width = `${r.width}px`;
        arraste.sombra.style.height = `${r.height}px`;
        arraste.offsetX = arraste.x - r.left;
        arraste.offsetY = arraste.y - r.top;
        foto.classList.add('foto-em-arraste');
        document.body.append(arraste.sombra);
        navigator.vibrate?.(15);
        const animar = () => {
            mover();
            quadro = requestAnimationFrame(animar);
        };
        animar();
    }

    function terminar(salvar) {
        clearTimeout(timer);
        cancelAnimationFrame(quadro);
        const estado = arraste;
        arraste = null;
        if (!estado?.sombra) return;
        estado.sombra.remove();
        estado.foto.classList.remove('foto-em-arraste');
        for (const foto of fotos()) {
            foto.style.transition = 'none';
            foto.style.transform = '';
        }
        requestAnimationFrame(() => {
            for (const foto of fotos()) foto.style.transition = '';
        });
        bloquearClique = true;
        setTimeout(() => bloquearClique = false, 400);
        if (salvar && estado.origem !== estado.destino)
            dotnet.invokeMethodAsync('ReordenarFoto', estado.origem, estado.destino);
    }

    const options = { signal: listeners.signal };
    grade.addEventListener('pointerdown', e => {
        const foto = e.target.closest('[data-foto]');
        if (!foto || e.button !== 0 || grade.dataset.selecao === "true") return;
        bloquearClique = false;
        terminar(false);
        const origem = fotos().indexOf(foto);
        arraste = { foto, origem, destino: origem, id: e.pointerId, x: e.clientX, y: e.clientY };
        timer = setTimeout(iniciar, 400);
    }, options);
    window.addEventListener('pointermove', e => {
        if (!arraste || arraste.id !== e.pointerId) return;
        if (!arraste.sombra && Math.hypot(e.clientX - arraste.x, e.clientY - arraste.y) > 7) terminar(false);
        else {
            arraste.x = e.clientX;
            arraste.y = e.clientY;
        }
    }, options);
    window.addEventListener('pointerup', e => {
        if (arraste?.id === e.pointerId) {
            if (arraste.sombra) {
                arraste.x = e.clientX;
                arraste.y = e.clientY;
                mover();
            }
            terminar(true);
        }
    }, options);
    window.addEventListener('pointercancel', () => terminar(false), options);
    grade.addEventListener('touchmove', e => {
        if (arraste?.sombra && e.cancelable) e.preventDefault();
    }, { ...options, passive: false });
    grade.addEventListener('contextmenu', e => {
        if (arraste?.sombra) e.preventDefault();
    }, options);
    grade.addEventListener('click', e => {
        if (!bloquearClique) return;
        e.preventDefault();
        e.stopImmediatePropagation();
    }, { ...options, capture: true });
    ligados.set(grade, () => {
        terminar(false);
        listeners.abort();
    });
}

export function desligar(grade) {
    ligados.get(grade)?.();
    ligados.delete(grade);
}
