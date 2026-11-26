// 대기 카드(ADR 0082 → 2026-11-25 사용자 시안 「페이지 이동 대기 — 로딩 · 지연 · 오류」 적용).
// 링크를 누르면 0.2초 뒤 이동하며 카드를 바로 띄운다(로딩). 8초가 지나도 안 끝나면 「지연」(경과 시간), 25초가 지나도 이동하지 않으면
// 「오류」(다시 시도 · 홈으로 돌아가기). 폼은 늦추지 않는다. 그 자리에서 처리하는 동작(preventDefault) · 새 창 · 다운로드 · data-nowait 에는 나오지 않는다.
// 화면을 옮기지 않는 큰 동작(읽음 표시 · 사진 올리기)에서는 window.cbWait(during · show · text · hide)로 부른다.
(() => {
    const LINK_HOLD_MS = 200;     // 링크 이동 늦춤(2026-11-25 사용자 "0.2 딜레이")
    const LATE_MS = 8000;         // 이 시간이 지나면 「지연」
    const FAIL_MS = 25000;        // 링크 이동이 이 시간이 지나도 안 일어나면 「오류」
    const GIVE_UP_MS = 30000;     // 폼 · 큰 동작: 카드가 남지 않게
    const HOLD_ACT_MS = 600;      // 큰 동작(읽음 표시)은 카드가 깜빡이지 않게 최소 이만큼 보인다
    const MOVING = '이동 중입니다';
    const WAIT = '기다려 주세요';
    const LATE = '평소보다 조금 오래 걸리고 있어요';

    const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
    const clean = (pathname) => pathname.replace(/^\/+|\/+$/g, '').toLowerCase();
    const forPost = (pathname) => {
        const p = clean(pathname);
        return (p === 'account/logout' || p === 'account/impersonate/stop') ? MOVING : '저장하고 있어요';
    };
    // 파일을 받는 주소는 이동이 아니라 카드를 띄우지 않는다
    const isFile = (u) => /(export|download|handler=(export|download))/i.test(u.pathname + u.search);

    // 네잎클로버 + 점 세 개(초록 · 파랑 · 분홍 = 「도전!!!」 삼색)가 하나씩 차례로 깜빡인다(2026-11-26 사용자 시안 「대기카드-클로버-점」)
    const MARK = '<span class="mark" aria-hidden="true"><img class="cl" src="/icons/clover.svg" alt="" width="64" height="64"/><span class="dots"><i></i><i></i><i></i></span></span>';
    const ERR = '<span class="err" aria-hidden="true"><svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round">'
        + '<path d="M12 7.5v6.2"/><circle cx="12" cy="17.2" r="1.15" fill="currentColor" stroke="none"/></svg></span>';

    const wait = document.createElement('div');
    wait.className = 'wcard';
    wait.setAttribute('role', 'status');
    wait.setAttribute('aria-live', 'polite');
    wait.innerHTML = '<div class="card"></div>';
    document.body.append(wait);
    const card = wait.querySelector('.card');

    let timers = [];
    let ticker = 0;
    let started = 0;
    const reset = () => {
        timers.forEach(clearTimeout);
        timers = [];
        clearInterval(ticker);
        wait.classList.remove('on');
    };
    // 뒤로 가기로 복원될 때만 닫는다. 처음 열릴 때의 pageshow 까지 받으면 로딩 중에 누른 링크의 이동이 취소된다.
    addEventListener('pageshow', (e) => { if (e.persisted) reset(); });

    // 통독 · 필사 화면은 사용자가 고른 테마 색을 따른다(카드는 .rp 밖 body 에 있어 값을 옮겨 준다). 테마를 그 자리에서 바꿀 수 있어 띄울 때마다 읽는다
    const THEME = [['--accent', '--r-acc'], ['--accent-bg', '--r-accSoft'], ['--bg', '--bg'], ['--fg', '--r-ink'], ['--dim', '--r-dim']];
    const followTheme = () => {
        const rp = document.querySelector('.rp');
        if (!rp) return;
        const cs = getComputedStyle(rp);
        for (const [to, from] of THEME) {
            const value = cs.getPropertyValue(from).trim();
            if (value) wait.style.setProperty(to, value);
        }
    };

    const showLoad = (title, msg) => {
        card.innerHTML = MARK + '<p class="card-title">' + esc(title) + '</p><p class="card-msg">' + esc(msg) + '</p>';
        wait.classList.add('on');
    };
    const showLate = (title) => {
        const secs = () => Math.max(1, Math.round((Date.now() - started) / 1000));
        card.innerHTML = MARK + '<p class="card-title">' + esc(title) + '</p><p class="card-msg">' + LATE
            + '</p><p class="card-meta"><span data-sec>' + secs() + '</span>초 지났어요</p>';
        const meta = card.querySelector('[data-sec]');
        ticker = setInterval(() => { if (meta) meta.textContent = String(secs()); }, 1000);
    };
    const showError = (url) => {
        clearInterval(ticker);
        card.innerHTML = ERR + '<p class="card-title">이동하지 못했어요</p><p class="card-msg">연결을 확인하고 다시 시도해 주세요</p>'
            + '<button class="btn" type="button" data-retry>다시 시도</button><a class="btn-quiet" href="/" data-nowait>홈으로 돌아가기</a>';
        card.querySelector('[data-retry]').addEventListener('click', () => go(url, '다시 이동하고 있어요'));
    };

    // 카드를 띄우고 시간 단계(지연 · 오류)를 건다. url 이 있으면 링크 이동이라 끝내 안 되면 오류
    const start = (title, opts = {}) => {
        reset();
        followTheme();
        started = Date.now();
        showLoad(title, WAIT);
        timers.push(setTimeout(() => showLate(title), LATE_MS));
        if (opts.url) timers.push(setTimeout(() => showError(opts.url), FAIL_MS));
        else if (!opts.keep) timers.push(setTimeout(reset, GIVE_UP_MS));
    };
    const go = (url, title) => {
        start(title, { url });
        timers.push(setTimeout(() => location.assign(url), LINK_HOLD_MS));
    };

    // 화면을 옮기지 않는 큰 동작(읽음 표시 등)에서도 쓴다. during: 카드를 띄우고 작업과 최소 시간을 기다린다. 닫는 것은 부르는 쪽에서(hide)
    const hold = () => new Promise((r) => setTimeout(r, HOLD_ACT_MS));
    window.cbWait = {
        hide: reset,
        // 오래 걸리는 올리기(사진 등): 카드를 띄운 채로 두고(30초에 닫지 않음) 문구만 바꾼다. 닫는 것은 부르는 쪽에서(hide)
        show: (message) => start(message, { keep: true }),
        text: (message) => { const t = card.querySelector('.card-title'); if (t) t.textContent = message; },
        during: async (message, work) => {
            start(message);
            const [res] = await Promise.all([work.catch(() => ({ ok: false, json: {} })), hold()]);
            return res;
        },
    };

    window.addEventListener('click', (e) => {
        if (e.defaultPrevented) return;
        const a = e.target.closest && e.target.closest('a[href]');
        if (!a || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        if (a.closest('.wcard')) return;
        if ((a.target && a.target !== '_self') || a.hasAttribute('download') || a.hasAttribute('data-nowait')) return;
        const u = new URL(a.href, location.href);
        if (u.origin !== location.origin || (u.pathname === location.pathname && u.search === location.search) || isFile(u)) return;
        e.preventDefault();
        go(u.href, MOVING);
    });

    window.addEventListener('submit', (e) => {
        if (e.defaultPrevented) return;
        const f = e.target;
        const by = e.submitter;
        if (f.hasAttribute('data-nowait') || (by && by.hasAttribute('data-nowait'))) return;
        if ((by && by.getAttribute('formtarget')) === '_blank' || f.getAttribute('target') === '_blank') return;
        const action = (by && by.getAttribute('formaction')) || f.getAttribute('action') || location.href;
        const method = ((by && by.getAttribute('formmethod')) || f.getAttribute('method') || 'get').toLowerCase();
        const u = new URL(action, location.href);
        if (isFile(u)) return;
        start(method === 'post' ? forPost(u.pathname) : MOVING);
    });
})();
