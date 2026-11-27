// 사람 바꾸기(ADR 0096, 확정 시안 사람바꾸기): 가장 띠의 [사람 바꾸기] → 화면 위 창. 부서 · 역할 칩 · 찾기 · 더 보기는 창 안에서 다시 불러오고,
// [바꾸기 ›]는 보통 폼 보내기(서버가 로그인을 바꾸고 옮긴다). 스크립트가 없으면 링크대로 바꾸기 화면이 열린다.
(() => {
    const btn = document.querySelector('[data-imp-switch]');
    if (!btn) return;
    let bg = null, timer = 0;
    const withFrag = (url) => url + (url.includes('?') ? '&' : '?') + 'frag=true';
    const close = () => { bg?.remove(); bg = null; };
    let composing = false;
    const load = async (url, keepFind = false) => {
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
                bg.addEventListener('compositionstart', () => { composing = true; });
                bg.addEventListener('compositionend', (e) => { composing = false; onInput(e); });
            }
            // 2026-11-21 아이폰 한글: 찾기 칸을 통째로 다시 그리면 글자를 조합하는 중에 끊겨 "민종"이 "민조ㅇ"이 된다.
            // 찾기 칸이 이미 있으면 그 칸은 그대로 두고 나머지(칩 · 목록)만 바꾼다.
            const oldFind = bg.querySelector('[data-sw-find]');
            if (oldFind && keepFind) {
                const tmp = document.createElement('div');
                tmp.innerHTML = html;
                const top = bg.querySelector('.sw-top'), ftop = tmp.querySelector('.sw-top');
                const body = bg.querySelector('.sw-body'), fbody = tmp.querySelector('.sw-body');
                if (top && ftop && body && fbody) {
                    // 찾기 칸 뒤(칩)만 새것으로, 목록 칸은 통째로 — 찾기 칸 자체는 건드리지 않아서 포커스 · 한글 조합이 그대로
                    while (oldFind.nextSibling) oldFind.nextSibling.remove();
                    const nf = ftop.querySelector('[data-sw-find]');
                    let n = nf ? nf.nextSibling : null;
                    while (n) { const next = n.nextSibling; top.appendChild(n); n = next; }
                    body.replaceWith(fbody);
                } else {
                    bg.innerHTML = html;
                }
            } else {
                bg.innerHTML = html;
            }
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
        if (f) { e.preventDefault(); load(findUrl(f), true); }
    }
    function onInput(e) {
        const f = e.target.closest('[data-sw-find]');
        if (!f || composing || e.isComposing) return;   // 한글 조합 중에는 찾지 않는다(조합이 끝나면 찾음)
        clearTimeout(timer);
        timer = setTimeout(() => load(findUrl(f), true), 300);
    }
    btn.addEventListener('click', (e) => { e.preventDefault(); load(btn.getAttribute('href')); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
})();
