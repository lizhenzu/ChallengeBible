// 도전!!!성경통독 — 성경 목록의 "다 읽음 표시": 장 번호를 눌러 바로 완료 · 취소(R-151, 종이 성경으로 읽은 사람).
// 켜기 PUT · 끄기 DELETE(X-17). 저장하는 동안 그 동그라미 안에서 원이 돈다.
(() => {
  const panel = document.querySelector('.rp-panel[data-book]');
  if (!panel) return;
  const book = panel.dataset.book;
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
  const toast = document.getElementById('toast');
  let tt;
  const say = (msg) => { if (!toast) return; toast.textContent = msg; toast.hidden = false; clearTimeout(tt); tt = setTimeout(() => (toast.hidden = true), 2600); };
  const count = panel.querySelector('[data-panel-count]');
  const total = panel.querySelectorAll('.ch').length;

  panel.addEventListener('click', async (e) => {
    const b = e.target.closest('button.ch');
    if (!b || b.disabled) return;
    const on = b.getAttribute('aria-pressed') !== 'true';
    b.disabled = true; b.classList.add('wait');
    const res = await fetch(`/api/me/reading/chapters/${book}/${b.dataset.ch}`, {
      method: on ? 'PUT' : 'DELETE', credentials: 'same-origin', headers: { RequestVerificationToken: token },
    });
    const json = await res.json().catch(() => ({}));
    b.disabled = false; b.classList.remove('wait');
    if (!res.ok) { say('잠시 뒤에 다시 해 주세요.'); return; }
    b.setAttribute('aria-pressed', String(on));
    b.classList.toggle('done', on);
    const n = panel.querySelectorAll('.ch.done').length;
    if (count) count.textContent = `${n} / ${total}장 · ${Math.floor(n * 100 / total)}%`;
    if (json.roundCompleted) say(`성경을 다 읽었어요! ${json.newRoundNo}회차를 시작해요`);
    else say(on ? `${b.dataset.ch}장을 다 읽음으로 표시했어요` : `${b.dataset.ch}장 표시를 뺐어요`);
  });
})();
