// 쪽지 쓰기(확정 시안 쪽지 ④ ⑦): 글자 수 · 플랫폼 관리자 받는 사람 고르기(한 사람 / 모든 사용자 / 부서)와 받는 사람 수
(() => {
  const body = document.querySelector('[data-ms-body]');
  const n = document.querySelector('[data-ms-n]');
  if (body && n) { const f = () => n.textContent = body.value.length.toLocaleString('ko-KR'); body.addEventListener('input', f); f(); }
  const modes = document.querySelectorAll('[data-ms-mode]');
  if (!modes.length) return;
  const ones = document.querySelectorAll('[data-ms-one]');
  const dept = document.querySelector('[data-ms-dept]');
  const tip = document.querySelector('[data-ms-count]');
  const send = document.querySelector('[data-ms-send]');
  const mode = () => document.querySelector('[data-ms-mode]:checked')?.value ?? 'one';
  const count = async () => {
    const m = mode();
    if (m === 'one') { tip.hidden = true; send.textContent = '보내기'; return; }
    const ids = [...dept.querySelectorAll('input:checked')].map(i => i.value);
    if (m === 'dept' && ids.length === 0) { tip.hidden = false; tip.textContent = '받을 부서를 골라 주세요.'; send.textContent = '보내기'; return; }
    const q = new URLSearchParams({ handler: 'Count' });
    if (m === 'dept') ids.forEach(i => q.append('tids', i));
    try {
      const r = await fetch('/messages/write?' + q.toString());
      const { n: c } = await r.json();
      tip.hidden = false;
      tip.innerHTML = `${m === 'all' ? '모든 사용자' : [...dept.querySelectorAll('input:checked')].map(i => i.nextElementSibling.textContent).join(' · ')} <b>${c.toLocaleString('ko-KR')}명</b>에게 가요 · 쪽지는 하나만 만들어져요. 나중에 들어온 사람에게도 보여요.`;
      send.textContent = `${c.toLocaleString('ko-KR')}명에게 보내기`;
    } catch { tip.hidden = true; }
  };
  const sync = () => {
    const m = mode();
    ones.forEach(o => o.hidden = m !== 'one');
    dept.hidden = m !== 'dept';
    count();
  };
  modes.forEach(r => r.addEventListener('change', sync));
  dept.addEventListener('change', count);
  sync();
})();
