// SQL 실행(ADR 0107): 자주 쓰는 쿼리를 칸에 넣고, 저장을 고를 때만 확인 글자 칸을 보인다
(() => {
  const f = document.querySelector('[data-sql]');
  if (!f) return;
  const q = f.querySelector('[data-sql-q]');
  const conf = f.querySelector('[data-sql-conf]');
  f.querySelector('[data-sql-clear]')?.addEventListener('click', () => { q.value = ''; q.focus(); });   // 칸 비우기
  f.querySelectorAll('[data-sql-preset]').forEach(b => b.addEventListener('click', () => { q.value = b.dataset.sqlPreset; q.focus(); }));
  const sync = () => {
    const save = f.querySelector('input[name=mode]:checked')?.value === 'save';
    conf.hidden = !save;
    f.querySelectorAll('.sq-mode label').forEach(l => l.classList.toggle('on', l.querySelector('input').checked));
  };
  f.querySelectorAll('input[name=mode]').forEach(r => r.addEventListener('change', sync));
  sync();
  f.addEventListener('submit', () => f.querySelector('.sq-run')?.classList.add('busy'));
})();
