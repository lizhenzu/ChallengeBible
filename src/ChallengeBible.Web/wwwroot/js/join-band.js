/* 알림 띠(2026-11-28 시안 JoinA): 수락은 한 번 — 「처리 중…」 → 「합류했어요 ✓」, 거절은 「아니요」로 되돌리기. 스크립트가 없어도 폼이 그대로 동작한다. */
(function () {
  document.querySelectorAll('[data-jb-back]').forEach(function (b) {
    b.addEventListener('click', function () { var d = b.closest('details'); if (d) d.open = false; });
  });
  document.querySelectorAll('form[data-jb-accept]').forEach(function (f) {
    f.addEventListener('submit', function (e) {
      if (!window.fetch) return;
      e.preventDefault();
      var box = f.closest('[data-jb-btns]');
      var msg = document.createElement('span');
      msg.className = 'jb-state';
      msg.textContent = '처리 중…';
      if (box) { box.replaceChildren(msg); }
      fetch(f.action, { method: 'POST', body: new FormData(f), headers: { 'X-Requested-With': 'fetch' }, credentials: 'same-origin' })
        .then(function (r) { return r.json(); })
        .then(function (j) {
          if (j && j.ok) { msg.textContent = '합류했어요 ✓'; msg.classList.add('ok'); setTimeout(function () { location.reload(); }, 1200); }
          else { location.reload(); }
        })
        .catch(function () { location.reload(); });
    });
  });
})();
