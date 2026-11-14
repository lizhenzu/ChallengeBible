// 개인 선물(확정 시안 2판 말씀-선물고르기, ADR 0094 B · 정한 것 8, R-285).
// ① 미션 홈: 100점 단위를 새로 넘었을 때 창 한 번(이 기기에 마지막으로 본 단계를 적어 둔다).
// ③ → ④ 고르기 화면: 선물을 누르면 확인 창(남는 점수 — 모두 남는다, 정한 것 9), [이 선물로 할게요]에서 보낸다. 스크립트가 없으면 바로 보낸다.
// ⑤ 선물 취소도 확인 창을 거친다.
(() => {
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const n = (v) => Number(v).toLocaleString('ko-KR');

    const open = (cls, html, onOk) => {
        const bg = document.createElement('div');
        bg.className = 'gk-dim ' + cls;
        bg.innerHTML = html;
        document.body.appendChild(bg);
        const close = () => bg.remove();
        bg.addEventListener('click', (e) => {
            if (e.target === bg || e.target.closest('[data-close]')) { e.preventDefault(); close(); }
            else if (e.target.closest('[data-ok]')) { if (onOk) { e.preventDefault(); close(); onOk(); } }
        });
        document.addEventListener('keydown', function k(e) { if (e.key === 'Escape') { close(); document.removeEventListener('keydown', k); } });
        (bg.querySelector('[data-ok]') || bg.querySelector('[data-close]'))?.focus();
    };

    // ④ 고르기 확인
    const pick = document.querySelector('[data-gk-pick]');
    if (pick) {
        let sure = false;
        pick.addEventListener('submit', (e) => {
            const b = e.submitter;
            if (sure || !b || !b.dataset.need) return;
            e.preventDefault();
            const d = b.dataset;
            open('gk-bottom', `<section class="gk-sheet" role="dialog" aria-modal="true" aria-labelledby="gkT">
                <div class="gk-top"><span class="gk-pic">${d.img ? `<img src="${esc(d.img)}" alt="" onerror="this.remove()">` : ''}</span>
                  <div><h2 id="gkT">${esc(d.name)}</h2><div class="gk-need">${n(d.need)}점</div></div></div>
                ${d.desc ? `<p class="gk-desc">${esc(d.desc)}</p>` : ''}
                <div class="gk-calc"><div><span>지금 점수</span><span>${n(d.now)}점</span></div>
                  <div><span>이 선물</span><span>− ${n(d.need)}점</span></div>
                  <div class="gk-sum"><span>고른 뒤 남는 점수</span><span>${n(d.left)}점</span></div></div>
                <p class="gk-sn">남은 점수는 그대로 모여요. 이 선물을 받으면 다음 선물을 고를 수 있어요.</p>
                <div class="gk-row2"><button type="button" class="gk-btn gk-ghost" data-close>닫기</button><button type="button" class="gk-btn gk-pri" data-ok>이 선물로 할게요</button></div>
              </section>`, () => { sure = true; pick.requestSubmit(b); });
        });
    }

    // ⑤ 취소 확인
    const cancel = document.querySelector('[data-gk-cancel]');
    if (cancel) {
        let sure = false;
        cancel.addEventListener('submit', (e) => {
            if (sure) return;
            e.preventDefault();
            open('', `<section class="gk-pop" role="dialog" aria-modal="true" aria-labelledby="gkT">
                <h2 id="gkT">선물을 취소할까요?</h2><p>쓴 점수가 다시 돌아와요.</p>
                <button type="button" class="gk-btn gk-line" data-ok>취소하기</button><button type="button" class="gk-btn gk-ghost" data-close>그대로 둘게요</button>
              </section>`, () => { sure = true; cancel.requestSubmit(); });
        });
    }

    // ② 100점 단위를 새로 넘었을 때 창 한 번
    const card = document.querySelector('[data-gk-pop]');
    if (card) {
        const d = card.dataset;
        const step = Number(d.step);
        let seen = null;
        try { seen = localStorage.getItem(d.key); } catch { }
        if (step >= 1 && (seen === null || Number(seen) < step)) {
            try { localStorage.setItem(d.key, String(step)); } catch { }
            const pts = step * Number(d.unit);
            const body = d.kind === 'pick'
                ? `<h2 id="gkT">선물을 고를 수 있어요</h2><p>지금 고르거나, 더 모아서<br>더 큰 선물을 골라도 돼요.</p>
                   <a class="gk-btn gk-pri" href="${esc(d.href)}" data-ok>선물 보러 가기</a><button type="button" class="gk-btn gk-ghost" data-close>나중에 할게요</button>`
                : d.kind === 'wait'
                ? `<h2 id="gkT">잘하고 있어요</h2><p>고른 선물을 받으면<br>모은 점수로 다음 선물을 골라요.</p><button type="button" class="gk-btn gk-pri" data-close>확인</button>`
                : `<h2 id="gkT">잘하고 있어요</h2><p>꾸준히 함께해 줘서 고마워요.</p><button type="button" class="gk-btn gk-pri" data-close>확인</button>`;
            open('', `<section class="gk-pop" role="dialog" aria-modal="true" aria-labelledby="gkT">
                <div class="gk-k">올해 모은 점수</div><div class="gk-big">${n(pts)}<u>점</u></div>${body}</section>`, null);
        }
    }
})();
