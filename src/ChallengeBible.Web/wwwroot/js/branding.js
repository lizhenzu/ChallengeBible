// 관리 · 이름 · 색(확정 시안: 관리-이름색): 글자 수 · 미리보기 · 기본값으로 되돌리기. 저장은 일반 폼 전송(JS 없이도 동작).
(() => {
  const root = document.querySelector('[data-branding]');
  if (!root) return;
  const form = root.querySelector('form');
  const name = form.elements.AppName, short = form.elements.AppShortName;
  const pv = root.querySelector('.br-pv');
  const saved = () => JSON.stringify([name.value.trim(), short.value.trim(), form.elements.ThemeColor.value]);
  const first = saved();

  function iconText(s) { return (s.match(/[\p{L}\p{N}]/gu) || []).slice(0, 2).join('') || '도전'; }
  function sync() {
    root.querySelector('[data-count="AppName"]').textContent = `${name.value.length} / 30`;
    root.querySelector('[data-count="AppShortName"]').textContent = `${short.value.length} / 12`;
    const n = name.value.trim() || root.dataset.defname, s = short.value.trim() || root.dataset.defshort;
    root.querySelector('[data-pvname]').textContent = n;
    root.querySelector('[data-pvshort]').textContent = s;
    root.querySelector('[data-pvico]').textContent = iconText(s);
    const c = form.elements.ThemeColor.value;
    pv.style.setProperty('--tc', `var(--c-${c})`);
    document.documentElement.dataset.color = c;          // 화면 강조색도 바로 미리보기
    const sh = document.getElementById('AppShortNameH');
    if (!sh.classList.contains('bad')) sh.textContent = s.length > 6 ? '휴대폰에 따라 뒷부분이 잘려 보일 수 있어요.' : '휴대폰 홈 화면 아이콘 아래에 보여요. 6자 안쪽이 잘 보여요.';
    root.querySelector('[data-save]').disabled = saved() === first;
  }
  form.addEventListener('input', sync);
  form.addEventListener('change', sync);
  root.querySelector('[data-reset]').addEventListener('click', () => { name.value = ''; short.value = ''; sync(); name.focus(); });
  sync();
})();
