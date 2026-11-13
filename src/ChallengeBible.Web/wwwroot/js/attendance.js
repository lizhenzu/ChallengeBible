// 출석(ADR 0093 출석, 유년부 출석부 "오늘"과 같은 동작): 누르면 바로 저장 · 실패하면 되돌리고 알림 · 초성 검색 · 미출석 거르기 · 반 바로가기
(function () {
  const board = document.getElementById('board');
  if (!board) return;
  const slug = board.dataset.slug, sunday = board.dataset.sunday;
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
  const items = Array.from(document.querySelectorAll('[data-id]'));
  const q = document.getElementById('q');
  const toast = document.getElementById('toast');
  let filter = 'all', tt;

  const put = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };
  const fail = (msg) => { if (!toast) return; toast.textContent = msg; toast.classList.remove('hidden'); clearTimeout(tt); tt = setTimeout(() => toast.classList.add('hidden'), 2600); };

  // 초성 검색
  const CHO = ['ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ', 'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'];
  const cho = (s) => Array.from(s).map((ch) => { const c = ch.charCodeAt(0) - 0xac00; return c >= 0 && c <= 11171 ? CHO[Math.floor(c / 588)] : ch; }).join('');
  items.forEach((k) => { k.dataset.cho = cho(k.dataset.name || ''); });

  function apply() {
    const t = (q?.value || '').replace(/\s+/g, '');
    board.classList.toggle('searching', !!t);   // 찾는 중에는 이름 아래 반 이름을 보인다(유년부와 같다)
    let hits = 0;
    items.forEach((k) => {
      const p = k.dataset.p === '1';
      const show = t ? (/^[ㄱ-ㅎ]+$/.test(t) ? k.dataset.cho.includes(t) : k.dataset.name.includes(t)) : filter === 'all' || !p;
      k.classList.toggle('hidden', !show);
      if (show) hits++;
    });
    document.querySelectorAll('.grp').forEach((g) => g.classList.toggle('hidden', !g.querySelector('[data-id]:not(.hidden)')));
    const nh = document.getElementById('noHit');
    if (nh) { nh.classList.toggle('hidden', hits > 0); nh.textContent = !t && filter === 'left' ? '모두 왔어요 🙂' : '찾는 아이가 없어요.'; }
  }

  function counts() {
    const total = items.length, came = items.filter((k) => k.dataset.p === '1').length;
    put('presentN', came); put('leftN', total - came); put('cLeft', total - came);
    const bar = document.getElementById('barFill');
    if (bar) bar.style.width = (total ? Math.round((came * 100) / total) : 0) + '%';
    // 새친구 상세: 위 카드의 "출석 N번"과 4칸 막대도 같이(주일 칸을 누를 때)
    const ncN = document.getElementById('ncN');
    if (ncN) { ncN.textContent = came; document.querySelectorAll('.nc-bar i').forEach((i, ix) => i.classList.toggle('on', ix < came)); }
    let todo = 0, done = 0;
    document.querySelectorAll('.grp').forEach((g) => {
      const ks = g.querySelectorAll('[data-id]'), c = g.querySelectorAll('[data-id][data-p="1"]').length;
      const gc = g.querySelector('.gc'); if (gc) gc.textContent = c + '/' + ks.length;
      const j = document.querySelector('.jump a[data-g="' + g.dataset.g + '"]');
      if (!j) return;
      const d = c > 0; if (d) done++; else todo++;
      j.classList.toggle('done', d); j.classList.toggle('todo', !d);
      j.style.order = (d ? 1000 : 0) + Number(j.dataset.ord);
    });
    put('jTodo', todo); put('jDone', done);
    const jh = document.getElementById('jumpH');   // 모든 반을 체크하면 "✓ 모든 반 체크 완료"(유년부와 같다)
    if (jh) { const all = todo === 0 && done > 0; jh.querySelector('.a')?.classList.toggle('hidden', all); jh.querySelector('.z')?.classList.toggle('hidden', all); jh.querySelector('.all')?.classList.toggle('hidden', !all); }
  }

  function set(k, on) {
    k.dataset.p = on ? '1' : '0';
    k.classList.toggle('on', on);
    k.setAttribute('aria-pressed', on ? 'true' : 'false');
    const ck = k.querySelector('.ck'); if (ck) ck.textContent = on ? '✓ 출석' : '출석';
    if (k.classList.contains('wrow')) { const em = k.querySelector('em'); if (em) em.textContent = on ? '출석' : '결석'; }   // 새친구 상세 주일 줄
    // 기존 출석 변경(K7): 이름 옆 [출석]/[결석]
    const pill = k.querySelector('.pill'); if (pill) { pill.textContent = on ? '출석' : '결석'; pill.classList.toggle('y', on); pill.classList.toggle('n', !on); }
  }

  async function toggle(k) {
    if (k.classList.contains('busy')) return;
    const prev = k.dataset.p === '1', next = !prev;
    set(k, next); counts();
    k.classList.add('busy');
    k.setAttribute('aria-busy', 'true');
    // 서버가 바로 답해도 동그라미가 보이도록 최소 0.5초(유년부와 같다)
    const MIN_SPIN = 500, t0 = performance.now();
    try {
      const res = await fetch(`/${slug}/attendance?handler=Set`, {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
        body: JSON.stringify({ personId: Number(k.dataset.id), sunday: k.dataset.sunday || sunday, present: next }),
      });
      const j = await res.json().catch(() => null);
      if (!res.ok || !j || !j.ok) throw new Error((j && j.message) || '잠시 뒤에 다시 해 주세요');
    } catch (e) {
      set(k, prev); counts();
      fail(k.dataset.name + ' 저장하지 못했어요 · ' + e.message);
    } finally {
      const left = MIN_SPIN - (performance.now() - t0);
      if (left > 0) await new Promise((r) => setTimeout(r, left));
      k.classList.remove('busy');
      k.removeAttribute('aria-busy');
      if (q && q.value) { q.value = ''; clearBtn(); apply(); q.focus(); } else if (filter !== 'all') setTimeout(apply, 350);
    }
  }

  items.forEach((k) => (k.querySelector('.ck, .pill') || k).addEventListener('click', () => toggle(k)));
  const qClear = document.getElementById('qClear');   // 검색어가 있으면 ✕ 로 한 번에 지운다
  const clearBtn = () => qClear?.classList.toggle('hidden', !q?.value);
  q?.addEventListener('input', () => { clearBtn(); apply(); });
  qClear?.addEventListener('click', () => { q.value = ''; clearBtn(); apply(); q.focus(); });
  // [검색 →]: 글자를 쓰는 대로 이미 거르므로, 누르면 거른 결과를 다시 맞추고 자판을 닫는다(성경 검색과 같은 모양)
  document.getElementById('qGo')?.addEventListener('click', () => { apply(); q?.blur(); });
  q?.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); apply(); q.blur(); } });
  document.querySelectorAll('.seg2 button').forEach((b) => b.addEventListener('click', () => {
    filter = b.dataset.f;
    document.querySelectorAll('.seg2 button').forEach((x) => x.classList.toggle('on', x === b));
    apply();
  }));
  counts();
})();
