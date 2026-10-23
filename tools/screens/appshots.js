// 구현한 화면 HTML(ScreenDump 테스트가 저장)을 휴대폰 너비로 찍는다. 사용: node appshots.js <screens 폴더> <png 폴더>
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const [IN, OUT] = process.argv.slice(2);
const EDGE = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
fs.mkdirSync(OUT, { recursive: true });
const sleep = (ms) => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
let run = 0;
for (const f of fs.readdirSync(IN).filter((x) => x.endsWith('.html')).sort()) {
  const png = path.join(OUT, f.replace('.html', '.png'));
  fs.rmSync(png, { force: true });
  for (let t = 0; t < 3 && !fs.existsSync(png); t++) {
    spawnSync(EDGE, ['--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=2', '--window-size=500,1500',
      '--user-data-dir=' + path.join(OUT, '..', 'edge-app-' + (run++)), '--virtual-time-budget=3000',
      '--screenshot=' + png, 'file:///' + path.join(IN, f).replace(/\\/g, '/')], { timeout: 60000 });
    for (let i = 0; i < 10 && !fs.existsSync(png); i++) sleep(500);
  }
  console.log(f, fs.existsSync(png) ? 'ok' : 'MISSING');
}
