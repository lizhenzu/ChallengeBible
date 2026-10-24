// 서비스 워커(PWA, 확정 시안: 오프라인). 주소 맨 앞(/)에 두어 모든 부서 화면을 맡는다.
// 저장하는 것: 오프라인 화면과 화면 모양(/css · /js · 부서 아이콘)뿐. 화면(HTML) · API 응답은 저장하지 않는다 —
// 점수 · 이름 같은 개인 내용이 가족이 함께 쓰는 휴대폰에 남지 않게. 미션 버튼(POST)은 손대지 않는다(나중에 모아 보내기 없음).
const VERSION = 'cb-v1';
const OFFLINE = '/offline.html';

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(VERSION).then((c) => c.add(new Request(OFFLINE, { cache: 'reload' }))).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (e) => {
  e.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
});

/** 화면 모양 파일: 같은 주소의 /css · /js · 부서 아이콘. */
function isAsset(url) {
  return url.origin === self.location.origin && (url.pathname.startsWith('/css/') || url.pathname.startsWith('/js/') || url.pathname.startsWith('/icons/')
    || /^\/[a-z0-9-]+\/icon\.svg$/.test(url.pathname));
}

self.addEventListener('fetch', (e) => {
  const req = e.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);

  // 화면: 언제나 서버에서. 연결이 없을 때만 오프라인 화면
  if (req.mode === 'navigate') {
    e.respondWith(fetch(req).catch(() => caches.match(OFFLINE)));
    return;
  }

  // 화면 모양: 저장본을 먼저 주고 뒤에서 새로 받아 둔다(파일 이름에 지문이 붙어 바뀌면 새 주소가 된다)
  if (isAsset(url)) {
    e.respondWith(caches.open(VERSION).then(async (c) => {
      const hit = await c.match(req);
      const fresh = fetch(req).then((res) => { if (res.ok) c.put(req, res.clone()); return res; }).catch(() => hit);
      return hit ?? fresh;
    }));
  }
  // 그 밖(API · 글꼴 · 다른 주소)은 브라우저 기본 그대로
});
