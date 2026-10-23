// appshots 의 PNG 를 한 페이지(사진을 안에 넣은 HTML)로 묶는다. 사용: node gallery.js <png 폴더> <출력 html>
const fs = require('fs');
const path = require('path');
const [IN, OUT] = process.argv.slice(2);
const notes = {
  '01-첫화면': '로그인 전 공통 첫 화면(간단판). 부서 이름 없음',
  '02-로그인': '로그인',
  '03-회원가입': '이름 · 아이디 · 비밀번호만',
  '04-참여신청': '부서 주소로 들어와 코드가 채워진 상태',
  '05-부서홈': '부서 홈(간단판) · 프로그램 카드',
  '06-미션홈': '올해 점수 · 다음 축하까지 · 날짜 칩 · 남은 미션 · 최근 7일',
  '07-말씀': '혼자 읽었어요 완료, 부모님께는 아직',
  '08-퀴즈': '1번 틀림(빨강), 1번 더 도전 가능',
  '09-기도': '기도문에 내 이름 강조',
  '10-암송': '이번 주 암송은 4일에 완료',
  '11-내점수': '42칸 달력(3 · 4일 완주) + 날짜별 표',
  '12-소속관리': '선생님 화면: 참여 신청 2건',
  '12b-소속관리-승인됨': '소속 탭: 김철수를 펼친 상태. 맨 아래에 보호자 연결(김영희 · 풀기)',
  '16-보호자': '보호자 김영희: 오늘 말씀 · 퀴즈 오답 · 이번 주 · 달력 · 오늘 한 일',
  '17-보호자-지난날': '달력에서 4일을 고른 상태(완주한 날)',
  '13-플랫폼부서': '운영자 화면: 부서 목록 · 새 부서 만들기',
  '14-공용콘텐츠-말씀': '운영자: 1월 날짜 목록(공개 · 없음) + 5일 말씀 편집',
  '15-공용콘텐츠-퀴즈': '운영자: 퀴즈 편집(보기 · 정답 동그라미)',
};
const files = fs.readdirSync(IN).filter((f) => f.endsWith('.png')).sort();
const cards = files.map((f) => {
  const n = f.replace('.png', '');
  const b64 = fs.readFileSync(path.join(IN, f)).toString('base64');
  const [no, ...rest] = n.split('-');
  return `<figure><figcaption><b>${no}</b> ${rest.join('-')}<small>${notes[n] ?? ''}</small></figcaption><div class="shot"><img src="data:image/png;base64,${b64}" alt="${rest.join('-')} 화면" loading="lazy"></div></figure>`;
}).join('\n');
const html = `<title>구현 화면 캡처</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Noto+Sans+KR:wght@400;700;900&display=swap">
<style>
  :root { --bg: #F5F6F8; --card: #FFFFFF; --fg: #1B1F24; --dim: #5B6470; --rule: #E3E6EA; --accent: #2B59C3; }
  @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg: #14171B; --card: #1B1F25; --fg: #E8EAED; --dim: #A3AAB4; --rule: #2C323A; --accent: #7FA2F2; color-scheme: dark } }
  :root[data-theme="dark"] { --bg: #14171B; --card: #1B1F25; --fg: #E8EAED; --dim: #A3AAB4; --rule: #2C323A; --accent: #7FA2F2; color-scheme: dark }
  body { background: var(--bg); color: var(--fg); font-family: "Noto Sans KR", system-ui, sans-serif; margin: 0;
    padding-block: 20px 48px; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); }
  header { max-width: 1100px; margin: 0 auto 18px; }
  h1 { margin: 0; font-size: 24px; font-weight: 900; }
  header p { margin: 6px 0 0; color: var(--dim); font-size: 14px; line-height: 1.7; }
  main { max-width: 1100px; margin: 0 auto; display: grid; gap: 16px; grid-template-columns: repeat(auto-fill, minmax(min(100%, 300px), 1fr)); }
  figure { margin: 0; background: var(--card); border: 1px solid var(--rule); border-radius: 16px; overflow: hidden; display: flex; flex-direction: column; }
  figcaption { padding: 12px 14px; font-size: 15px; display: flex; flex-direction: column; gap: 2px; border-bottom: 1px solid var(--rule); }
  figcaption b { color: var(--accent); margin-right: 4px; }
  figcaption small { color: var(--dim); font-size: 12.5px; }
  .shot { max-height: 720px; overflow-y: auto; background: #fff; }
  .shot img { display: block; width: 100%; height: auto; max-width: 100%; }
</style>
<header><h1>도전!!!성경말씀 · 구현 화면</h1>
<p>시안이 아니라 <b>실제로 만든 코드</b>가 그린 화면입니다(시험 데이터, 오늘 = 2027년 1월 5일, 김철수). 칸 안을 위아래로 밀면 화면 전체가 보여요.</p></header>
<main>
${cards}
</main>`;
fs.writeFileSync(OUT, html);
console.log(files.length, 'shots', Math.round(fs.statSync(OUT).size / 1024), 'KB');
