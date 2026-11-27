// 앱 설치 안내로 보내기(ADR 0115 · R-331). <head> 에서 바로 돈다. 브라우저로 연 화면만 /install 로 보내고, 아래는 보내지 않는다:
// 설치된 아이콘으로 연 화면 · [이미 설치했어요] · [나중에] 7일 안 · 이 창에서 이미 안내를 본 뒤 · 설치된 것이 확인될 때(안드로이드 · PC 크롬)
// · 자동 프로그램(검색 로봇 · 시험 브라우저) · <html data-install="off">(플랫폼 관리 · 가장 중 · 설치 안내 자신).
(() => {
  const KEY = 'cb-install';
  if (document.documentElement.dataset.install === 'off') return;
  const save = (v) => { try { localStorage.setItem(KEY, JSON.stringify(v)); } catch { /* 저장 못 해도 화면은 그대로 */ } };
  const standalone = matchMedia('(display-mode: standalone)').matches || matchMedia('(display-mode: window-controls-overlay)').matches
    || matchMedia('(display-mode: minimal-ui)').matches || navigator.standalone === true;
  if (standalone) { save({ s: 'done' }); return; }
  if (navigator.webdriver || /bot|crawl|spider|scrap|facebookexternalhit|Yeti|Daum(?!Apps)|preview|Headless|Lighthouse/i.test(navigator.userAgent)) return;   // 화면 캡처 도구(헤드리스)도
  let st;
  try {
    if (sessionStorage.getItem('cb-install-seen')) return;          // 이 창에서 이미 안내를 봤다(저장이 막힌 브라우저에서도 되풀이 없음)
    localStorage.setItem('cb-t', '1'); localStorage.removeItem('cb-t'); // 저장이 안 되는 브라우저면 보내지 않는다(되풀이 방지)
    st = JSON.parse(localStorage.getItem(KEY) || 'null');
  } catch { return; }
  if (st && (st.s === 'done' || (st.s === 'later' && Date.now() < st.until))) return;
  const go = () => location.replace('/install?back=' + encodeURIComponent(location.pathname + location.search));
  if (navigator.getInstalledRelatedApps) {
    navigator.getInstalledRelatedApps().then((a) => { if (a && a.length) save({ s: 'done' }); else go(); }, go);
  } else go();
})();
