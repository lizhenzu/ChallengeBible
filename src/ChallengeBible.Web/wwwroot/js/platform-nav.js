// 플랫폼 햄버거 메뉴(확정 시안 플랫폼-메뉴-햄버거, 2026-11-15): ☰ 로 열고, ✕ · 바깥 · Esc 로 닫는다.
(() => {
  const d = document.getElementById('pnav');
  const btn = document.querySelector('[data-pnav-open]');
  if (!d || !btn) return;
  const open = () => {
    d.hidden = false; btn.setAttribute('aria-expanded', 'true'); document.body.classList.add('pnav-open');
    (d.querySelector('a[aria-current="page"]') || d.querySelector('.pd-x')).focus();
  };
  const close = () => { d.hidden = true; btn.setAttribute('aria-expanded', 'false'); document.body.classList.remove('pnav-open'); btn.focus(); };
  btn.addEventListener('click', open);
  d.querySelectorAll('[data-pnav-close]').forEach(e => e.addEventListener('click', close));
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && !d.hidden) close(); });
})();
