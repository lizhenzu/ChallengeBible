// 페이지 이동 대기 카드(ADR 0082, 확정 시안 wait-card.png 1안).
// 누르면 진행 줄과 카드를 바로 보여 준다. 링크는 카드가 보이도록 0.6초 뒤에 옮기고, 폼은 늦추지 않는다.
// 그 자리에서 처리하는 동작(preventDefault) · 새 창 · 다운로드 · data-nowait 에는 나오지 않는다.
// 다른 처리기가 모두 돈 뒤에 판단하도록 window 에서 듣는다.
(() => {
    const LINK_HOLD_MS = 600;
    const GIVE_UP_MS = 15000;   // 이동이 일어나지 않은 경우(응답이 파일 등) 카드가 남지 않게
    const MOVING = '이동하고 있어요';
    const ME = '내 정보를 확인하고 있어요';

    // 슬러그 뒤 주소(/{slug}/quiz → quiz)에 맞는 문구. 위에서부터 먼저 맞는 것을 쓴다.
    const IN_TENANT = [
        [/^reading\/read(\/|$)/, '성경을 펼치고 있어요'],
        [/^reading(\/|$)/, '통독 기록을 찾고 있어요'],
        [/^(word|read)$/, '말씀을 펼치고 있어요'],
        [/^quiz$/, '퀴즈를 준비하고 있어요'],
        [/^prayer$/, '기도 시간을 준비하고 있어요'],
        [/^recite$/, '암송 구절을 꺼내고 있어요'],
        [/^word-mission$/, '오늘의 미션을 불러오고 있어요'],
        [/^calendar$/, '달력을 넘기고 있어요'],
        [/^(points|ranking)$/, '점수를 모으고 있어요'],
    ];

    const clean = (pathname) => pathname.replace(/^\/+|\/+$/g, '').toLowerCase();

    const forPage = (pathname) => {
        const p = clean(pathname);
        if (p === 'account/logout' || p === 'account/impersonate/stop') return MOVING;
        if (p === 'account' || p.startsWith('account/')) return ME;
        const rest = p.slice(p.indexOf('/') + 1);
        if (!p.includes('/')) return MOVING;
        const hit = IN_TENANT.find(([rx]) => rx.test(rest));
        return hit ? hit[1] : MOVING;
    };

    const forPost = (pathname) => {
        const p = clean(pathname);
        if (p === 'account/login' || p === 'account/signup') return ME;
        if (p === 'account/logout' || p === 'account/impersonate/stop') return MOVING;
        return '저장하고 있어요';
    };

    const line = document.createElement('div');
    line.className = 'wline';
    line.setAttribute('aria-hidden', 'true');
    line.innerHTML = '<i></i>';

    const wait = document.createElement('div');
    wait.className = 'wcard';
    wait.setAttribute('role', 'status');
    wait.setAttribute('aria-live', 'polite');
    wait.innerHTML = '<div class="card">'
        + '<svg class="bk" width="80" height="60" viewBox="0 0 80 60" aria-hidden="true">'
        + '<path class="cover" d="M40 12C31 5 18 4 6 6v44c12-2 25-1 34 6 9-7 22-8 34-6V6C62 4 49 5 40 12z"/>'
        + '<path class="spine" d="M40 12v44"/>'
        + '<path class="txt" d="M14 18c7-1 13 0 18 2M14 28c7-1 13 0 18 2M14 38c7-1 13 0 18 2M66 18c-7-1-13 0-18 2M66 28c-7-1-13 0-18 2M66 38c-7-1-13 0-18 2"/>'
        + '</svg>'
        + '<div class="t">잠시만 기다려 주세요</div><div class="s"></div>'
        + '<div class="dots"><i></i><i></i><i></i></div></div>';
    document.body.append(line, wait);
    const sub = wait.querySelector('.s');

    let timers = [];
    const reset = () => {
        timers.forEach(clearTimeout);
        timers = [];
        line.classList.remove('on');
        wait.classList.remove('on');
    };
    // 뒤로 가기로 복원될 때만 닫는다. 처음 열릴 때의 pageshow 까지 받으면 로딩 중에 누른 링크의 이동이 취소된다.
    addEventListener('pageshow', (e) => { if (e.persisted) reset(); });

    const start = (message) => {
        reset();
        sub.textContent = message;
        line.classList.add('on');
        wait.classList.add('on');
        timers = [setTimeout(reset, GIVE_UP_MS)];
    };

    window.addEventListener('click', (e) => {
        if (e.defaultPrevented) return;
        const a = e.target.closest && e.target.closest('a[href]');
        if (!a || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        if ((a.target && a.target !== '_self') || a.hasAttribute('download') || a.hasAttribute('data-nowait')) return;
        const u = new URL(a.href, location.href);
        if (u.origin !== location.origin || (u.pathname === location.pathname && u.search === location.search)) return;
        e.preventDefault();
        start(forPage(u.pathname));
        timers.push(setTimeout(() => location.assign(u.href), LINK_HOLD_MS));
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
        start(method === 'post' ? forPost(u.pathname) : forPage(u.pathname));
    });
})();
