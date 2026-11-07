// 말씀 입력 점검(확정 시안 word-check, R-216): 서버가 준 한 달치 자료로 완성 수 · 칩 · 달력 · 그날 칸을 그린다.
// 사용자가 입력한 글은 esc 를 거쳐서만 화면에 넣는다.
(function () {
  var data = document.getElementById('wkData');
  if (!data) return;
  var d = JSON.parse(data.textContent);
  var ITEMS = ['성경 범위', '제목', '오늘 이야기', '영상 구간'];
  var NONE = '말씀 없음', DRAFT = '초안';
  var WEEK = '일월화수목금토';
  var parts = d.month.split('-');
  var year = +parts[0], month = +parts[1];
  var lastDay = new Date(year, month, 0).getDate();
  var lead = new Date(year, month - 1, 1).getDay();
  var sel = +d.initial.slice(8);
  var filter = null;
  var rows = {};
  d.days.forEach(function (x) { rows[+x.date.slice(8)] = x; });

  var $ = function (id) { return document.getElementById(id); };
  function esc(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function isDraft(x) { return x.status !== 'Published'; }
  function isFull(x) { return !!x && !x.missing.length && !isDraft(x); }
  function hits(day, key) {
    var x = rows[day];
    if (key === NONE) return !x;
    if (!x) return false;
    return key === DRAFT ? isDraft(x) : x.missing.indexOf(key) >= 0;
  }
  function count(key) {
    var n = 0;
    for (var day = 1; day <= lastDay; day++) if (hits(day, key)) n++;
    return n;
  }
  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function label(day) {
    var x = rows[day], head = month + '월 ' + day + '일';
    if (!x) return head + ', ' + NONE;
    var says = x.missing.length ? '빠진 곳 ' + x.missing.join(' · ') : isDraft(x) ? '' : '모두 입력';
    return head + (says ? ', ' + says : '') + (isDraft(x) ? ', ' + DRAFT : '');
  }

  // 그려 두고 상태만 바꾼다(다시 그리면 키보드 초점이 사라진다).
  function buildChips() {
    var full = 0;
    for (var day = 1; day <= lastDay; day++) if (isFull(rows[day])) full++;
    $('wkDone').innerHTML = lastDay + '일 중 <b>' + full + '일</b> 완성';
    var keys = [NONE, DRAFT].concat(ITEMS), html = '';
    keys.forEach(function (k) {
      var n = count(k), name = k === NONE || k === DRAFT ? k : k + ' 없음';
      html += '<button type="button" class="' + (n === 0 ? 'z' : k === NONE ? 'g' : 'n') + '" data-k="' + esc(k) + '"' + (n === 0 ? ' disabled' : ' aria-pressed="false"') + '>' + esc(name) + ' ' + n + '</button>';
    });
    $('wkChips').innerHTML = html;
  }
  function buildGrid() {
    var html = '';
    for (var i = 0; i < lead; i++) html += '<span class="wk-out"></span>';
    for (var day = 1; day <= lastDay; day++) {
      var x = rows[day];
      var cls = !x ? 'none' : isFull(x) ? 'all' : 'some';
      if (x && isDraft(x)) cls += ' dr';
      if (new Date(year, month - 1, day).getDay() === 0) cls += ' sun';
      var dots = ITEMS.map(function (k) { return '<i' + (!x || x.missing.indexOf(k) >= 0 ? ' class="o"' : '') + '></i>'; }).join('');
      html += '<button type="button" class="wk-cell ' + cls + '" data-day="' + day + '" aria-label="' + esc(label(day)) + '"><span class="no">' + day + '</span><span class="wk-dots">' + dots + '</span></button>';
    }
    $('wkGrid').innerHTML = html;
  }
  function field(name, value, none) {
    return '<div class="wk-f"><b>' + name + (none ? '<span class="wk-tag">없음</span>' : '') + '</b>' + (value ? '<p>' + esc(value) + '</p>' : '') + '</div>';
  }
  function drawDay() {
    var x = rows[sel];
    var date = new Date(year, month - 1, sel);
    var edit = '/platform/content/word?month=' + d.month + '&date=' + d.month + '-' + pad(sel);
    var html = '<h2>' + month + '월 ' + sel + '일 ' + WEEK.charAt(date.getDay()) + '요일</h2>';
    if (!x) {
      html += '<p class="wk-empty">이 날은 아직 말씀이 없어요</p><p class="wk-note">이대로 두면 아이들 화면에 "오늘은 없음"으로 보여요.</p>'
        + '<a class="wk-go" href="' + edit + '">말씀 입력하기</a>';
    } else {
      html += '<p class="wk-ref">' + (x.ref ? esc(x.ref) + ' ' : '') + '<span class="wk-tag' + (isDraft(x) ? '' : ' ok') + '">' + (isDraft(x) ? DRAFT : '공개') + '</span></p><div class="wk-st">';
      ITEMS.forEach(function (k) {
        var no = x.missing.indexOf(k) >= 0;
        html += '<div class="' + (no ? 'x' : 'y') + '">' + (no ? '✕ ' : '✓ ') + k + '</div>';
      });
      html += '</div>' + field('제목', x.title, !x.title) + field('오늘 이야기', x.summary, !x.summary);
      if (x.commentary) html += field('해설', x.commentary, false);
      var clipNone = x.missing.indexOf('영상 구간') >= 0;
      var lines = x.clips.map(function (c) { return esc(c.chapter) + ' ' + esc(c.range); }).join('<br>');
      html += '<div class="wk-f"><b>영상 구간' + (clipNone ? '<span class="wk-tag">없음</span>' : '') + '</b>'
        + (lines ? '<p>' + lines + '</p>' : '')
        + (clipNone ? '<p class="wk-note">시점을 정한 구간이 없으면 그 장의 기본 듣기 영상을 처음부터 끝까지 재생해요.</p>' : '') + '</div>'
        + '<a class="wk-go" href="' + edit + '">말씀 수정</a>';
    }
    $('wkDay').innerHTML = html;
  }
  function apply() {
    Array.prototype.forEach.call($('wkGrid').querySelectorAll('.wk-cell'), function (c) {
      var day = +c.getAttribute('data-day');
      c.classList.toggle('sel', day === sel);
      c.classList.toggle('dim', !!filter && !hits(day, filter));
      if (day === sel) c.setAttribute('aria-current', 'true'); else c.removeAttribute('aria-current');
    });
    Array.prototype.forEach.call($('wkChips').querySelectorAll('button:not([disabled])'), function (b) {
      var on = b.getAttribute('data-k') === filter;
      b.classList.toggle('on', on);
      b.setAttribute('aria-pressed', on ? 'true' : 'false');
    });
  }

  $('wkChips').addEventListener('click', function (e) {
    var b = e.target.closest('button');
    if (!b || b.disabled) return;
    var k = b.getAttribute('data-k');
    filter = filter === k ? null : k;
    apply();
  });
  $('wkGrid').addEventListener('click', function (e) {
    var c = e.target.closest('.wk-cell');
    if (!c) return;
    sel = +c.getAttribute('data-day');
    apply();
    drawDay();
  });
  $('wkMonth').querySelector('input').addEventListener('change', function () {
    if (this.value) this.form.submit();
  });

  buildChips();
  buildGrid();
  drawDay();
  apply();
})();
