// 앱 설치 안내(ADR 0115, 확정 시안 설치안내): 기기 판별 → 알맞은 안내 한 덩어리, 설치 · [이미 설치했어요] · [나중에].
(() => {
  const root = document.querySelector('[data-install-page]');
  if (!root) return;
  const KEY = 'cb-install', WEEK = 7 * 24 * 60 * 60 * 1000;
  const back = root.dataset.back || '/';
  const save = (v) => { try { localStorage.setItem(KEY, JSON.stringify(v)); } catch { /* 저장 못 해도 이동은 한다 */ } };
  try { sessionStorage.setItem('cb-install-seen', '1'); } catch { /* 무시 */ }

  const ua = navigator.userAgent;
  const ios = /iPhone|iPad|iPod/i.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1);
  const android = /Android/i.test(ua);
  const kakao = /KAKAOTALK/i.test(ua);
  const inapp = kakao || /NAVER\(inapp|BAND\/|Instagram|FBAN|FBAV|FB_IAB|Line\/|DaumApps|everytimeApp|; wv\)/i.test(ua);
  const standalone = matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
  if (standalone) { save({ s: 'done' }); location.replace(back); return; }

  function show(v) {
    root.dataset.v = v;
    root.querySelectorAll('section[data-for]').forEach((s) => { s.hidden = !s.dataset.for.split(' ').includes(v); });
    root.querySelectorAll('[data-only]').forEach((e) => { e.hidden = e.dataset.only !== v; });
    root.querySelector('[data-more]').hidden = v === 'done';
  }
  let v = inapp ? 'inapp' : ios ? 'ios' : android ? 'android' : ('onbeforeinstallprompt' in window ? 'desktop' : 'desktop-other');
  show(v);
  root.querySelectorAll('[data-only-os]').forEach((e) => { e.hidden = e.dataset.onlyOs !== (ios ? 'ios' : 'android'); });

  // 안드로이드 · PC 크롬 · 엣지: 브라우저가 "설치할 수 있어요" 신호를 주면 받아 두었다가 버튼에서 띄운다
  let deferred = null;
  window.addEventListener('beforeinstallprompt', (e) => { e.preventDefault(); deferred = e; });
  window.addEventListener('appinstalled', () => { save({ s: 'done' }); show('done'); });

  const manual = root.querySelector('[data-manual]');
  root.querySelector('[data-install]').addEventListener('click', async () => {
    for (let i = 0; i < 20 && !deferred; i++) await new Promise((r) => setTimeout(r, 100));   // 신호가 조금 늦게 올 수 있다
    if (!deferred) { manual.hidden = false; return; }   // 이미 설치됐거나 설치를 못 하는 브라우저 → 손으로 하는 법
    deferred.prompt();
    const r = await deferred.userChoice;
    deferred = null;
    if (r && r.outcome === 'accepted') { save({ s: 'done' }); show('done'); }
  });

  // 카톡 · 밴드 같은 앱 안: 카톡은 바깥 브라우저 열기 주소, 안드로이드는 크롬으로 여는 주소. 아이폰 다른 앱은 메뉴 안내(문구)
  root.querySelector('[data-open-out]').addEventListener('click', () => {
    const here = location.href;
    if (kakao) location.href = 'kakaotalk://web/openExternal?url=' + encodeURIComponent(here);
    else if (android) location.href = 'intent://' + location.host + location.pathname + location.search + '#Intent;scheme=https;package=com.android.chrome;end';
  });

  root.querySelector('[data-done]').addEventListener('click', () => { save({ s: 'done' }); location.replace(back); });
  root.querySelector('[data-later]').addEventListener('click', () => { save({ s: 'later', until: Date.now() + WEEK }); location.replace(back); });
})();
