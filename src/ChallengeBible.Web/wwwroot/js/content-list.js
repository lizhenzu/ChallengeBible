// 공용 콘텐츠(확정 시안 platform-content → 2026-11-17 시안 A): 상태 거르기(누르면 목록이 펼쳐짐), 고른 초안 수, 답 형식에 맞는 칸, 성경 범위 글, 날짜 띠 위치, 오늘 이야기 문장 수.
(function () {
  var root = document.querySelector('.cp');
  if (!root) return;
  var all = function (s, r) { return Array.prototype.slice.call((r || root).querySelectorAll(s)); };

  // 상태 거르기: 날짜를 고치고 목록으로 돌아와도 유지한다(저장 실패해도 화면은 그대로 동작).
  var key = 'cp-filter-' + root.getAttribute('data-kind');
  function load() { try { return sessionStorage.getItem(key) || ''; } catch (e) { return ''; } }
  function save(v) { try { sessionStorage.setItem(key, v); } catch (e) { /* 저장 못 해도 괜찮다 */ } }
  var chips = all('.cp-chips button');
  var rows = all('.cp-rows li');
  function filter(value) {
    chips.forEach(function (b) { b.setAttribute('aria-pressed', b.getAttribute('data-f') === value ? 'true' : 'false'); });
    rows.forEach(function (li) { li.hidden = !!value && li.getAttribute('data-status') !== value; });
    root.classList.toggle('filtering', !!value);   // 날짜를 고른 화면에서는 칩을 누를 때만 목록이 펼쳐진다
  }
  chips.forEach(function (b) {
    b.addEventListener('click', function () {
      var v = b.getAttribute('data-f');
      save(v);
      filter(v);
    });
  });
  // 날짜를 고른 화면은 편집이 먼저 — 남겨 둔 거르기로 목록을 펼치지 않는다
  var kept = root.classList.contains('has-sel') ? '' : load();
  if (chips.some(function (b) { return b.getAttribute('data-f') === kept && !b.disabled; })) filter(kept);

  // 초안을 고르면 아래에 공개 버튼이 나타난다.
  var bulk = document.getElementById('cpBulk');
  if (bulk) {
    var boxes = all('.cp-cb input');
    var button = bulk.querySelector('button');
    var update = function () {
      var n = boxes.filter(function (x) { return x.checked; }).length;
      bulk.hidden = n === 0;
      button.textContent = '고른 초안 ' + n + '개 공개';
    };
    boxes.forEach(function (x) { x.addEventListener('change', update); });
    update();
  }

  // 답 형식에 맞는 칸만 보여 준다.
  var formats = all('[data-cp-format]');
  function showFormat() {
    var on = formats.filter(function (x) { return x.checked; })[0];
    if (!on) return;
    all('[data-cp-for]').forEach(function (f) { f.hidden = f.getAttribute('data-cp-for') !== on.value; });
  }
  formats.forEach(function (x) { x.addEventListener('change', showFormat); });
  showFormat();

  // 성경 범위 글: 서버의 BibleBooks.Range 와 같은 모양.
  var book = root.querySelector('[data-cp-book]');
  var prev = root.querySelector('[data-cp-prev]');
  if (book && prev) {
    var value = function (k) { return +root.querySelector('[data-cp-r="' + k + '"]').value; };
    var draw = function () {
      var sc = value('sc'), sv = value('sv'), ec = value('ec'), ev = value('ev');
      if (!(sc >= 1 && sv >= 1 && ec >= 1 && ev >= 1)) return;
      var name = book.options[book.selectedIndex].text;
      prev.textContent = sc === ec && sv === ev ? name + ' ' + sc + ':' + sv
        : sc === ec ? name + ' ' + sc + '장 ' + sv + '~' + ev + '절'
        : name + ' ' + sc + ':' + sv + ' ~ ' + ec + ':' + ev;
    };
    book.addEventListener('change', draw);
    all('[data-cp-r]').forEach(function (x) { x.addEventListener('input', draw); });
  }

  // 날짜 띠: 고른 날이 보이게 옆으로 맞춘다.
  var strip = root.querySelector('.cp-strip');
  var picked = strip && strip.querySelector('a.on');
  if (picked && strip.scrollWidth > strip.clientWidth) strip.scrollLeft = Math.max(0, picked.offsetLeft - strip.clientWidth / 2 + picked.offsetWidth / 2);

  // 오늘 이야기: 문장 수 · 글자 수(3 ~ 5문장 안내).
  var story = root.querySelector('[data-cp-count]');
  var out = root.querySelector('[data-cp-count-out]');
  if (story && out) {
    var count = function () {
      var text = story.value.trim();
      if (!text) { out.textContent = ''; return; }
      var n = text.split(/[.!?。]+(?:\s+|$)/).filter(function (x) { return x.trim(); }).length;
      out.textContent = n + '문장 · ' + text.replace(/\s/g, '').length + '자';
      out.classList.toggle('out', n < 3 || n > 5);
    };
    story.addEventListener('input', count);
    count();
  }

  // 목록(상태 칩으로 펼친 것): 고른 줄이 보이게 한다.
  var list = root.querySelector('.cp-rows');
  var on = list && list.querySelector('li.on');
  if (on && list.scrollHeight > list.clientHeight) list.scrollTop = Math.max(0, on.offsetTop - list.clientHeight / 2);
})();
