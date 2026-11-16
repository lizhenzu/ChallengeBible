// 오늘 할 일(ADR 0100): [링크 복사] — 서버에서 링크를 받아 클립보드에 넣는다.
(() => {
  const root = document.querySelector('[data-td]');
  if (!root) return;
  const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
  root.addEventListener('click', async e => {
    const b = e.target.closest('[data-td-link]');
    if (!b) return;
    b.disabled = true;
    try {
      const res = await fetch(location.pathname + '?handler=Link', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams({ __RequestVerificationToken: token, id: b.dataset.tdLink }) });
      const r = await res.json();
      if (!r.ok) { b.textContent = '할 수 없어요'; return; }
      try { await navigator.clipboard.writeText(r.url); b.textContent = '복사했어요 ✓'; }
      catch { window.prompt('링크를 복사해 보내 주세요', r.url); b.textContent = '링크 열림'; }
    } finally { b.disabled = false; }
  });
})();
