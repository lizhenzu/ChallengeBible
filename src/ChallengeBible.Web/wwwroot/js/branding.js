// 관리 · 이름 · 색(확정 시안: 관리-이름색): 글자 수 · 미리보기 · 기본값으로 되돌리기. 저장은 일반 폼 전송(JS 없이도 동작).
(() => {
  const root = document.querySelector('[data-branding]');
  if (!root) return;
  const form = root.querySelector('form');
  const name = form.elements.AppName, short = form.elements.AppShortName;
  const pv = root.querySelector('.br-pv');
  const saved = () => JSON.stringify([name.value.trim(), short.value.trim(), form.elements.ThemeColor.value]);
  const first = saved();

  function sync() {
    root.querySelector('[data-count="AppName"]').textContent = `${name.value.length} / 30`;
    const n = name.value.trim() || root.dataset.defname;
    root.querySelector('[data-pvname]').textContent = n;
    const c = form.elements.ThemeColor.value;
    pv.style.setProperty('--tc', `var(--c-${c})`);
    document.documentElement.dataset.color = c;          // 화면 강조색도 바로 미리보기
    root.querySelector('[data-save]').disabled = saved() === first;
  }
  form.addEventListener('input', sync);
  form.addEventListener('change', sync);
  root.querySelector('[data-reset]').addEventListener('click', () => { name.value = ''; short.value = ''; sync(); name.focus(); });   // 숨은 짧은 이름도 기본으로
  sync();
})();
