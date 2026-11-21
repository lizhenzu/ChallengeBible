// 카톡으로 보내기(2026-11-21 사용자): 휴대폰 공유 창(Web Share)에서 카카오톡을 고른다. 공유 창이 없는 컴퓨터는 안내글을 복사한다.
(function () {
  document.querySelectorAll('[data-share]').forEach(function (b) {
    b.addEventListener('click', async function () {
      var text = b.getAttribute('data-share');
      if (navigator.share) {
        try { await navigator.share({ text: text }); return; }
        catch (e) { if (e && e.name === 'AbortError') return; }   // 닫았으면 그대로, 그 밖의 오류는 복사로
      }
      try { await navigator.clipboard.writeText(text); b.textContent = '안내글을 복사했어요 · 카톡에 붙여 넣어 주세요'; b.classList.add('go-done'); }
      catch (e) { window.prompt('복사해서 카톡에 붙여 넣어 주세요', text); }
    });
  });
})();
