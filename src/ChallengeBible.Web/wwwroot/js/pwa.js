// PWA(확정 시안: 오프라인 ②): 서비스 워커 등록 + 쓰는 중에 연결이 끊기면 화면 위에 띠.
(() => {
  if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => { navigator.serviceWorker.register('/sw.js').catch(() => { /* 등록 실패해도 화면은 그대로 쓴다 */ }); });
  }

  let bar = null, timer = 0;
  function show(online) {
    if (!bar) {
      bar = document.createElement('div');
      bar.className = 'netbar';
      bar.setAttribute('role', 'status');
      bar.innerHTML = '<i aria-hidden="true"></i><span></span>';
      document.body.appendChild(bar);
    }
    clearTimeout(timer);
    bar.classList.toggle('ok', online);
    bar.hidden = false;
    bar.querySelector('span').textContent = online ? '다시 연결됐어요' : '인터넷 연결이 끊겼어요 · 미션 버튼은 연결된 뒤에 눌러 주세요';
    if (online) timer = setTimeout(() => { bar.hidden = true; }, 2500);
  }
  window.addEventListener('offline', () => show(false));
  window.addEventListener('online', () => show(true));
  if (!navigator.onLine) document.addEventListener('DOMContentLoaded', () => show(false));
})();
