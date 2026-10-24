// 도전!!!성경통독 화면 동작. 켜기 PUT · 끄기 DELETE(X-17). 저장하는 동안 누른 동그라미 안에서 원이 돈다.
(() => {
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
  const toast = document.getElementById('toast');
  let tt;
  const say = (msg) => { if (!toast) return; toast.textContent = msg; toast.hidden = false; clearTimeout(tt); tt = setTimeout(() => (toast.hidden = true), 2600); };
  const call = async (method, url) => {
    const res = await fetch(url, { method, credentials: 'same-origin', headers: { RequestVerificationToken: token } });
    return { ok: res.ok, json: await res.json().catch(() => ({})) };
  };
  const wait = (b, on) => { b.disabled = on; b.classList.toggle('wait', on); b.setAttribute('aria-busy', String(on)); };

  // 성경 목록의 "다 읽음 표시": 장 번호를 눌러 바로 완료 · 취소(R-151, 종이 성경으로 읽은 사람)
  const panel = document.querySelector('.rp-panel[data-book]');
  if (panel) {
    const book = panel.dataset.book;
    const count = panel.querySelector('[data-panel-count]');
    const total = panel.querySelectorAll('.ch').length;
    panel.addEventListener('click', async (e) => {
      const b = e.target.closest('button.ch');
      if (!b || b.disabled) return;
      const on = b.getAttribute('aria-pressed') !== 'true';
      wait(b, true);
      const { ok, json } = await call(on ? 'PUT' : 'DELETE', `/api/me/reading/chapters/${book}/${b.dataset.ch}`);
      wait(b, false);
      if (!ok) { say('잠시 뒤에 다시 해 주세요.'); return; }
      b.setAttribute('aria-pressed', String(on));
      b.classList.toggle('done', on);
      const n = panel.querySelectorAll('.ch.done').length;
      if (count) count.textContent = `${n} / ${total}장 · ${Math.floor(n * 100 / total)}%`;
      if (json.roundCompleted) say(`성경을 다 읽었어요! ${json.newRoundNo}회차를 시작해요`);
      else say(on ? `${b.dataset.ch}장을 다 읽음으로 표시했어요` : `${b.dataset.ch}장 표시를 뺐어요`);
    });
  }

  // 북마크 모음: ★ 을 누르면 빼고, 다시 누르면 다시 담는다(카드는 새로 고칠 때까지 그 자리에 둔다)
  const marks = document.querySelector('[data-bookmarks]');
  if (marks) {
    const count = document.querySelector('[data-bmcount]');
    marks.addEventListener('click', async (e) => {
      const b = e.target.closest('button[data-unstar]');
      if (!b || b.disabled) return;
      const li = b.closest('li');
      const on = !b.classList.contains('on');
      wait(b, true);
      const { ok } = await call(on ? 'PUT' : 'DELETE', `/api/me/bible/bookmarks/${li.dataset.book}/${li.dataset.ch}/${li.dataset.v}`);
      wait(b, false);
      if (!ok) { say('잠시 뒤에 다시 해 주세요.'); return; }
      b.classList.toggle('on', on);
      li.classList.toggle('off', !on);
      if (count) count.textContent = `${marks.querySelectorAll('.star.on').length}절`;
      say(on ? '다시 담았어요' : '북마크에서 뺐어요');
    });
  }
})();
