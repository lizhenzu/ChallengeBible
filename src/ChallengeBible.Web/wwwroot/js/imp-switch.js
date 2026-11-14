// 사람 바꾸기(ADR 0096, 확정 시안 사람바꾸기): 가장 띠의 [사람 바꾸기] → 화면 위 창. 부서 · 역할 칩 · 찾기 · 더 보기는 창 안에서 다시 불러오고,
// [바꾸기 ›]는 보통 폼 보내기(서버가 로그인을 바꾸고 옮긴다). 스크립트가 없으면 링크대로 바꾸기 화면이 열린다.
(() => {
    const btn = document.querySelector('[data-imp-switch]');
    if (!btn) return;
    let bg = null, timer = 0;
    const withFrag = (url) => url + (url.includes('?') ? '&' : '?') + 'frag=true';
    const close = () => { bg?.remove(); bg = null; };
    const load = async (url) => {
        try {
            const r = await fetch(withFrag(url), { credentials: 'same-origin', headers: { 'X-Requested-With': 'fetch' } });
            if (!r.ok) { location.href = url; return; }
            const html = await r.text();
            if (!bg) {
                bg = document.createElement('div');
                bg.className = 'sw-dim';
                document.body.appendChild(bg);
                bg.addEventListener('click', onClick);
                bg.addEventListener('submit', onSubmit);
                bg.addEventListener('input', onInput);
            }
            const focused = document.activeElement?.name === 'q';
            bg.innerHTML = html;
            const q = bg.querySelector('input[name=q]');
            if (focused && q) { q.focus(); q.setSelectionRange(q.value.length, q.value.length); }
        } catch { location.href = url; }
    };
    const findUrl = (form) => {
        const p = new URLSearchParams(new FormData(form));
        if (!p.get('q')) p.delete('q');
        return form.getAttribute('action') + '?' + p.toString();
    };
    function onClick(e) {
        if (e.target === bg || e.target.closest('[data-sw-close]')) { e.preventDefault(); close(); return; }
        const a = e.target.closest('a[data-sw]');
        if (a) { e.preventDefault(); load(a.getAttribute('href')); }
    }
    function onSubmit(e) {
        const f = e.target.closest('[data-sw-find]');
        if (f) { e.preventDefault(); load(findUrl(f)); }
    }
    function onInput(e) {
        const f = e.target.closest('[data-sw-find]');
        if (!f) return;
        clearTimeout(timer);
        timer = setTimeout(() => load(findUrl(f)), 300);
    }
    btn.addEventListener('click', (e) => { e.preventDefault(); load(btn.getAttribute('href')); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
})();
