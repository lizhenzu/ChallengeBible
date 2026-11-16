// 완전 초기화(ADR 0102): 묶음 [모두] 체크, 사람을 지우면 기록은 모두 함께(잠금), 지울 개수 표시
(() => {
  const f = document.querySelector('[data-reset]');
  if (!f) return;
  const items = [...f.querySelectorAll('input[name="pick"]')];
  const sum = f.querySelector('[data-rs-sum]');
  const note = f.querySelector('[data-rs-forced]');
  const group = g => items.filter(i => i.dataset.rsGroup === g);
  const sync = () => {
    const forced = group('사람').some(i => i.checked);
    group('기록').forEach(i => {
      if (forced) { i.dataset.was ??= i.checked ? '1' : '0'; i.checked = true; }
      else if (i.dataset.was !== undefined) { i.checked = i.dataset.was === '1'; delete i.dataset.was; }
      i.closest('.rs-it').classList.toggle('forced', forced);
      i.setAttribute('aria-disabled', forced ? 'true' : 'false');
    });
    if (note) note.hidden = !forced;
    f.querySelectorAll('[data-rs-all]').forEach(a => { const gs = group(a.dataset.rsAll); a.checked = gs.every(i => i.checked); });
    if (sum) sum.textContent = `${items.length}개 중 ${items.filter(i => i.checked).length}개를 지워요`;
  };
  items.forEach(i => i.addEventListener('click', e => {
    if (i.getAttribute('aria-disabled') === 'true') { e.preventDefault(); return; }
    sync();
  }));
  f.querySelectorAll('[data-rs-all]').forEach(a => a.addEventListener('change', () => {
    group(a.dataset.rsAll).forEach(i => { if (i.getAttribute('aria-disabled') !== 'true') i.checked = a.checked; });
    sync();
  }));
  sync();
})();
