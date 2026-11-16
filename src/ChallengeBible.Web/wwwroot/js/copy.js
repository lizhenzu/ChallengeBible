// 성경필사 따라 치기(ADR 0099, 확정 시안 성경필사 ②): 글자만 비교한다(띄어쓰기 · 문장 부호 · 대소문자 무시, R-306 — 서버 CopyService.Letters 와 같다).
// 맞은 글자 초록 · 틀린 글자 빨강 · 남은 글자 회색. 다 맞게 치면 저장(?handler=Save)하고 아직 안 쓴 다음 절로 넘어간다.
(() => {
  const root = document.querySelector('[data-bc-write]');
  if (!root) return;
  const data = JSON.parse(root.querySelector('[data-bc-data]').textContent);
  const $ = s => root.querySelector(s);
  const verseEl = $('[data-bc-verse]'), input = $('[data-bc-input]'), err = $('[data-bc-err]');
  const prevBtn = $('[data-bc-prev]'), nextBtn = $('[data-bc-next]'), doneBox = $('[data-bc-done]');
  const dotsEl = $('[data-bc-dots]'), trEl = $('[data-bc-tr]');
  const counter = document.querySelector('.b2top .b2r-right');   // 머리말 "n / m절"(ADR 0105: 통독과 같은 틀)
  const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
  const done = new Set(data.done);
  const verses = data.verses;   // [{ v, t: { KRV, KLB, NIV } }]
  const isLetter = c => /[\p{L}\p{Nd}]/u.test(c);
  const letters = s => Array.from((s || '').normalize('NFC')).filter(isLetter).map(c => c.toLowerCase());
  let tr = null;
  try { tr = localStorage.getItem('bc-tr'); } catch { }
  if (!data.codes.includes(tr)) tr = data.codes[0];
  let idx = Math.max(0, verses.findIndex(x => x.v === data.start));
  let saving = false;

  const text = () => verses[idx].t[tr];
  const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');

  function drawChips() {
    // 번역 칸(ADR 0105): 성경통독 본문 화면의 번역 줄과 같은 모양(a · aria-current)
    trEl.innerHTML = data.codes.map((c, i) => `<a href="#" role="button" data-tr="${c}" aria-current="${c === tr}">${data.names[i]}</a>`).join('');
  }
  function drawDots() {
    dotsEl.innerHTML = verses.map((x, i) => `<button type="button" class="${done.has(x.v) ? 'd' : ''}${i === idx ? ' c' : ''}" data-i="${i}" aria-label="${x.v}절${done.has(x.v) ? ' · 다 씀' : ''}"></button>`).join('');
    if (counter) counter.textContent = `${done.size} / ${verses.length}절`;
    // 머리 띠 절 진도(성경통독과 같은 막대)
    const pct = verses.length ? Math.floor(done.size * 100 / verses.length) : 0;
    const cnt = document.querySelector('[data-bc-count]'), pc = document.querySelector('[data-bc-pct]'), bar = document.querySelector('[data-bc-bar]');
    if (cnt) cnt.textContent = done.size; if (pc) pc.textContent = pct + '%'; if (bar) bar.style.width = pct + '%';
  }
  function paint() {
    const src = text();
    err.hidden = true;
    if (!src) {
      verseEl.innerHTML = `<span class="bc-vno">${verses[idx].v}</span><span class="todo">이 번역에는 이 절이 없어요. 다른 번역을 골라 주세요.</span>`;
      input.disabled = true;
      nextBtn.disabled = !done.has(verses[idx].v);
      nextBtn.textContent = '다음 절';
      return;
    }
    input.disabled = false;
    const typed = letters(input.value);
    const chars = Array.from(src.normalize('NFC'));
    let k = 0, html = '', cur = false, lastCls = 'todo';
    for (const c of chars) {
      let cls;
      if (isLetter(c)) {
        cls = k < typed.length ? (typed[k] === c.toLowerCase() ? 'ok' : 'bad') : 'todo';
        if (cls === 'todo' && !cur) { cls += ' cur'; cur = true; }
        k++;
      } else cls = k <= typed.length && k > 0 ? (lastCls === 'bad' ? 'ok' : lastCls.replace(' cur', '')) : 'todo';
      lastCls = cls;
      html += `<span class="${cls}">${esc(c)}</span>`;
    }
    verseEl.innerHTML = `<span class="bc-vno">${verses[idx].v}</span>${html}`;
    const need = letters(src);
    const left = need.length - typed.length;
    if (left < 0) { err.textContent = `글자가 ${-left}개 더 많아요.`; err.hidden = false; }
    const isDone = done.has(verses[idx].v);
    nextBtn.disabled = !isDone;
    nextBtn.textContent = isDone ? '다음 절 →' : `다음 절 (남은 글자 ${Math.max(0, left)})`;
    prevBtn.disabled = idx === 0;
    if (typed.length === need.length && typed.every((c, i) => c === need[i]) && !saving) save(src);
  }
  function go(i, keep) {
    idx = Math.min(Math.max(0, i), verses.length - 1);
    if (!keep) input.value = '';
    drawDots();
    paint();
  }
  function nextUnwritten() {
    for (let j = idx + 1; j < verses.length; j++) if (!done.has(verses[j].v)) return j;
    for (let j = 0; j < idx; j++) if (!done.has(verses[j].v)) return j;
    return -1;
  }
  async function save(src) {
    saving = true;
    const v = verses[idx].v;
    const body = new URLSearchParams({ __RequestVerificationToken: token, book: data.book, chapter: data.chapter, verse: v, translation: tr, typed: input.value });
    try {
      const res = await fetch(root.dataset.save, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body });
      const r = await res.json();
      if (!r.ok) { err.textContent = r.error || '저장하지 못했어요. 다시 해 주세요.'; err.hidden = false; return; }
      done.add(v);
      doneBox.hidden = false;
      doneBox.querySelector('b').textContent = r.newlyDone ? `✓ ${v}절 다 썼어요 · 축하해요! 필사 홈에서 선물을 확인해 보세요` : `✓ ${v}절 다 썼어요`;
      doneBox.querySelector('p').textContent = src;
      const n = nextUnwritten();
      if (n >= 0) go(n);
      else {
        input.value = '';
        drawDots(); paint();
        doneBox.querySelector('b').textContent = `✓ 이 장을 다 썼어요${data.nextName ? ` · 다음은 ${data.nextName}` : ''}`;
        const nx = root.querySelector('[data-bc-nextch]');
        if (nx) nx.classList.add('p');
      }
    } catch {
      err.textContent = '연결이 끊겼어요. 잠시 뒤 다시 해 주세요.'; err.hidden = false;
    } finally { saving = false; }
  }

  input.addEventListener('input', paint);
  input.addEventListener('compositionend', paint);
  // 복사 · 붙여넣기 막기(2026-11-16 사용자, ADR 0105): 본문은 고를 · 복사할 수 없고, 쓰는 칸에는 붙여넣기 · 끌어 놓기를 받지 않는다(직접 따라 쓰기)
  const noPaste = (e) => { e.preventDefault(); err.textContent = '붙여넣기는 할 수 없어요. 본문을 보고 직접 따라 써 주세요.'; err.hidden = false; };
  input.addEventListener('paste', noPaste);
  input.addEventListener('drop', noPaste);
  input.addEventListener('beforeinput', (e) => { if (e.inputType === 'insertFromPaste' || e.inputType === 'insertFromDrop' || e.inputType === 'insertFromPasteAsQuotation') noPaste(e); });
  for (const ev of ['copy', 'cut', 'contextmenu', 'dragstart', 'selectstart']) verseEl.addEventListener(ev, (e) => e.preventDefault());
  input.addEventListener('copy', (e) => e.preventDefault());
  input.addEventListener('cut', (e) => e.preventDefault());
  prevBtn.addEventListener('click', () => go(idx - 1));
  nextBtn.addEventListener('click', () => go(idx + 1));
  dotsEl.addEventListener('click', e => { const b = e.target.closest('[data-i]'); if (b) go(+b.dataset.i); });
  trEl.addEventListener('click', e => {
    const b = e.target.closest('[data-tr]'); if (!b) return;
    e.preventDefault();
    tr = b.dataset.tr;
    try { localStorage.setItem('bc-tr', tr); } catch { }
    drawChips(); paint();
  });
  drawChips();
  go(idx);
})();
