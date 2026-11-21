// 교적부 반 교적(R-361, 2026-11-21 확정 시안 교적부) — 유년부 /Views/ChurchRecord/Class.cshtml 의 스크립트를 옮겼다:
// 초성 검색 · 전체 / 정보 빠짐 / ○월 생일 거르기 · 마지막으로 본 반 · [정보 수정] 창(생일 · 전화 · 전화 2) · 저장 뒤 그 카드로.
(function () {
    const root0 = document.querySelector('[data-rb]'); if (!root0) return;
    const $ = id => document.getElementById(id);
    const SAVE = root0.dataset.save;
    try { localStorage.setItem('rbLastClass', root0.dataset.cid); localStorage.setItem('rbLastName', root0.dataset.cname); localStorage.setItem('rbLastSlug', root0.dataset.slug); } catch (e) { }
    const kids = Array.from(document.querySelectorAll('.rb-kid'));
    let filter = 'all';

    // ── 이름 찾기(초성) · 거르기 ──
    const CHO = ['ㄱ','ㄲ','ㄴ','ㄷ','ㄸ','ㄹ','ㅁ','ㅂ','ㅃ','ㅅ','ㅆ','ㅇ','ㅈ','ㅉ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ'];
    const cho = s => Array.from(s).map(ch => { const c = ch.charCodeAt(0) - 0xAC00; return (c >= 0 && c <= 11171) ? CHO[Math.floor(c / 588)] : ch; }).join('');
    kids.forEach(k => k.dataset.cho = cho(k.dataset.name));
    function apply() {
        const t = ($('rbQ').value || '').replace(/\s+/g, '');
        const choOnly = /^[ㄱ-ㅎ]+$/.test(t);
        let n = 0;
        kids.forEach(k => {
            const byName = !t || (choOnly ? k.dataset.cho.includes(t) : k.dataset.name.includes(t));
            const byF = filter === 'all' || (filter === 'miss' && k.dataset.miss === '1') || (filter === 'bday' && k.dataset.bday === '1');
            const show = byName && byF;
            k.classList.toggle('rb-hide', !show);
            if (show) n++;
        });
        if (kids.length) {
            $('rbNone').classList.toggle('rb-hide', n > 0);
            $('rbNone').textContent = filter === 'miss' && !t ? '정보가 빠진 아이가 없어요.' : '해당하는 아이가 없어요.';
        }
    }
    $('rbQ').addEventListener('input', apply);
    document.querySelectorAll('.rb-seg button').forEach(b => b.addEventListener('click', () => {
        filter = b.dataset.f;
        document.querySelectorAll('.rb-seg button').forEach(x => x.classList.toggle('on', x === b));
        apply();
    }));

    // ── 알림 ──
    let tt = null;
    function toast(text, bad) { const el = $('rbToast'); el.textContent = text; el.classList.toggle('bad', !!bad); el.classList.add('show'); clearTimeout(tt); tt = setTimeout(() => el.classList.remove('show'), 2200); }

    // ── 정보 수정 창 ──
    const root = document.documentElement, sheet = $('rbSheet'), btnSave = $('rbSave');
    let cur = null, opener = null, saving = false;
    function clearErr() { const f = $('rbFBirth'); f.classList.remove('err'); f.querySelector('.em').textContent = ''; $('rbShErr').textContent = ''; }
    ['rbMonth', 'rbDay'].forEach(id => $(id).addEventListener('input', clearErr));
    function open(card, btn) {
        if (saving) return;
        cur = card; opener = btn;
        $('rbShName').textContent = card.dataset.name;
        $('rbMonth').value = card.dataset.month || ''; $('rbDay').value = card.dataset.day || '';
        $('rbP1').value = card.dataset.p1 || ''; $('rbP2').value = card.dataset.p2 || '';
        clearErr();
        root.classList.add('rb-open'); sheet.setAttribute('aria-hidden', 'false');
        setTimeout(() => $('rbMonth').focus(), 60);
    }
    function close() {
        if (saving) return;
        root.classList.remove('rb-open'); sheet.setAttribute('aria-hidden', 'true');
        cur = null; if (opener) opener.focus({ preventScroll: true });
    }
    $('rbList').addEventListener('click', e => { const b = e.target.closest('.js-edit'); if (b) open(b.closest('.rb-kid'), b); });
    $('rbList').addEventListener('keydown', e => { if ((e.key === 'Enter' || e.key === ' ') && e.target.matches('b.js-edit')) { e.preventDefault(); open(e.target.closest('.rb-kid'), e.target); } });
    $('rbScrim').addEventListener('click', close);
    $('rbShClose').addEventListener('click', close);
    document.addEventListener('keydown', e => { if (e.key === 'Escape' && root.classList.contains('rb-open')) close(); });

    const int = v => { const s = String(v ?? '').trim(); if (!s) return null; const n = parseInt(s, 10); return Number.isFinite(n) ? n : null; };
    btnSave.addEventListener('click', async () => {
        if (!cur || saving) return;
        const m = int($('rbMonth').value), d = int($('rbDay').value);
        clearErr();
        if (m !== null || d !== null) {
            const last = m && m >= 1 && m <= 12 ? new Date(2024, m, 0).getDate() : 0;
            let msg = '';
            if (m === null || d === null) msg = '월 · 일을 모두 넣어 주세요.';
            else if (m < 1 || m > 12) msg = '월은 1 ~ 12로 넣어 주세요.';
            else if (d < 1 || d > last) msg = `${m}월은 1 ~ ${last}일까지예요.`;
            if (msg) { const f = $('rbFBirth'); f.classList.add('err'); f.querySelector('.em').textContent = msg; $('rbShErr').textContent = '빨간 칸을 확인해 주세요.'; return; }
        }
        saving = true; btnSave.disabled = true;
        try {
            const form = new URLSearchParams();
            form.set('__RequestVerificationToken', sheet.querySelector('input[name="__RequestVerificationToken"]')?.value || '');
            form.set('personId', cur.dataset.id);
            if (m !== null) form.set('month', m); if (d !== null) form.set('day', d);
            form.set('phone1', $('rbP1').value.trim()); form.set('phone2', $('rbP2').value.trim());
            const res = await fetch(SAVE, { method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8', 'Accept': 'application/json' }, body: form.toString() });
            const j = await res.json().catch(() => null);
            if (!res.ok || !j || !j.ok) throw new Error(j && j.message || '저장하지 못했어요');
            try { sessionStorage.setItem('rbSaved', cur.dataset.name); } catch (e) { }
            location.replace(location.pathname + location.search + '#p-' + cur.dataset.id);
            location.reload();
        } catch (e) {
            $('rbShErr').textContent = (e.message || '저장하지 못했어요') + ' · 다시 눌러 주세요';
            saving = false; btnSave.disabled = false;
        }
    });

    // 찾기 결과나 저장 뒤 "#p-아이" 로 들어오면 그 아이로 이동해서 잠깐 표시
    const h = location.hash.slice(1);
    if (h.indexOf('p-') === 0) {
        const el = document.getElementById(h);
        if (el) { el.scrollIntoView({ block: 'center' }); el.classList.add('flash'); setTimeout(() => el.classList.remove('flash'), 1600); }
    }
    try { const s = sessionStorage.getItem('rbSaved'); if (s) { sessionStorage.removeItem('rbSaved'); toast(`${s} 정보를 저장했어요`); } } catch (e) { }
})();
