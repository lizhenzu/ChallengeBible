// 공용 콘텐츠(확정 시안 platform-content): 상태 거르기, 고른 초안 수, 답 형식에 맞는 칸, 성경 범위 글, 넓은 화면의 목록 위치.
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
  }
  chips.forEach(function (b) {
    b.addEventListener('click', function () {
      var v = b.getAttribute('data-f');
      save(v);
      filter(v);
    });
  });
  var kept = load();
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

  // 넓은 화면: 목록만 따로 스크롤되므로 고른 줄이 보이게 한다.
  var list = root.querySelector('.cp-rows');
  var on = list && list.querySelector('li.on');
  if (on && list.scrollHeight > list.clientHeight) list.scrollTop = Math.max(0, on.offsetTop - list.clientHeight / 2);
})();
