// 미션 버튼 → JSON API(ADR 0034). 프레임워크 없이 화면 일부만 바꾼다.
(() => {
  const main = document.querySelector('main[data-slug]');
  if (!main) return;
  const slug = main.dataset.slug, date = main.dataset.date;
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
  const toast = document.getElementById('toast');
  let tt;
  const say = (msg) => { if (!toast) return; toast.textContent = msg; toast.hidden = false; clearTimeout(tt); tt = setTimeout(() => (toast.hidden = true), 2600); };

  async function post(path, body) {
    const res = await fetch(`/api/${slug}/missions/${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
      body: JSON.stringify(body),
      credentials: 'same-origin',
    });
    const json = await res.json().catch(() => ({ message: '잠시 뒤에 다시 해 주세요.' }));
    return { ok: res.ok, json };
  }

  function after(json) {
    const season = document.querySelector('[data-season]');
    if (season && typeof json.seasonTotal === 'number' && json.seasonTotal > 0) season.textContent = json.seasonTotal.toLocaleString('ko-KR');
    const day = document.querySelector('[data-daypoints]');
    if (day && json.pointsEarned > 0) day.textContent = String(Number(day.textContent) + json.pointsEarned);
    if (json.milestonesReached?.length) celebrate(json.milestonesReached.at(-1), json);
    else say(json.message);
  }

  // 축하 창(R-101, 확정 시안: 말씀-축하팝업). 단계를 처음 넘을 때만 뜬다.
  function celebrate(m, json) {
    const box = document.getElementById('cb-ms');
    const steps = (box?.dataset.milestones ?? '').split(',').map(Number).filter(Boolean);
    const next = steps.find((s) => s > m);
    const total = json.seasonTotal;
    const fmt = (n) => n.toLocaleString('ko-KR');
    const last = steps.length && m === steps.at(-1);
    const dlg = document.createElement('div');
    dlg.className = 'scrim';
    dlg.innerHTML = `<section class="dlg" role="dialog" aria-modal="true" aria-labelledby="cT">
      <span class="who"></span>
      <div class="medal"><div><b class="num">${fmt(m)}</b><small>점 돌파</small></div></div>
      <h2 id="cT">${last ? '올해 목표를 이뤘어요!' : `${fmt(m)}점을 넘었어요!`}</h2>
      <div class="got"><span>+${json.pointsEarned}점</span><span>올해 총 ${fmt(total)}점</span></div>
      <p>${last ? '한 해 동안 말씀과 함께해 줘서 고마워요.' : '매일 말씀과 함께한 시간이 쌓였어요.'}</p>
      ${next ? `<div class="next"><div class="l"><span>다음 목표 <b>${fmt(next)}점</b></span><span>${fmt(next - total)}점 남음</span></div>
        <div class="bar"><i style="width:${Math.min(100, total / steps.at(-1) * 100)}%"></i></div></div>` : ''}
      <button type="button" class="btn p" data-close>고마워요</button>
      <a class="btn t" href="${box?.dataset.points ?? '#'}">내 점수 보기</a>
    </section>`;
    dlg.querySelector('.who').textContent = box?.dataset.who ?? '';
    const close = () => dlg.remove();
    dlg.addEventListener('click', (e) => { if (e.target === dlg || e.target.closest('[data-close]')) close(); });
    document.addEventListener('keydown', function esc(e) { if (e.key === 'Escape') { close(); document.removeEventListener('keydown', esc); } });
    document.body.appendChild(dlg);
    dlg.querySelector('[data-close]').focus();
  }

  // 아래 미션 메뉴의 지금 화면 상태 글자(완료 · 끝남)
  function navState(text, done) {
    const nav = document.querySelector('.mnav a[aria-current="page"]');
    if (!nav) return;
    nav.classList.toggle('dn', done);
    const s = nav.querySelector('.S');
    if (s) { s.textContent = text; s.classList.toggle('done', done); }
  }

  // 성경 읽기: 절 읽음 ✓ · 책갈피 ★ · 이 페이지 모두 읽음(R-112). 켜기 PUT · 끄기 DELETE(X-17), 점수와 무관(R-113)
  const verses = document.querySelector('.rdv[data-book]');
  if (verses) {
    const book = verses.dataset.book;
    // 통독 본문(data-api="reading")은 ✓ 를 회차 절 읽음(R-160)으로, 말씀 본문은 성경 읽음 표시(R-112)로 보낸다. ★ 는 둘 다 책갈피
    const reading = verses.dataset.api === 'reading', chapter = verses.dataset.chapter;
    const call = async (method, url, body) => {
      const res = await fetch(url, {
        method, credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
        body: body ? JSON.stringify(body) : undefined,
      });
      return { ok: res.ok, json: await res.json().catch(() => ({})) };
    };
    const doneFlag = document.querySelector('[data-chapterdone]');
    const afterReading = (json) => {
      if (!reading) return;
      if (doneFlag) doneFlag.hidden = !json.chapterDone;
      if (json.roundCompleted) say(`성경을 다 읽었어요! ${json.newRoundNo}회차를 시작해요`);
      else if (json.chapterDone) say(`${chapter}장을 다 읽었어요`);
    };
    // 저장 중(확정 시안 v2): 누른 동그라미 안에서 파란 원이 돌고, 그 절 줄이 은은하게 반짝인다
    const busy = (el, on) => {
      el.disabled = on;
      el.classList.toggle('wait', on);
      el.setAttribute('aria-busy', String(on));
      el.closest('[data-ch]')?.classList.toggle('busy', on);
    };
    const setMark = (b, on) => {
      b.setAttribute('aria-pressed', String(on));
      b.classList.toggle('on', on);
      b.closest('[data-ch]')?.classList.toggle(b.dataset.mark === 'read' ? 'read' : 'bm', on);
    };
    const prog = document.querySelector('.rdprog');
    const total = Number(prog?.dataset.total ?? 0);
    const count = document.querySelector('[data-readcount]'), pctEl = document.querySelector('[data-readpct]'), bar = document.querySelector('[data-readbar]');
    const bump = (delta) => {
      if (!count) return;
      const n = Math.max(0, Math.min(total, Number(count.textContent) + delta));
      count.textContent = String(n);
      const pct = total === 0 ? 0 : Math.round(n * 100 / total);
      if (pctEl) pctEl.textContent = `${pct}%`;
      if (bar) bar.style.width = `${pct}%`;
    };
    const all = document.querySelector('[data-pageall]');
    const readBtns = () => [...verses.querySelectorAll('[data-mark="read"]')];
    const isOn = (b) => b.getAttribute('aria-pressed') === 'true';
    const refreshAll = () => {
      if (!all) return;
      const n = readBtns().filter(isOn).length, done = n === readBtns().length;
      all.classList.toggle('done', done);
      all.querySelector('[data-megat]').textContent = done ? '이 페이지 다 읽었어요' : '이 페이지 읽음으로 표시';
      all.querySelector('[data-megad]').textContent = done ? '다시 누르면 읽음이 취소돼요' : '넘기기만 해서는 읽음이 되지 않아요';
      all.querySelector('[data-megac]').textContent = `이 페이지 읽은 절 ${n} / ${all.dataset.n}`;
    };
    verses.addEventListener('click', async (e) => {
      const b = e.target.closest('button[data-mark]');
      if (!b || b.disabled) return;
      const row = b.closest('[data-ch]');
      const on = !isOn(b);
      const kind = b.dataset.mark === 'read' ? 'read' : 'bookmarks';
      busy(b, true);   // 서버가 느려도 눌렸다는 것을 보이게: 끝날 때까지 동그라미가 돈다
      const url = reading && kind === 'read'
        ? `/api/me/reading/verses/${book}/${row.dataset.ch}/${row.dataset.v}`
        : `/api/me/bible/${kind}/${book}/${row.dataset.ch}/${row.dataset.v}`;
      const { ok, json } = await call(on ? 'PUT' : 'DELETE', url);
      busy(b, false);
      if (!ok) { say('잠시 뒤에 다시 해 주세요.'); return; }
      setMark(b, on);
      if (kind === 'read') { bump(on ? 1 : -1); refreshAll(); afterReading(json); }
      else say(on ? '책갈피에 넣었어요' : '책갈피를 뺐어요');
    });
    all?.addEventListener('click', async () => {
      if (all.disabled) return;
      const on = !all.classList.contains('done');
      const r = { book: Number(book), startChapter: Number(all.dataset.sc), startVerse: Number(all.dataset.sv), endChapter: Number(all.dataset.ec), endVerse: Number(all.dataset.ev) };
      const changing = readBtns().filter((x) => isOn(x) !== on);
      busy(all, true);
      changing.forEach((x) => busy(x, true));
      const { ok, json } = reading
        ? await call(on ? 'PUT' : 'DELETE', `/api/me/reading/verses/${r.book}/${r.startChapter}?from=${r.startVerse}&to=${r.endVerse}`)
        : on
          ? await call('PUT', '/api/me/bible/read-range', r)
          : await call('DELETE', `/api/me/bible/read-range?book=${r.book}&startChapter=${r.startChapter}&startVerse=${r.startVerse}&endChapter=${r.endChapter}&endVerse=${r.endVerse}`);
      busy(all, false);
      changing.forEach((x) => busy(x, false));
      if (!ok) { say('잠시 뒤에 다시 해 주세요.'); return; }
      changing.forEach((x) => setMark(x, on));
      bump(on ? changing.length : -changing.length);
      refreshAll();
      afterReading(json);
    });
  }

  // 접었다 펴는 구역(암송 요점 등): data-fold = 펼칠 칸의 id
  document.querySelectorAll('[data-fold]').forEach((b) => b.addEventListener('click', () => {
    const box = document.getElementById(b.dataset.fold);
    if (!box) return;
    box.hidden = !box.hidden;
    b.textContent = box.hidden ? b.dataset.more : b.dataset.less;
    b.setAttribute('aria-expanded', String(!box.hidden));
  }));

  // 말씀: 이야기 3줄 접기. 넘칠 때만 "더 보기", 펼치면 01 번호가 초록(시안)
  const story = document.getElementById('story'), more = document.getElementById('storyMore');
  if (story && more) {
    if (story.scrollHeight > story.clientHeight + 2) more.hidden = false;
    else document.getElementById('storySec')?.classList.add('done');
    more.addEventListener('click', () => {
      const open = story.classList.toggle('clip') === false;
      more.textContent = open ? '이야기 접기 －' : '이야기 더 보기 ＋';
      more.setAttribute('aria-expanded', String(open));
      document.getElementById('storySec')?.classList.add('done');
    });
  }

  // 말씀 · 기도 · 암송 항목
  document.querySelectorAll('.picks[data-api]').forEach((box) => box.addEventListener('click', async (e) => {
    const b = e.target.closest('button.pick');
    if (!b || b.disabled) return;
    b.disabled = true;
    const api = box.dataset.api;
    const { ok, json } = await post(api, api === 'recite' ? { date } : { date, items: [b.dataset.item] });
    if (ok && json.ok !== false) {
      b.setAttribute('aria-pressed', 'true');
      const pt = b.querySelector('.pt');
      if (pt && !pt.textContent.startsWith('완료') && !pt.textContent.startsWith('이번 주')) pt.textContent = (api === 'recite' ? '이번 주 완료 · ' : '완료 · ') + pt.textContent;
      // 구역 번호 · 상태 글자 · 아래 메뉴를 시안처럼 바꾼다
      const sec = box.closest('[data-pickbox]');
      const all = box.querySelectorAll('button.pick').length, on = box.querySelectorAll('button.pick[aria-pressed="true"]').length;
      sec?.classList.add('done');
      const st = sec?.querySelector('[data-pickstate]');
      if (st) st.textContent = api === 'recite' ? '이번 주 완료' : on === all ? '모두 했어요' : '미션 완료';
      navState('완료', true);
      after(json);
    } else {
      b.disabled = false;
      say(json.message ?? '잠시 뒤에 다시 해 주세요.');
    }
  }));

  // 퀴즈(확정 시안): 보기를 고른 뒤 "답 제출하기". 판정 · 남은 기회는 서버가 알려 준다(R-43).
  const quiz = document.getElementById('quiz');
  if (quiz) {
    const msg = document.getElementById('qmsg'), go = document.getElementById('qgo'), tries = document.getElementById('tries');
    const text = document.getElementById('answerText');
    const max = Number(quiz.dataset.max), pts = Number(quiz.dataset.points);
    const picked = () => quiz.querySelector('input[type="radio"]:checked');
    const tag = (label, t) => {
      const box = label.querySelector('.t');
      box.querySelector('small')?.remove();
      const sm = document.createElement('small'); sm.textContent = t; box.appendChild(sm);
    };
    const tell = (t, cls) => { msg.textContent = t; msg.className = `msg ${cls}`; };
    const refresh = () => {
      quiz.querySelectorAll('.qopt').forEach((l) => l.classList.toggle('sel', !!l.querySelector('input:checked')));
      go.disabled = quiz.dataset.finished === '1' || (text ? text.disabled || !text.value.trim() : !picked());
    };
    const finish = (json, solved) => {
      quiz.dataset.finished = '1';
      quiz.querySelectorAll('input').forEach((x) => { x.disabled = true; if (x.type === 'radio') x.checked = false; });
      quiz.querySelectorAll('.qopt').forEach((l) => { l.classList.remove('sel'); if (!l.classList.contains('bad') && !l.classList.contains('ans')) l.classList.add('dim', 'fixed'); });
      const ans = json.correctChoiceId && quiz.querySelector(`.qopt[data-choice="${json.correctChoiceId}"]`);
      if (ans) { ans.classList.remove('dim', 'bad'); ans.classList.add('ans', 'fixed'); tag(ans, solved ? '정답' : '정답은 이것'); }
      go.innerHTML = `${solved ? '정답을 맞혔어요' : `오늘은 ${max}번 도전했어요`} <em aria-hidden="true">✓</em>`;
      go.disabled = true;
      navState(solved ? '완료' : '끝남', solved);
      const ex = document.getElementById('explain');
      if (ex && json.explanation) {
        ex.querySelector('p').textContent = json.explanation;
        const r = document.getElementById('resTag');
        if (r) { r.textContent = solved ? `정답 +${pts}점` : '점수 없음'; r.className = solved ? 'ok' : 'warn'; }
        ex.hidden = false;
        ex.scrollIntoView({ behavior: 'smooth', block: 'center' });
      }
    };
    quiz.addEventListener('change', refresh);
    text?.addEventListener('input', refresh);
    text?.addEventListener('keydown', (e) => { if (e.key === 'Enter' && !go.disabled) go.click(); });
    go.addEventListener('click', async () => {
      const r = picked(), v = text?.value.trim();
      if (!r && !v) return;
      go.disabled = true;
      const { ok, json } = await post('quiz/attempts', { date, ...(r ? { choiceId: Number(r.value) } : { answerText: v }) });
      if (!ok || json.ok === false) { tell(json.message ?? '잠시 뒤에 다시 해 주세요.', 'bad'); refresh(); return; }
      const label = r?.closest('.qopt');
      const left = json.attemptsLeft ?? 0;
      if (json.isCorrect) {
        quiz.classList.add('done');
        tries.textContent = '정답!'; tries.className = 'ok';
        tell(`정답! +${pts}점`, 'ok');
        if (label) { label.classList.add('ans', 'fixed'); tag(label, '정답'); }
        after(json);
        finish(json, true);
        return;
      }
      if (label) { label.classList.remove('sel'); label.classList.add('bad', 'fixed'); tag(label, '내가 고른 답 · 오답'); r.checked = false; r.disabled = true; }
      tries.className = 'warn';
      if (left > 0) {
        tries.textContent = `${left}번 남았어요`;
        tell(`오답이에요 (${max - left}/${max}) 한 번 더 도전해 보세요`, 'bad');
        if (text) text.value = '';
        refresh();
      } else {
        tries.textContent = '오늘은 끝';
        tell(`오늘은 ${max}번 도전했어요. 내일 새 퀴즈에 도전해요`, 'bad');
        finish(json, false);
      }
    });
    refresh();
  }

  // 글자 크기: − 5%씩, + 10%씩, 각 5번(75% ~ 150%), 가운데 = 100% (R-167). 이 기기에 기억.
  const steps = [75, 80, 85, 90, 95, 100, 110, 120, 130, 140, 150];
  let i = 5;
  try { i = Math.max(0, Math.min(10, Number(localStorage.getItem('cb.fs') ?? 5))); } catch { /* 저장 못 해도 화면은 된다 */ }
  const pct = document.querySelector('.fs .pct');
  const apply = () => {
    document.documentElement.style.setProperty('--fs', String(steps[i] / 100));
    if (pct) pct.textContent = `${steps[i]}%`;
    try { localStorage.setItem('cb.fs', String(i)); } catch { /* 무시 */ }
  };
  document.querySelectorAll('.fs button').forEach((b) => b.addEventListener('click', () => {
    const v = Number(b.dataset.fs);
    i = v === 0 ? 5 : Math.max(0, Math.min(10, i + v));
    apply();
  }));
  apply();
})();
