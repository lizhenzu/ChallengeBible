// 플랫폼 › 계정 찾기 › 부서 바로 옮기기(2026-11-18): 고른 부서 이름을 확인 문장 · 버튼에 넣는다
(() => {
  const f = document.querySelector('[data-move]');
  if (!f) return;
  const sel = f.elements.target, q = f.querySelector('[data-move-q]'), go = f.querySelector('[data-move-go]');
  const short = (n) => n.replace(/^잠실새내교회\s+/, '');
  const ro = (w) => { const c = w.charCodeAt(w.length - 1) - 0xAC00; return c < 0 || c > 11171 || c % 28 === 0 || c % 28 === 8 ? '로' : '으로'; };
  const sync = () => {
    const to = sel.options[sel.selectedIndex]?.text || '';
    q.textContent = `${f.dataset.name}님을 ${short(f.dataset.from)} → ${short(to)}${ro(short(to))} 옮길까요?`;
    go.textContent = `${short(to)}${ro(short(to))} 옮기기`;
  };
  sel.addEventListener('change', sync);
  sync();
})();
