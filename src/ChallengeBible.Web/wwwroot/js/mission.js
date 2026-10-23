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
      if (pt && !pt.textContent.startsWith('완료')) pt.textContent = (api === 'recite' ? '이번 주 완료 · ' : '완료 · ') + pt.textContent;
      after(json);
    } else {
      b.disabled = false;
      say(json.message ?? '잠시 뒤에 다시 해 주세요.');
    }
  }));

  // 퀴즈
  const quiz = document.getElementById('quiz');
  if (quiz) {
    const msg = document.getElementById('qmsg');
    const finish = (json) => {
      quiz.querySelectorAll('button, input').forEach((x) => (x.disabled = true));
      if (json.correctChoiceId) quiz.querySelector(`[data-choice="${json.correctChoiceId}"]`)?.classList.add('ok');
      if (json.explanation) { const ex = document.getElementById('explain'); ex.querySelector('p').textContent = json.explanation; ex.hidden = false; }
    };
    const answer = async (body, btn) => {
      const { ok, json } = await post('quiz/attempts', { date, ...body });
      msg.textContent = json.message ?? '';
      if (!ok || json.ok === false) return;
      if (json.isCorrect) { btn?.classList.add('ok'); after(json); }
      else btn?.classList.add('no');
      if (btn) btn.disabled = true;
      if (json.isCorrect || json.attemptsLeft === 0) finish(json);
    };
    quiz.addEventListener('click', (e) => {
      const b = e.target.closest('button.choice');
      if (b && !b.disabled) answer({ choiceId: Number(b.dataset.choice) }, b);
    });
    document.getElementById('textAnswer')?.addEventListener('submit', (e) => {
      e.preventDefault();
      const v = e.target.answer.value.trim();
      if (v) answer({ answerText: v }, null);
    });
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
