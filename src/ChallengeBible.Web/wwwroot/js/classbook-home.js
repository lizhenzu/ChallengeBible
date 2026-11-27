// 교적부 처음(R-361, 2026-11-21 확정 시안 교적부-A): 학년 탭 · 마지막으로 본 반 · 칠 때마다 바로 이름 찾기(초성도).
(function () {
    const root = document.querySelector('[data-rbh]'); if (!root) return;
    const $ = id => document.getElementById(id);
    const slug = root.dataset.slug;

    // 학년 탭
    const tabs = document.querySelectorAll('.rb-tabs button');
    const blocks = document.querySelectorAll('#rbCards .rb-blk[data-g]');
    let lastShown = false;
    function show(g) {
        tabs.forEach(b => b.classList.toggle('on', b.dataset.g === g));
        blocks.forEach(s => {
            const k = s.dataset.g;
            s.classList.toggle('rb-hide', k === 'last' ? !(lastShown && g === 'all') : !(g === 'all' || g === k));
        });
    }
    tabs.forEach(b => b.addEventListener('click', () => show(b.dataset.g)));

    // 마지막으로 본 반(이 기기에만) — 그 반 카드를 위에 한 번 더
    try {
        const id = localStorage.getItem('rbLastClass');
        if (id && localStorage.getItem('rbLastSlug') === slug) {
            const card = document.querySelector('#rbCards .rb-card[data-cid="' + CSS.escape(id) + '"]');
            if (card) {
                const copy = card.cloneNode(true); copy.classList.add('last');
                const full = localStorage.getItem('rbLastName'); if (full) copy.querySelector('.cn b').textContent = full;
                $('rbLastGrid').appendChild(copy); lastShown = true;
            }
        }
    } catch (e) { }
    show('all');

    // 이름 찾기 — 칠 때마다 바로
    let all = [];
    try { all = JSON.parse($('rbAll').textContent || '[]'); } catch (e) { }
    const CHO = ['ㄱ','ㄲ','ㄴ','ㄷ','ㄸ','ㄹ','ㅁ','ㅂ','ㅃ','ㅅ','ㅆ','ㅇ','ㅈ','ㅉ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ'];
    const cho = s => Array.from(s).map(ch => { const c = ch.charCodeAt(0) - 0xAC00; return (c >= 0 && c <= 11171) ? CHO[Math.floor(c / 588)] : ch; }).join('');
    all.forEach(p => p.ch = cho(p.n));
    const esc = s => s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const input = $('rbFind');
    function find() {
        const t = (input.value || '').replace(/\s+/g, '');
        $('rbHits').classList.toggle('rb-hide', !t);
        $('rbCards').classList.toggle('rb-hide', !!t);
        if (!t) return;
        const choOnly = /^[ㄱ-ㅎ]+$/.test(t);
        const hits = all.filter(p => choOnly ? p.ch.includes(t) : p.n.includes(t))
            .sort((a, b) => ((choOnly ? a.ch : a.n).startsWith(t) ? 0 : 1) - ((choOnly ? b.ch : b.n).startsWith(t) ? 0 : 1) || a.n.localeCompare(b.n, 'ko'));
        $('rbHitT').textContent = '찾은 아이 ' + hits.length + '명';
        $('rbHitList').innerHTML = hits.length ? hits.slice(0, 50).map(p => {
            const i = choOnly ? p.ch.indexOf(t) : p.n.indexOf(t);
            const name = i < 0 ? esc(p.n) : esc(p.n.slice(0, i)) + '<mark>' + esc(p.n.slice(i, i + t.length)) + '</mark>' + esc(p.n.slice(i + t.length));
            return '<a class="rb-hit" href="/' + encodeURIComponent(slug) + '/classbook/' + p.c + '#p-' + p.i + '"><b>' + name + '</b><span>' + esc(p.cn) + '</span><em>›</em></a>';
        }).join('') + (hits.length > 50 ? '<div class="rb-none">50명까지만 보여요 · 이름을 더 입력해 보세요</div>' : '')
            : '<div class="rb-none">이 이름의 아이가 없어요.</div>';
    }
    input.addEventListener('input', find);
    $('rbClear').addEventListener('click', e => { e.preventDefault(); input.value = ''; history.replaceState(null, '', location.pathname); find(); input.focus(); });
    input.form.addEventListener('submit', e => { e.preventDefault(); find(); input.blur(); });
    if (input.value) find();
})();
