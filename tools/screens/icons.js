// 홈 화면 아이콘 PNG 만들기(아이폰 apple-touch-icon · 안드로이드 매니페스트). 대표 색 4가지(0033)마다 "도전" 두 글자.
// 사용: node tools/screens/icons.js <출력 폴더 절대경로>   예) ...\src\ChallengeBible.Web\wwwroot\icons
// 아이폰이 모서리를 직접 깎으므로 네모로 꽉 채운다. Edge 창은 500px 보다 작게 못 줄여서 512px 로 그리고 배율로 줄인다.
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const [OUT] = process.argv.slice(2);
const EDGE = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const COLORS = { blue: '#2B59C3', green: '#1F7A5C', red: '#B2462E', violet: '#6A4BB5' };   // BrandingService.ColorHex 와 같게
const SIZES = { 'apple-touch-icon': 180, 'icon-192': 192, 'icon-512': 512 };
const TEXT = '도전';
fs.mkdirSync(OUT, { recursive: true });
const tmp = path.join(OUT, '..', '..', '..', '..', 'artifacts', 'icon-tmp');
fs.mkdirSync(tmp, { recursive: true });
const sleep = (ms) => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
let run = 0;
for (const [color, hex] of Object.entries(COLORS)) {
  const html = path.join(tmp, `${color}.html`);
  fs.writeFileSync(html, `<!DOCTYPE html><html><head><meta charset="utf-8"><style>html,body{margin:0;width:512px;height:512px;overflow:hidden;background:${hex}}
    div{width:512px;height:512px;display:grid;place-items:center;color:#fff;font:900 200px/1 "Noto Sans KR","Malgun Gothic","Apple SD Gothic Neo",sans-serif;letter-spacing:-8px}</style></head>
    <body><div>${TEXT}</div></body></html>`);
  for (const [name, size] of Object.entries(SIZES)) {
    const png = path.join(OUT, `${name}-${color}.png`);
    fs.rmSync(png, { force: true });
    for (let t = 0; t < 3 && !fs.existsSync(png); t++) {
      spawnSync(EDGE, ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--force-device-scale-factor=${size / 512}`, '--window-size=512,512',
        '--user-data-dir=' + path.join(tmp, 'edge-' + (run++)), '--virtual-time-budget=2000',
        '--screenshot=' + png, 'file:///' + html.replace(/\\/g, '/')], { timeout: 60000 });
      for (let i = 0; i < 10 && !fs.existsSync(png); i++) sleep(500);
    }
    console.log(`${name}-${color}.png`, fs.existsSync(png) ? fs.statSync(png).size + ' bytes' : 'MISSING');
  }
}
