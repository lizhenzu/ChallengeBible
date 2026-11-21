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

// 회원 내보내기(2026-11-21): 찾기 · 직접 가입/가짜 탭 · 선택 수 · 파일로 받기 · 복사. SQL 은 이 화면에만 있고 서버에 남지 않는다
(() => {
  const f = document.querySelector('[data-mx]');
  if (f) {
    const rows = [...f.querySelectorAll('.mx-u')];
    const q = f.querySelector('[data-mx-q]');
    const none = f.querySelector('[data-mx-none]');
    let tab = 'real';
    const count = () => { const n = rows.filter(r => r.querySelector('input').checked).length; f.querySelectorAll('[data-mx-n]').forEach(b => b.textContent = n); };
    const show = () => {
      const t = (q.value || '').trim().toLowerCase();
      let shown = 0;
      rows.forEach(r => { const ok = r.dataset.mxKind === tab && (!t || r.dataset.mxText.includes(t)); r.hidden = !ok; if (ok) shown++; });
      if (none) none.hidden = shown > 0 || rows.length === 0;
    };
    q.addEventListener('input', show);
    q.addEventListener('keydown', e => { if (e.key === 'Enter') e.preventDefault(); });
    f.querySelectorAll('[data-mx-tab]').forEach(b => b.addEventListener('click', () => {
      tab = b.dataset.mxTab; f.querySelectorAll('[data-mx-tab]').forEach(x => x.classList.toggle('on', x === b)); show();
    }));
    f.querySelectorAll('[data-mx-all]').forEach(b => b.addEventListener('click', () => {
      rows.filter(r => !r.hidden).forEach(r => r.querySelector('input').checked = b.dataset.mxAll === '1'); count();
    }));
    rows.forEach(r => r.querySelector('input').addEventListener('change', count));
    show(); count();
  }
  const sql = document.querySelector('[data-mx-sql]');
  if (!sql) return;
  document.querySelector('[data-mx-save]')?.addEventListener('click', e => {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob(['\ufeff' + sql.value], { type: 'application/sql;charset=utf-8' }));
    a.download = e.currentTarget.dataset.name || 'member-restore.sql';
    document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(a.href), 2000);
  });
  document.querySelector('[data-mx-copy]')?.addEventListener('click', async e => {
    try { await navigator.clipboard.writeText(sql.value); } catch { sql.select(); document.execCommand('copy'); }
    e.currentTarget.textContent = '복사했어요';
  });
  document.getElementById('rs-export')?.scrollIntoView({ block: 'start' });
})();
