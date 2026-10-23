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
    if (json.milestonesReached?.length) say(`${json.milestonesReached.at(-1).toLocaleString('ko-KR')}점을 넘었어요! 축하해요`);   // 축하 창은 다음 단계
    else say(json.message);
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
