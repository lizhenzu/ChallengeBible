// 브랜드 그림 만들기(ADR 0114 「네잎 정돈」): 아이콘 PNG · SVG · favicon · 카톡 대표 이미지(og.png 1200×630).
// 사용: node tools/brand/make.js   (Playwright + chromium 필요. favicon.ico 는 python Pillow 로 fav16/32/48.png 를 묶는다)
const fs=require('fs');const path=require('path');const {chromium}=require('playwright');
const W=path.resolve(__dirname,'../../src/ChallengeBible.Web/wwwroot');const T=__dirname;
const H="M0 0 C -70 -45 -92 -92 -64 -122 C -40 -146 -12 -134 0 -110 C 12 -134 40 -146 64 -122 C 92 -92 70 -45 0 0Z";
const C=['#22C55E','#3B82F6','#F97316','#EC4899'];
const mark=(cx,cy,s)=>`<g transform="translate(${cx} ${cy}) scale(${s})">${C.map((c,i)=>`<path transform="rotate(${45+90*i}) translate(0 -16)" d="${H}" fill="${c}"/>`).join('')}<path d="M0 6 Q 4 120 58 176" stroke="#fff" stroke-width="30" fill="none" stroke-linecap="round"/><path d="M0 6 Q 4 120 58 176" stroke="#16A34A" stroke-width="16" fill="none" stroke-linecap="round"/></g>`;
const svg=(inner,bg=true)=>`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">${bg?'<rect width="512" height="512" fill="#FFFFFF"/>':''}${inner}</svg>`;
const full=svg(mark(256,222,.95));            // 홈 화면 아이콘(네모 꽉 채움, 휴대폰이 모서리를 깎는다)
const mask=svg(mark(256,232,.72));            // 안드로이드 maskable: 가운데 80% 안전 영역
const fav=svg(mark(256,214,1.14),false);      // 탭 아이콘: 바탕 없이 크게
fs.writeFileSync(W+'/icons/clover.svg',full+'\n');
fs.writeFileSync(W+'/icons/favicon.svg',fav+'\n');
(async()=>{const b=await chromium.launch();
 const shot=async(s,size,out,transparent=false)=>{const p=await b.newPage({viewport:{width:size,height:size}});await p.setContent(`<html><body style="margin:0;background:transparent">${s.replace('<svg ','<svg width="'+size+'" height="'+size+'" ')}</body></html>`);await p.screenshot({path:out,omitBackground:transparent});await p.close()};
 await shot(full,512,W+'/icons/icon-512.png');await shot(full,192,W+'/icons/icon-192.png');await shot(full,180,W+'/icons/apple-touch-icon.png');
 await shot(mask,512,W+'/icons/icon-maskable-512.png');
 for(const z of [16,32,48]) await shot(fav,z,T+'/fav'+z+'.png',true);
 const p=await b.newPage({viewport:{width:1200,height:630}});await p.goto('file://'+T+'/og.html',{waitUntil:'networkidle'});await p.waitForTimeout(1500);await p.screenshot({path:W+'/images/og.png'});
 await b.close();
 const {spawnSync}=require('child_process');
 spawnSync('python3',['-c',"from PIL import Image;import sys;d=sys.argv[1];i=[Image.open(d+'/fav%d.png'%z).convert('RGBA') for z in (16,32,48)];i[2].save(sys.argv[2],sizes=[(16,16),(32,32),(48,48)],append_images=i[:2])",T,W+'/favicon.ico'],{stdio:'inherit'});
 for(const z of [16,32,48]) fs.rmSync(T+'/fav'+z+'.png',{force:true});
})();
