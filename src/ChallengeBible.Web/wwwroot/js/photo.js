// 얼굴 사진 올리기(ADR 0078, R-202, 확정 시안 crop.png): 사진을 고르면 자르기 화면이 열린다.
// 끌어서 옮기고 · 두 손가락(또는 막대 · 휠)으로 키우고 · 90°씩 돌린 뒤 "이대로 올리기" → 동그라미 안만 320px JPEG 로 올린다.
// 사진을 읽지 못하면(오래된 브라우저) 고른 파일 그대로 올리고, 크기는 서버가 400KB 로 막는다.
(function () {
  var OUT = 320, MAXZ = 4;

  function el(tag, cls, html) { var e = document.createElement(tag); if (cls) e.className = cls; if (html) e.innerHTML = html; return e; }

  function send(form, blob) {
    var fd = new FormData(form);
    fd.delete('photo');
    fd.append('photo', blob, 'photo.jpg');
    fetch(form.getAttribute('action') || location.href, { method: 'POST', body: fd, credentials: 'same-origin' })
      .then(function (r) {
        if (r.redirected) { location.href = r.url; return; }
        return r.text().then(function (html) { document.open(); document.write(html); document.close(); });
      })
      .catch(function () { alert('사진을 올리지 못했어요. 잠시 뒤 다시 해 주세요.'); });
  }

  function open(form, input, img) {
    var ov = el('div', 'crop', '<div class="crop-bar"><button type="button" class="crop-x">✕ 취소</button><b>사진 자르기</b><button type="button" class="crop-ok">완료</button></div>' +
      '<div class="crop-stage"><canvas></canvas><div class="crop-mask"></div><div class="crop-ring"></div><p class="crop-tip">손가락으로 끌어 옮기고, 두 손가락이나 아래 막대로 키워요</p></div>' +
      '<div class="crop-zoom"><span aria-hidden="true">－</span><input type="range" min="1" max="' + MAXZ + '" step="0.01" value="1" aria-label="크게 보기" /><span aria-hidden="true">＋</span></div>' +
      '<div class="crop-btns"><button type="button" class="crop-rot">↻ 돌리기</button><button type="button" class="crop-go">이대로 올리기</button></div>');
    ov.setAttribute('role', 'dialog');
    ov.setAttribute('aria-label', '사진 자르기');
    document.body.appendChild(ov);
    document.documentElement.classList.add('crop-on');

    var stage = ov.querySelector('.crop-stage'), cv = stage.querySelector('canvas'), ctx = cv.getContext('2d'), range = ov.querySelector('input[type=range]');
    var dpr = window.devicePixelRatio || 1, W = 0, H = 0, R = 0;
    var zoom = 1, rot = 0, x = 0, y = 0;   // x · y = 사진 가운데가 동그라미 가운데에서 벗어난 거리(화면 px)

    function dims() { var q = rot % 180 !== 0; return { w: q ? img.naturalHeight : img.naturalWidth, h: q ? img.naturalWidth : img.naturalHeight }; }
    function k() { var d = dims(); return (2 * R / Math.min(d.w, d.h)) * zoom; }   // zoom 1 = 동그라미를 꼭 덮는 크기
    function clamp() {
      var d = dims(), s = k(), mx = Math.max(0, d.w * s / 2 - R), my = Math.max(0, d.h * s / 2 - R);
      x = Math.min(mx, Math.max(-mx, x)); y = Math.min(my, Math.max(-my, y));
    }
    function paint(c, size, f) {   // f = 화면 px → 그릴 판 px
      c.save();
      c.translate(size.w / 2 + x * f, size.h / 2 + y * f);
      c.rotate(rot * Math.PI / 180);
      var s = k() * f;
      c.drawImage(img, -img.naturalWidth * s / 2, -img.naturalHeight * s / 2, img.naturalWidth * s, img.naturalHeight * s);
      c.restore();
    }
    function draw() {
      clamp();
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, W, H);
      paint(ctx, { w: W, h: H }, 1);
    }
    function layout() {
      W = stage.clientWidth; H = stage.clientHeight;
      R = Math.max(60, Math.min(W, H) / 2 - 28);
      cv.width = W * dpr; cv.height = H * dpr; cv.style.width = W + 'px'; cv.style.height = H + 'px';
      stage.style.setProperty('--r', R + 'px');
      draw();
    }
    function setZoom(z) { zoom = Math.min(MAXZ, Math.max(1, z)); range.value = zoom; draw(); }

    // 끌기 · 두 손가락 키우기
    var pts = {}, last = null, pinch = null;
    stage.addEventListener('pointerdown', function (e) { stage.setPointerCapture(e.pointerId); pts[e.pointerId] = { x: e.clientX, y: e.clientY }; last = { x: e.clientX, y: e.clientY }; pinch = null; });
    stage.addEventListener('pointermove', function (e) {
      if (!pts[e.pointerId]) return;
      pts[e.pointerId] = { x: e.clientX, y: e.clientY };
      var ids = Object.keys(pts);
      if (ids.length >= 2) {
        var a = pts[ids[0]], b = pts[ids[1]], dist = Math.hypot(a.x - b.x, a.y - b.y);
        if (pinch) setZoom(pinch.z * dist / pinch.d); else pinch = { d: dist, z: zoom };
        return;
      }
      if (last) { x += e.clientX - last.x; y += e.clientY - last.y; last = { x: e.clientX, y: e.clientY }; draw(); }
    });
    function up(e) { delete pts[e.pointerId]; pinch = null; var ids = Object.keys(pts); last = ids.length ? pts[ids[0]] : null; }
    stage.addEventListener('pointerup', up);
    stage.addEventListener('pointercancel', up);
    stage.addEventListener('wheel', function (e) { e.preventDefault(); setZoom(zoom * (e.deltaY < 0 ? 1.08 : 1 / 1.08)); }, { passive: false });
    range.addEventListener('input', function () { setZoom(parseFloat(range.value)); });
    ov.querySelector('.crop-rot').addEventListener('click', function () { rot = (rot + 90) % 360; var t = x; x = -y; y = t; draw(); });

    function close() { ov.remove(); document.documentElement.classList.remove('crop-on'); window.removeEventListener('resize', layout); document.removeEventListener('keydown', esc); input.value = ''; }
    function esc(e) { if (e.key === 'Escape') close(); }
    ov.querySelector('.crop-x').addEventListener('click', close);
    document.addEventListener('keydown', esc);
    window.addEventListener('resize', layout);

    function go() {
      var out = document.createElement('canvas'); out.width = out.height = OUT;
      var c = out.getContext('2d');
      c.fillStyle = '#fff'; c.fillRect(0, 0, OUT, OUT);
      paint(c, { w: OUT, h: OUT }, OUT / (2 * R));   // 동그라미(지름 2R) = 판 전체
      ov.querySelector('.crop-go').disabled = true;
      ov.querySelector('.crop-go').textContent = '올리는 중…';
      out.toBlob(function (blob) { send(form, blob); }, 'image/jpeg', 0.88);
    }
    ov.querySelector('.crop-go').addEventListener('click', go);
    ov.querySelector('.crop-ok').addEventListener('click', go);
    layout();
  }

  document.querySelectorAll('form[data-photo]').forEach(function (form) {
    var input = form.querySelector('input[type=file]');
    if (!input) return;
    input.addEventListener('change', function () {
      var file = input.files && input.files[0];
      if (!file) return;
      var url = URL.createObjectURL(file), img = new Image();
      img.onload = function () { open(form, input, img); };
      img.onerror = function () { URL.revokeObjectURL(url); form.submit(); };
      img.src = url;
    });
  });
})();
