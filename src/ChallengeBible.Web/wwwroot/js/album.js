// 부서 사진첩 앨범(ADR 0118): 크게 보기 · 선택(ZIP · 지우기) · 사진 올리기(한 장씩 차례로 → 진행률 · 성공/대기/실패 · 실패만 다시 · 취소)
(() => {
  const root = document.querySelector('[data-al]');
  if (!root) return;
  const base = root.dataset.alBase, photoUrl = root.dataset.alPhoto;
  const $ = s => document.querySelector(s);
  const tiles = Array.from(document.querySelectorAll('[data-al-i]'));
  let selecting = false;

  // ── 크게 보기 ──
  const vw = $('[data-al-vw]');
  let at = 0;
  const size = n => n >= 1048576 ? (n / 1048576).toFixed(1) + 'MB' : Math.max(1, Math.round(n / 1024)) + 'KB';
  function show(i) {
    at = (i + tiles.length) % tiles.length;
    const t = tiles[at];
    $('[data-al-img]').src = photoUrl + t.dataset.id;
    $('[data-al-img]').alt = t.dataset.name;
    $('[data-al-pos]').textContent = `${at + 1} / ${tiles.length}`;
    $('[data-al-meta]').textContent = `${t.dataset.name} · ${size(+t.dataset.size)}`;
    const dl = $('[data-al-dl]'); if (dl) { dl.href = photoUrl + t.dataset.id + '?dl=1'; }
    const cv = $('[data-al-cover]'); if (cv) cv.value = t.dataset.id;
    const del = $('[data-al-delone]'); if (del) { del.hidden = t.dataset.del !== '1'; $('[data-al-delid]').value = t.dataset.id; }
    vw.hidden = false;
  }
  tiles.forEach(t => t.addEventListener('click', e => { if (!selecting) { e.preventDefault(); show(+t.dataset.alI); } }));
  if (vw) {
    $('[data-al-close]').onclick = () => { vw.hidden = true; };
    $('[data-al-prev]').onclick = () => show(at - 1);
    $('[data-al-next]').onclick = () => show(at + 1);
    document.addEventListener('keydown', e => { if (vw.hidden) return; if (e.key === 'Escape') vw.hidden = true; if (e.key === 'ArrowLeft') show(at - 1); if (e.key === 'ArrowRight') show(at + 1); });
    let sx = null;
    vw.addEventListener('touchstart', e => { sx = e.touches[0].clientX; }, { passive: true });
    vw.addEventListener('touchend', e => { if (sx === null) return; const dx = e.changedTouches[0].clientX - sx; if (Math.abs(dx) > 50) show(at + (dx < 0 ? 1 : -1)); sx = null; });
  }

  // ── 선택 ──
  const bar = $('[data-al-selbar]'), form = $('[data-al-form]');
  const picked = () => tiles.filter(t => t.querySelector('input').checked);
  function count() { const n = picked().length; $('[data-al-count]').textContent = `${n}개 선택`; }
  function selectMode(on) {
    selecting = on; if (!form) return;
    form.classList.toggle('sel', on); bar.hidden = !on;
    if (!on) tiles.forEach(t => t.querySelector('input').checked = false);
    count();
  }
  $('[data-al-select]')?.addEventListener('click', () => selectMode(!selecting));
  $('[data-al-off]')?.addEventListener('click', () => selectMode(false));
  $('[data-al-all]')?.addEventListener('click', () => { const all = picked().length < tiles.length; tiles.forEach(t => t.querySelector('input').checked = all); count(); });
  form?.addEventListener('change', count);
  $('[data-al-zip]')?.addEventListener('click', () => { const ids = picked().map(t => t.dataset.id); if (ids.length) location.href = base + '?handler=Zip&ids=' + ids.join(','); });

  // ── 사진 올리기 ──
  const pick = $('[data-al-files]'), up = $('[data-al-up]');
  if (!pick) return;
  const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
  let queue = [], xhr = null, stopped = false, changed = false;
  const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');
  function draw() {
    const total = queue.length, ok = queue.filter(q => q.s === 'ok').length, bad = queue.filter(q => q.s === 'bad').length, wait = total - ok - bad;
    const done = ok + bad;
    $('[data-al-n]').textContent = `${done} / ${total}`;
    $('[data-al-pct]').textContent = total ? Math.round(done * 100 / total) + '%' : '0%';
    $('[data-al-bar]').style.width = total ? (done * 100 / total) + '%' : '0';
    $('[data-al-ok]').textContent = ok; $('[data-al-wait]').textContent = wait; $('[data-al-bad]').textContent = bad;
    const now = queue.find(q => q.s === 'now');
    $('[data-al-now]').textContent = now ? `지금: ${now.f.name}` : (wait === 0 ? `끝났어요 · 성공 ${ok}장${bad ? ` · 실패 ${bad}장` : ''}` : (stopped ? '멈췄어요' : ''));
    $('[data-al-list]').innerHTML = queue.map(q => `<li class="${q.s}"><span>${esc(q.f.name)}${q.err ? `<small>${esc(q.err)}</small>` : ''}</span><em>${q.s === 'ok' ? '✓ 완료' : q.s === 'bad' ? '실패' : q.s === 'now' ? `올리는 중 ${q.p || 0}%` : '대기'}</em></li>`).join('');
    const running = queue.some(q => q.s === 'now' || q.s === 'wait') && !stopped;
    $('[data-al-cancel]').hidden = !running;
    $('[data-al-retry]').hidden = running || bad === 0;
    $('[data-al-done]').hidden = running;
  }
  function next() {
    if (stopped) { draw(); return; }
    const q = queue.find(x => x.s === 'wait');
    if (!q) { draw(); return; }
    q.s = 'now'; q.p = 0; draw();
    if (q.f.size > 20 * 1048576) { q.s = 'bad'; q.err = '20MB 를 넘어요.'; return next(); }
    thumbOf(q.f).then(t => {
      if (stopped) { q.s = 'wait'; draw(); return; }
      const fd = new FormData(); fd.append('__RequestVerificationToken', token); fd.append('file', q.f, q.f.name);
      if (t) { fd.append('thumb', t.blob, 'thumb.jpg'); fd.append('width', t.w); fd.append('height', t.h); }
      send(q, fd);
    });
  }
  // 썸네일: 브라우저가 사진을 읽어(EXIF 회전 반영) 긴 쪽 480px JPEG 로 줄인다. 못 읽으면 없이 보낸다(서버가 원본을 쓴다)
  async function thumbOf(f) {
    try {
      const bmp = await createImageBitmap(f, { imageOrientation: 'from-image' });
      const k = Math.min(1, 480 / Math.max(bmp.width, bmp.height));
      const c = document.createElement('canvas'); c.width = Math.max(1, Math.round(bmp.width * k)); c.height = Math.max(1, Math.round(bmp.height * k));
      const g = c.getContext('2d'); g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height); g.drawImage(bmp, 0, 0, c.width, c.height);
      const blob = await new Promise(r => c.toBlob(r, 'image/jpeg', 0.8));
      const r = blob ? { blob, w: bmp.width, h: bmp.height } : null; bmp.close?.(); return r;
    } catch { return null; }
  }
  function send(q, fd) {
    xhr = new XMLHttpRequest();
    xhr.open('POST', base + '?handler=Upload');
    xhr.upload.onprogress = e => { if (e.lengthComputable) { q.p = Math.round(e.loaded * 100 / e.total); draw(); } };
    xhr.onload = () => {
      let r = null; try { r = JSON.parse(xhr.responseText); } catch { }
      if (xhr.status === 200 && r && r.ok) { q.s = 'ok'; changed = true; } else { q.s = 'bad'; q.err = (r && r.error) || '연결이 끊겼어요. 다시 해 주세요.'; }
      next();
    };
    xhr.onerror = () => { q.s = 'bad'; q.err = '연결이 끊겼어요. 다시 해 주세요.'; next(); };
    xhr.onabort = () => { q.s = 'wait'; q.p = 0; draw(); };
    xhr.send(fd);
  }
  pick.addEventListener('change', () => {
    const files = Array.from(pick.files); pick.value = '';
    if (!files.length) return;
    if (queue.every(q => q.s === 'ok' || q.s === 'bad')) queue = [];
    files.sort((a, b) => a.name.localeCompare(b.name, 'ko', { numeric: true }));   // 파일 이름 차례로
    queue.push(...files.map(f => ({ f, s: 'wait' })));
    stopped = false; up.hidden = false;
    if (!queue.some(q => q.s === 'now')) next(); else draw();
  });
  $('[data-al-cancel]').onclick = () => { stopped = true; xhr?.abort(); draw(); };
  $('[data-al-retry]').onclick = () => { queue.forEach(q => { if (q.s === 'bad') { q.s = 'wait'; q.err = null; } }); stopped = false; next(); };
  $('[data-al-done]').onclick = () => { if (changed) location.reload(); else up.hidden = true; };
  window.addEventListener('beforeunload', e => { if (queue.some(q => q.s === 'now' || q.s === 'wait') && !stopped) e.preventDefault(); });
})();
