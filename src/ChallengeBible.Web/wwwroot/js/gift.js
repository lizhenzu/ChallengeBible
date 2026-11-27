// 개인 선물(확정 시안 2판 말씀-선물고르기, ADR 0094 B · 정한 것 8, R-285).
// ① 미션 홈: 올해 모은 점수가 100점 단위를 새로 넘었을 때 창 한 번(이 기기에 마지막으로 본 단계를 적어 둔다). 확정 시안 선물점수-15크레딧(ADR 0104).
// ③ → ④ 고르기 화면: 선물을 누르면 확인 창(남는 점수 — 모두 남는다, 정한 것 9), [이 선물로 할게요]에서 보낸다. 스크립트가 없으면 바로 보낸다.
// ⑤ 선물 취소도 확인 창을 거친다.
(() => {
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const n = (v) => Number(v).toLocaleString('ko-KR');

    const mint = !!document.querySelector('.gk-pg.mint');   // 민트 시안(2026-11-27) 선물 화면의 창
    const open = (cls, html, onOk) => {
        const bg = document.createElement('div');
        bg.className = 'gk-dim ' + cls + (mint ? ' mint' : '');
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
            if (sure || !b || !(b.dataset.need || b.dataset.team)) return;
            e.preventDefault();
            const d = b.dataset;
            if (d.team) {   // 가족팀 · 친구팀 선물(확정 시안 가족팀-선물고르기 ③) · 생일 선물(확정 시안 생일-선물): 점수 · 금액 없이
                const bday = d.team === 'birthday', nc = d.team === 'newcomer' || d.team === 'invite', rd = d.team === 'reading';
                const label = rd ? '완독 선물' : bday ? '생일 선물' : d.team === 'newcomer' ? '새친구 선물' : d.team === 'invite' ? '전도 선물' : d.team === 'friend' ? '친구팀 선물' : '가족 선물';
                const note = rd ? '성경 완독을 축하하는 선물이에요. 선생님이 사기 전까지는 다른 선물로 바꿀 수 있어요.' : bday ? '생일 축하 선물이에요. 선생님이 사기 전까지는 다른 선물로 바꿀 수 있어요.' : nc ? '고마운 마음을 담은 선물이에요. 선생님이 사기 전까지는 다른 선물로 바꿀 수 있어요.'
                    : d.team === 'friend' ? '내가 받을 선물이에요. 선생님이 사기 전까지는 바꿀 수 있어요.' : '우리 가족이 함께 받는 선물이에요. 선생님이 사기 전까지는 가족 누구나 다른 선물로 바꿀 수 있어요.';
                open('gk-bottom', `<section class="gk-sheet" role="dialog" aria-modal="true" aria-labelledby="gkT">
                    <div class="gk-top"><span class="gk-pic">${d.img ? `<img src="${esc(d.img)}" alt="" onerror="this.remove()">` : ''}</span>
                      <div><h2 id="gkT">${esc(d.name)}</h2><div class="gk-need" style="color:${rd ? '#D97706' : bday ? '#DB2777' : nc ? '#16A34A' : '#7C3AED'}">${label}</div></div></div>
                    ${d.desc ? `<p class="gk-desc">${esc(d.desc)}</p>` : ''}
                    <p class="gk-sn" style="padding:10px 0;border-top:1px solid #E5E7EB;border-bottom:1px solid #E5E7EB">${note}</p>
                    <div class="gk-row2"><button type="button" class="gk-btn gk-ghost" data-close>닫기</button><button type="button" class="gk-btn ${rd ? 'gk-orange' : bday ? 'gk-pink' : nc ? 'gk-green' : 'gk-pur'}" data-ok>이 선물로 할게요</button></div>
                  </section>`, () => { sure = true; pick.requestSubmit(b); });
                return;
            }
            open('gk-bottom', `<section class="gk-sheet" role="dialog" aria-modal="true" aria-labelledby="gkT">
                <div class="gk-top"><span class="gk-pic">${d.img ? `<img src="${esc(d.img)}" alt="" onerror="this.remove()">` : ''}</span>
                  <div><h2 id="gkT">${esc(d.name)}</h2><div class="gk-need">${mint ? `<i class="gk-kind">내 선물</i> ` : ''}${n(d.need)}점</div></div></div>
                ${d.desc ? `<p class="gk-desc">${esc(d.desc)}</p>` : ''}
                ${mint ? `<p class="gk-sn" style="padding:10px 0;border-top:1px solid #E3EAE8;border-bottom:1px solid #E3EAE8">고르면 쓸 수 있는 점수에서 ${n(d.need)}점이 빠져요.</p>` : `<div class="gk-calc"><div><span>지금 점수</span><span>${n(d.now)}점</span></div>
                  <div><span>이 선물</span><span>− ${n(d.need)}점</span></div>
                  <div class="gk-sum"><span>고른 뒤 남는 점수</span><span>${n(d.left)}점</span></div></div>
                <p class="gk-sn">남은 점수는 그대로 모여요. 이 선물을 받으면 다음 선물을 고를 수 있어요.</p>`}
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
            // 확정 시안 선물점수-15크레딧(ADR 0104): 큰 숫자 = 쓸 수 있는 점수, 아래 올해 모은 · 쓴 · 사용 가능. 학생이 아니면 올해 모은 점수만
            const sum = `<div class="gk-sum3"><div><span>올해 모은</span><b>${n(d.year)}</b></div><div><span>쓴</span><b>${n(d.spent)}</b></div><div class="on"><span>사용 가능</span><b>${n(d.usable)}</b></div></div>`
                + (Number(d.cap) > 0 ? `<p class="gk-capn">올해는 ${n(d.cap)}점까지 선물에 써요</p>` : '');
            const later = '<button type="button" class="gk-btn gk-ghost" data-close>나중에 할게요</button>';
            const body = d.kind === 'pick'
                ? `${sum}<h2 id="gkT">선물을 고를 수 있어요</h2><p>지금 고르거나, 더 모아서<br>더 큰 선물을 골라도 돼요.</p>
                   <a class="gk-btn gk-pri" href="${esc(d.href)}" data-ok>선택 가능 선물 보러 가기<small>(${n(d.count)}개)</small></a>${later}`
                : d.kind === 'near'
                ? `${sum}<h2 id="gkT">${n(d.need)}점 더 모으면 선물을 골라요</h2><p>가장 가까운 선물까지 ${n(d.need)}점 남았어요.<br>미리 구경해 볼까요?</p>
                   <a class="gk-btn gk-pri" href="${esc(d.href)}?all=true" data-ok>선물 구경하기</a>${later}`
                : d.kind === 'wait'
                ? `${sum}<h2 id="gkT">잘하고 있어요</h2><p>고른 선물을 받으면<br>다음 선물을 골라요.</p><a class="gk-btn gk-pri" href="${esc(d.href)}" data-ok>내 선물 보기</a>${later}`
                : `<h2 id="gkT">잘하고 있어요</h2><p>꾸준히 함께해 줘서 고마워요.</p><button type="button" class="gk-btn gk-pri" data-close>확인</button>`;
            const big = d.kind === 'clap'
                ? `<div class="gk-k">올해 모은 점수</div><div class="gk-big">${n(step * Number(d.unit))}<u>점</u></div>`
                : `<div class="gk-k">선물에 쓸 수 있는 점수</div><div class="gk-big">${n(d.usable)}<u>점</u></div>`;
            open('', `<section class="gk-pop" role="dialog" aria-modal="true" aria-labelledby="gkT">${big}${body}</section>`, null);
        }
    }
})();
