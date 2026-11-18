// 부서 홈 예배 안내(ADR 0116): 주소 복사 버튼
(() => {
  document.querySelectorAll('[data-copy]').forEach((b) => b.addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(b.dataset.copy); b.textContent = '✓'; } catch { b.textContent = '!'; }
    setTimeout(() => { b.textContent = '⧉'; }, 1500);
  }));
})();
