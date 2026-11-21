// 인명 검색(R-358, 2026-11-21 확정 시안 인명검색) — 유년부 /Views/AddressBook/Index.cshtml 의 스크립트를 그대로 옮겼다.
// 다른 점: 주소(검색 · 쪽지)는 화면의 data- 값에서, 사진이 없으면 이름 두 글자, "학생" 꼬리표는 반 이름(없으면 "학생").
(function () {
    const root0 = document.querySelector('[data-pb]'); if (!root0) return;
    const MIN_LEN = 2, TAKE = 30, DEBOUNCE_MS = 350;
    const THIS_MONTH = parseInt(root0.dataset.month || '0', 10);
    const SEARCH = root0.dataset.search, SEND = root0.dataset.send;
    const SENT_TTL_MS = 10 * 60 * 1000;               // "방금 보냄" 표시 10분
    const BLOCK_WORDS = ['욕설1', '욕설2', '비속어', '광고', '도박', '음란']; // 금칙어(예시, 유년부와 같다)
    const RECENT_KEY = 'addr_recent_v2', RECENT_MAX = 8;

    const $ = id => document.getElementById(id);
    const q = $('pbQ'), qClear = $('pbClear'), spin = $('pbSpin'), seg = $('pbSeg');
    const idle = $('pbIdle'), result = $('pbResult'), list = $('pbList'), none = $('pbNone');
    let items = [], filter = 'all', timer = null, aborter = null, lastQ = '';

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const val = v => (v === null || v === undefined) ? '' : String(v).trim();
    const isStudent = it => !!it.student;
    const digits = s => val(s).replace(/\D/g, '');
    function fmtPhone(s) {
        const d = digits(s);
        if (d.length === 11) return d.replace(/(\d{3})(\d{4})(\d{4})/, '$1-$2-$3');
        if (d.length === 10) return d.startsWith('02') ? d.replace(/(\d{2})(\d{4})(\d{4})/, '$1-$2-$3') : d.replace(/(\d{3})(\d{3})(\d{4})/, '$1-$2-$3');
        if (d.length === 9 && d.startsWith('02')) return d.replace(/(\d{2})(\d{3})(\d{4})/, '$1-$2-$3');
        return val(s);
    }
    function hl(text, term) {
        const t = val(text); if (!term) return esc(t);
        const i = t.toLowerCase().indexOf(term.toLowerCase());
        return i < 0 ? esc(t) : esc(t.slice(0, i)) + '<mark>' + esc(t.slice(i, i + term.length)) + '</mark>' + esc(t.slice(i + term.length));
    }
    const short = n => { const b = n.length > 1 ? n.slice(1) : n; return b.length > 2 ? b.slice(0, 2) : b; };

    // ── 알림 ──
    let tt = null;
    function toast(text, bad) {
        const el = $('pbToast'); el.textContent = text; el.classList.toggle('bad', !!bad); el.classList.add('show');
        clearTimeout(tt); tt = setTimeout(() => el.classList.remove('show'), 1800);
    }

    // ── 최근 검색 (이 기기에만 저장) ──
    function recentGet() { try { return JSON.parse(localStorage.getItem(RECENT_KEY) || '[]'); } catch (e) { return []; } }
    function recentSet(a) { try { localStorage.setItem(RECENT_KEY, JSON.stringify(a)); } catch (e) { } }
    function recentAdd(term) { recentSet([term, ...recentGet().filter(x => x !== term)].slice(0, RECENT_MAX)); }
    function drawRecent() {
        const a = recentGet();
        $('pbRecent').classList.toggle('pb-hide', a.length === 0);
        $('pbRecentList').innerHTML = a.map(x => `<button type="button" data-q="${esc(x)}">${esc(x)}</button>`).join('');
    }
    $('pbRecentList').addEventListener('click', e => { const b = e.target.closest('button[data-q]'); if (b) { q.value = b.dataset.q; run(); } });
    $('pbRecentClear').addEventListener('click', () => { recentSet([]); drawRecent(); });

    // ── "방금 보냄" ──
    const sentKey = u => `addr_sent_${u}`;
    function markSent(u) { try { localStorage.setItem(sentKey(u), String(Date.now())); } catch (e) { } }
    function recentlySent(u) { try { const t = parseInt(localStorage.getItem(sentKey(u)) || '0', 10); return t > 0 && Date.now() - t <= SENT_TTL_MS; } catch (e) { return false; } }

    // ── 결과 그리기 ──
    function row(it, i) {
        const name = val(it.fullName) || '(이름 없음)';
        const user = val(it.userName), phone = val(it.phoneNumber), email = val(it.email);
        const d = digits(phone);
        const st = isStudent(it);
        const bday = (it.bMonth && it.bDay) ? `${it.bMonth}월 ${it.bDay}일` : '';
        const bdayNow = it.bMonth === THIS_MONTH;
        const photo = val(it.photoUrl);
        const dd = (v, s) => v ? `<dd>${s ?? esc(v)}</dd>` : `<dd class="no">—</dd>`;
        return `<article class="pb-p" data-i="${i}" data-kind="${st ? 'st' : 'etc'}" data-user="${esc(user)}">
            <div class="pb-ph">${photo ? `<img src="${esc(photo)}" alt="" loading="lazy" onerror="this.remove()">` : ''}${photo ? '' : esc(short(name))}</div>
            <div class="pb-bd">
                <div class="pb-nm"><b>${hl(name, lastQ)}</b>
                    ${st ? `<span class="pb-tg st">${esc(val(it.classText) || '학생')}</span>` : `<span class="pb-tg">선생님·기타</span>`}
                    ${bdayNow ? `<span class="pb-tg bd">이번 달 생일</span>` : ''}
                    ${user && recentlySent(user) ? `<span class="pb-tg sent">방금 쪽지 보냄</span>` : ''}
                </div>
                <dl class="pb-kv">
                    <dt>아이디</dt>${dd(user, hl(user, lastQ))}
                    <dt>생일</dt>${dd(bday)}
                    <dt>전화</dt>${dd(phone, esc(fmtPhone(phone)) + (d.length >= 9 ? `<a class="pb-call" href="tel:${d}" aria-label="${esc(name)}에게 전화 걸기" title="전화 걸기">전화</a>` : ''))}
                    <dt>이메일</dt>${dd(email)}
                </dl>
                <div class="pb-act">
                    ${d.length >= 9 ? `<a class="pri" href="tel:${d}">전화</a><a href="sms:${d}">문자</a>` : ''}
                    ${user ? `<button type="button" data-act="note">쪽지</button>` : ''}
                    ${d.length >= 9 ? `<button type="button" data-act="copy">번호 복사</button>` : ''}
                </div>
            </div>
        </article>`;
    }
    function applyFilter() {
        let shown = 0;
        list.querySelectorAll('.pb-p').forEach(p => { const ok = filter === 'all' || p.dataset.kind === filter; p.classList.toggle('pb-hide', !ok); if (ok) shown++; });
        $('pbShown').textContent = shown;
        none.classList.toggle('pb-hide', shown > 0);
        if (!shown) none.textContent = filter === 'st' ? '학생 결과가 없어요.' : '선생님·기타 결과가 없어요.';
    }
    function draw() {
        const st = items.filter(isStudent).length;
        $('pbAll').textContent = items.length; $('pbSt').textContent = st; $('pbEtc').textContent = items.length - st;
        list.innerHTML = items.map(row).join('');
        $('pbMore').classList.toggle('pb-hide', items.length < TAKE);
        applyFilter();
    }
    seg.addEventListener('click', e => {
        const b = e.target.closest('button[data-f]'); if (!b) return;
        filter = b.dataset.f;
        seg.querySelectorAll('button').forEach(x => x.classList.toggle('on', x === b));
        applyFilter();
    });

    function showIdle() {
        idle.classList.remove('pb-hide'); result.classList.add('pb-hide'); seg.classList.add('pb-hide');
        drawRecent();
    }
    function showMsg(text) {
        idle.classList.add('pb-hide'); result.classList.remove('pb-hide'); seg.classList.add('pb-hide');
        list.innerHTML = ''; $('pbMore').classList.add('pb-hide'); $('pbShown').textContent = 0;
        none.textContent = text; none.classList.remove('pb-hide');
    }

    // ── 검색 ──
    async function run() {
        const term = q.value.trim();
        qClear.classList.toggle('pb-hide', !term);
        if (term.length < MIN_LEN) { if (aborter) aborter.abort(); spin.classList.add('pb-hide'); term ? showMsg('2글자 이상 입력해 주세요.') : showIdle(); return; }
        if (aborter) aborter.abort();
        aborter = new AbortController();
        spin.classList.remove('pb-hide');
        try {
            const res = await fetch(`${SEARCH}&q=${encodeURIComponent(term)}&take=${TAKE}`, { signal: aborter.signal, headers: { 'Accept': 'application/json' }, credentials: 'include' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const data = await res.json();
            items = (data && data.items) || [];
            lastQ = term;
            spin.classList.add('pb-hide');
            if (!items.length) { showMsg(`'${term}' 검색 결과가 없어요.`); return; }
            recentAdd(term);
            idle.classList.add('pb-hide'); result.classList.remove('pb-hide'); seg.classList.remove('pb-hide');
            draw();
        } catch (e) {
            if (e.name === 'AbortError') return;
            spin.classList.add('pb-hide');
            showMsg('검색하지 못했어요. 잠시 뒤 다시 해 주세요.');
        }
    }
    q.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(run, DEBOUNCE_MS); });
    q.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); clearTimeout(timer); run(); } });
    qClear.addEventListener('click', () => { q.value = ''; run(); q.focus(); });

    // ── 카드 버튼: 번호 복사 · 쪽지 ──
    list.addEventListener('click', async e => {
        const b = e.target.closest('button[data-act]'); if (!b) return;
        const it = items[+b.closest('.pb-p').dataset.i]; if (!it) return;
        if (b.dataset.act === 'copy') {
            const text = fmtPhone(it.phoneNumber);
            try { await navigator.clipboard.writeText(text); toast(`${val(it.fullName)} 번호를 복사했어요 · ${text}`); }
            catch (err) { toast('복사하지 못했어요 · ' + text, true); }
        } else if (b.dataset.act === 'note') openNote(it, b);
    });

    // ── 쪽지 보내기 ──
    const root = document.documentElement, sheet = $('pbSheet'), msgIn = $('pbMsgIn'), btnSend = $('pbSend'), shErr = $('pbShErr');
    let to = null, opener = null, sending = false;
    function openNote(it, btn) {
        to = it; opener = btn;
        $('pbToName').textContent = val(it.fullName) || val(it.userName);
        $('pbToSub').textContent = `${val(it.userName)} · 쪽지함으로 보내요`;
        msgIn.value = ''; $('pbMsgCnt').textContent = 0; shErr.textContent = '';
        root.classList.add('pb-open'); sheet.setAttribute('aria-hidden', 'false');
        setTimeout(() => msgIn.focus(), 60);
    }
    function closeNote() {
        if (sending) return;
        root.classList.remove('pb-open'); sheet.setAttribute('aria-hidden', 'true');
        to = null; if (opener) opener.focus({ preventScroll: true });
    }
    $('pbScrim').addEventListener('click', closeNote);
    $('pbShClose').addEventListener('click', closeNote);
    msgIn.addEventListener('input', () => { $('pbMsgCnt').textContent = msgIn.value.length; shErr.textContent = ''; });
    document.addEventListener('keydown', e => {
        if (!root.classList.contains('pb-open')) return;
        if (e.key === 'Escape') closeNote();
        if (e.key === 'Enter' && e.ctrlKey) send();
    });
    btnSend.addEventListener('click', send);
    async function send() {
        if (!to || sending) return;
        const text = msgIn.value.replace(/\r\n?/g, '\n').trim();
        if (!text) { shErr.textContent = '내용을 입력해 주세요.'; msgIn.focus(); return; }
        const bad = BLOCK_WORDS.find(w => text.toLowerCase().includes(w.toLowerCase()));
        if (bad) { shErr.textContent = `쓸 수 없는 말이 들어 있어요: ${bad}`; return; }
        sending = true; btnSend.disabled = true; btnSend.classList.add('busy');
        try {
            const form = new URLSearchParams();
            form.set('__RequestVerificationToken', sheet.querySelector('input[name="__RequestVerificationToken"]')?.value || '');
            form.set('ToUserName', val(to.userName));
            form.set('MessageText', text);
            const res = await fetch(SEND, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8', 'Accept': 'application/json' }, credentials: 'include', body: form.toString() });
            const data = await res.json().catch(() => null);
            if (!res.ok || !data || data.ok !== true) throw new Error(data && data.message || '보내지 못했어요');
            markSent(val(to.userName));
            const who = val(to.fullName) || val(to.userName);
            sending = false; btnSend.disabled = false; btnSend.classList.remove('busy');
            closeNote();
            toast(`${who}에게 쪽지를 보냈어요`);
            draw();
        } catch (err) {
            shErr.textContent = (err.message || '보내지 못했어요') + ' · 다시 눌러 주세요';
            sending = false; btnSend.disabled = false; btnSend.classList.remove('busy');
        }
    }

    // 주소에 ?q= 가 있으면 바로 검색
    const init = new URLSearchParams(location.search).get('q');
    if (init) { q.value = init; run(); } else showIdle();
    q.focus({ preventScroll: true });
})();
