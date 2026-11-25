// 대기 카드(ADR 0082, 확정 시안 wait-card.png 1안). 2026-11-25 사용자 "페이지 이동마다 느리게 하는 효과 삭제, 바로 이동":
// 링크 · 폼 이동에는 더 이상 카드를 띄우지 않는다(링크는 바로 이동). 화면을 옮기지 않는 큰 동작(읽음 표시 · 사진 올리기)에서만
// window.cbWait(during · show · text · hide)로 부른다.
(() => {
    const LINK_HOLD_MS = 600;
    const GIVE_UP_MS = 15000;   // 이동이 일어나지 않은 경우(응답이 파일 등) 카드가 남지 않게

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

    // 통독 화면은 사용자가 고른 테마 색을 따른다(카드는 .rp 밖 body 에 있어 값을 옮겨 준다). 테마를 그 자리에서 바꿀 수 있어 띄울 때마다 읽는다
    const THEME = [['--accent', '--r-acc'], ['--accent-bg', '--r-accSoft'], ['--bg', '--bg'], ['--fg', '--r-ink'], ['--dim', '--r-dim']];
    const followTheme = () => {
        const rp = document.querySelector('.rp');
        if (!rp) return;
        const cs = getComputedStyle(rp);
        for (const [to, from] of THEME) {
            const value = cs.getPropertyValue(from).trim();
            if (value) { wait.style.setProperty(to, value); line.style.setProperty(to, value); }
        }
    };

    const start = (message, keep) => {
        reset();
        followTheme();
        sub.textContent = message;
        line.classList.add('on');
        wait.classList.add('on');
        if (!keep) timers = [setTimeout(reset, GIVE_UP_MS)];
    };

    // 화면을 옮기지 않는 큰 동작(읽음 표시 등)에서도 쓴다. during: 카드를 띄우고 작업과 최소 시간을 기다린다. 닫는 것은 부르는 쪽에서(hide)
    const hold = () => new Promise((r) => setTimeout(r, LINK_HOLD_MS));
    window.cbWait = {
        hide: reset,
        // 오래 걸리는 올리기(사진 등): 카드를 띄운 채로 두고(15초에 닫지 않음) 문구만 바꾼다. 닫는 것은 부르는 쪽에서(hide)
        show: (message) => start(message, true),
        text: (message) => { sub.textContent = message; },
        during: async (message, work) => {
            start(message);
            const [res] = await Promise.all([work.catch(() => ({ ok: false, json: {} })), hold()]);
            return res;
        },
    };
})();
