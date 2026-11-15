// 영상 구간 편집(유년부 jumanna-segedit.js 와 같은 일, 확정 시안 퀴즈출제-말씀입력 ④):
// ▶ → 위 영상 창에 그 장 듣기 영상(YouTube IFrame API) → [시작은 지금] · [끝은 지금] → 바로 저장(?handler=Save).
(() => {
    const box = document.getElementById('wcPlayer');
    if (!box) return;
    const token = document.querySelector('#wcTok input[name="__RequestVerificationToken"]').value;
    let player = null, ready = false, row = null, tick = 0;
    const mmss = (s) => Math.floor(s / 60) + ':' + String(Math.floor(s % 60)).padStart(2, '0');

    const loadApi = () => new Promise((ok) => {
        if (window.YT && window.YT.Player) return ok();
        const prev = window.onYouTubeIframeAPIReady;
        window.onYouTubeIframeAPIReady = () => { if (prev) prev(); ok(); };
        const s = document.createElement('script');
        s.src = 'https://www.youtube.com/iframe_api';
        document.head.appendChild(s);
    });

    const open = async (r) => {
        row = r;
        box.hidden = false;
        document.getElementById('wcTitle').textContent = r.dataset.title;
        const start = Number(r.dataset.s || 0);
        showCur();
        document.body.style.overflow = 'hidden';
        await loadApi();
        if (!player) {
            player = new YT.Player('wcYt', {
                host: 'https://www.youtube-nocookie.com', videoId: r.dataset.yt,
                playerVars: { start, rel: 0, playsinline: 1 },
                events: { onReady: () => { ready = true; } },
            });
        } else if (ready) player.loadVideoById({ videoId: r.dataset.yt, startSeconds: start });
        clearInterval(tick);
        tick = setInterval(() => { if (ready && player.getCurrentTime) document.getElementById('wcNow').textContent = mmss(player.getCurrentTime()); }, 500);
    };

    const save = async (which, sec) => {
        const body = new URLSearchParams({ date: row.dataset.date, chapter: row.dataset.ch, which, sec: sec == null ? '' : String(sec), __RequestVerificationToken: token });
        const res = await fetch('?handler=Save', { method: 'POST', body, credentials: 'same-origin' });
        if (!res.ok) { alert('저장하지 못했어요. 다시 해 주세요.'); return false; }
        return true;
    };

    const paint = (which, sec) => {
        const v = row.querySelector('[data-v="' + which + '"]');
        row.dataset[which] = sec == null ? '' : String(sec);
        v.classList.toggle('def', sec == null);
        v.textContent = sec != null ? mmss(sec) : which === 's' ? '0:00' : (row.dataset.len ? mmss(Number(row.dataset.len)) : '끝');
        v.classList.add('saved'); setTimeout(() => v.classList.remove('saved'), 900);
        showCur();
    };

    // 팝업 안에 지금 정한 시작 · 끝을 보인다
    const showCur = () => {
        if (!row) return;
        document.getElementById('wcS').textContent = row.dataset.s ? mmss(Number(row.dataset.s)) : '0:00';
        document.getElementById('wcE').textContent = row.dataset.e ? mmss(Number(row.dataset.e)) : (row.dataset.len ? mmss(Number(row.dataset.len)) : '끝');
    };
    const close = () => { if (player && ready) player.pauseVideo(); box.hidden = true; clearInterval(tick); document.body.style.overflow = ''; };
    box.addEventListener('click', (e) => { if (e.target === box) close(); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !box.hidden) close(); });

    document.addEventListener('click', async (e) => {
        const play = e.target.closest('.wc-play:not(.off)');
        if (play) { open(play.closest('.wc-row')); return; }
        const b = e.target.closest('[data-set]');
        if (!b || !row) return;
        const k = b.dataset.set;
        if (k === 'close') { close(); return; }
        if (k === 'reset') {
            if (await save('s', null) && await save('e', null)) { paint('s', null); paint('e', null); }
            return;
        }
        if (!ready) return;
        const sec = Math.floor(player.getCurrentTime());
        if (await save(k, sec)) {
            paint(k, sec);
            if (k === 's' && row.dataset.e && Number(row.dataset.e) <= sec) paint('e', null);   // 끝이 시작보다 앞이면 서버가 끝을 기본으로 돌린다
        }
    });
})();
