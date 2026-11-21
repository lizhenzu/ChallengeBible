// 교사 · 임원 및 기타(2026-11-21 확정 시안 역할-담임-시안2): 칸 · 칩을 누르면 바닥 창. 바꾸기는 보통 폼 보내기(서버가 규칙을 다시 확인)
(() => {
  const root = document.querySelector('[data-stf]'); if (!root) return;
  const mode = root.dataset.stf, base = root.dataset.base;
  let D = {}; try { D = JSON.parse(document.getElementById('stData').textContent || '{}'); } catch { }
  const dim = root.querySelector('[data-sheet]'), body = root.querySelector('[data-body]'), title = document.getElementById('stTitle');
  const tok = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const ini = n => esc((n || '?').slice(0, 1));
  let opener = null;
  const open = (t, html, btn, tall = true) => { title.textContent = t; body.innerHTML = html; dim.querySelector('.st-sheet').classList.toggle('tall', tall); dim.hidden = false; document.documentElement.classList.add('st-lock'); opener = btn; setTimeout(() => body.querySelector('input,select,button')?.focus(), 30); };
  const close = () => { dim.hidden = true; document.documentElement.classList.remove('st-lock'); opener?.focus(); };
  dim.addEventListener('click', e => { if (e.target === dim || e.target.closest('[data-close]')) close(); });
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && !dim.hidden) close(); });
  const form = (handler, fields, label, cls, ask) =>
    `<form method="post" action="${base}?handler=${handler}" data-ask="${esc(ask || '')}"><input type="hidden" name="__RequestVerificationToken" value="${esc(tok)}">` +
    Object.entries(fields).map(([k, v]) => `<input type="hidden" name="${k}" value="${esc(v)}">`).join('') +
    `<button type="submit" class="${cls || 'st-go'}">${esc(label)}</button></form>`;
  body.addEventListener('submit', e => { const a = e.target.dataset.ask; if (a && !confirm(a)) e.preventDefault(); });
  // 찾기 칸: 이름으로 걸러 보기
  const finder = (ph) => `<input class="st-q" type="search" placeholder="${esc(ph)}" aria-label="${esc(ph)}" autocomplete="off" data-q>`;
  body.addEventListener('input', e => {
    if (!e.target.matches('[data-q]')) return;
    const t = e.target.value.replace(/\s+/g, '');
    let n = 0;
    body.querySelectorAll('[data-name]').forEach(r => { const ok = !t || r.dataset.name.includes(t); r.hidden = !ok; if (ok) n++; });
    const none = body.querySelector('[data-none]'); if (none) none.hidden = n > 0;
  });
  const row = (p, sub, right) => `<div class="st-pr" data-name="${esc(p.n.replace(/\s+/g, ''))}"><span class="st-av" aria-hidden="true">${ini(p.n)}</span><span class="n"><b>${esc(p.n)}</b><small>${esc(sub)}</small></span>${right}</div>`;

  if (mode === 'teachers') {
    const cls = new Map((D.classes || []).map(c => [c.id, c]));
    const ppl = new Map((D.people || []).map(p => [p.id, p]));
    const sub = p => [p.l, p.c ? (cls.get(p.c)?.n || '') + (p.t ? '' : ' 학생') : '반 없음', p.ph].filter(Boolean).join(' · ');
    root.addEventListener('click', e => {
      const b = e.target.closest('[data-act]'); if (!b || b.closest('[data-sheet]')) return;
      const act = b.dataset.act, cid = +b.dataset.cid, pid = +b.dataset.pid;
      if (act === 'chip') {
        const p = ppl.get(pid), c = cls.get(cid); if (!p || !c) return;
        const others = [...cls.values()].filter(x => x.id !== cid);
        // 2026-11-21 사용자 "상세가 이상하다": 사람 카드 + [다른 반으로 옮기기] 칸 + [이 반에서 빼기] — 내용만큼만 높이, 고르기 칸은 label 로 감싸지 않음(겹 테두리)
        const opts = others.map(x => `<option value="${x.id}">${esc(x.n)}</option>`).join('');
        open(`${c.n} ${c.nc ? '담당' : '담임'}`,
          `<div class="st-card"><span class="st-av" aria-hidden="true">${ini(p.n)}</span><span class="n"><b>${esc(p.n)}</b><small>${esc([p.l, p.ph].filter(Boolean).join(' · '))}</small></span></div>` +
          (others.length ? `<div class="st-box"><div class="st-box-h" id="stToL">다른 반으로 옮기기</div>` +
            `<form method="post" action="${base}?handler=Move" class="st-move" data-ask="${esc(p.n)}님을 다른 반으로 옮길까요?"><input type="hidden" name="__RequestVerificationToken" value="${esc(tok)}"><input type="hidden" name="fromId" value="${cid}"><input type="hidden" name="personId" value="${pid}">` +
            `<select class="st-sel" name="toId" aria-labelledby="stToL">${opts}</select><button type="submit" class="st-go">옮기기</button></form></div>` : '') +
          `<div class="st-out">${form('Remove', { classId: cid, personId: pid }, `${c.n}에서 빼기`, 'st-go ghost wide', `${p.n}님을 ${c.n}에서 뺄까요?`)}</div>`, b, false);
      } else if (act === 'add') {
        const c = cls.get(cid); if (!c) return;
        // 담임 후보: 반이 없는 교사 · 임원(새친구반 담당은 관리자도) — 서버가 다시 확인한다
        const list = [...ppl.values()].filter(p => !p.c && (p.k === 2 || p.k === 3 || (c.nc && p.a)))
          .sort((a, b) => (a.k === 2 ? 0 : 1) - (b.k === 2 ? 0 : 1) || a.n.localeCompare(b.n, 'ko'));
        const word = c.nc ? '담당으로' : '담임으로';
        open(`${c.n} — 누구로 할까요?`, `<p class="st-p">반이 없는 교사 · 임원이에요. 다른 반 선생님은 그 반 칩에서 [다른 반으로]를 써 주세요.</p>` + finder('이름으로 찾기') +
          `<div class="st-list">${list.map(p => row(p, sub(p), form('Add', { classId: cid, personId: p.id }, word, 'st-go', `${p.n}님을 ${c.n} ${word.replace('으로', '')}으로 넣을까요?`))).join('')}</div>` +
          `<p class="st-none" data-none ${list.length ? 'hidden' : ''}>넣을 수 있는 사람이 없어요.</p>`, b);
      } else if (act === 'pick') {
        const p = ppl.get(pid); if (!p) return;
        const list = [...cls.values()];
        open(`${p.n} — 어느 반?`, `<p class="st-p">${esc(p.l)}${p.ph ? ' · ' + esc(p.ph) : ''}</p><div class="st-list">` +
          list.map(c => `<div class="st-pr"><span class="n"><b>${esc(c.n)}</b><small>${esc([...ppl.values()].filter(x => x.c === c.id && x.t).map(x => x.n).join(' · ') || (c.nc ? '담당 없음' : '담임 없음'))}</small></span>${form('Add', { classId: c.id, personId: pid }, c.nc ? '담당으로' : '넣기', 'st-go', `${p.n}님을 ${c.n}에 넣을까요?`)}</div>`).join('') + `</div>`, b);
      }
    });
  } else {
    const slots = new Map((D.slots || []).map(s => [s.c, s]));
    const helpers = D.helpers || [];
    const choices = D.choices || [];
    const label = c => choices.find(x => x.c === c)?.l || slots.get(c)?.l || c || '역할 없음';
    const ppl = (D.people || []).filter(p => !p.lk);
    const askText = (p, code) => `${p.n}님을 ${label(code)}(으)로 바꿀까요?` + ((p.a || slots.get(code)?.adm) ? '\n(부서 관리자 여부가 함께 바뀔 수 있어요)' : '');
    const roleForm = (p, code, text, cls) => form('Role', { personId: p.id, code }, text, cls, askText(p, code));
    const pickSelect = (p) => `<select class="st-sel" data-pick="${p.id}">${choices.map(c => `<option value="${c.c}" ${c.c === p.c ? 'selected' : ''}>${esc(c.l)}</option>`).join('')}</select>`;
    root.addEventListener('click', e => {
      const b = e.target.closest('[data-act]'); if (!b || b.closest('[data-sheet]')) return;
      if (b.dataset.act === 'slot') {
        const code = b.dataset.code, s = slots.get(code); if (!s) return;
        const now = ppl.filter(p => p.c === code), rest = ppl.filter(p => p.c !== code)
          .sort((a, b) => ((a.c === 'T1001' || !a.c) ? 1 : 0) - ((b.c === 'T1001' || !b.c) ? 1 : 0) || a.n.localeCompare(b.n, 'ko'));
        open(`${s.l} — 누구로 할까요?`,
          `<p class="st-p">지금 ${esc(s.l)}: ${now.length ? esc(now.map(p => p.n).join(' · ')) : '없음'}${s.one ? ' · 한 명 자리' : ''}${s.adm ? ` · ${esc(s.l)}은(는) 부서 관리자가 돼요` : ''}</p>` +
          (now.length ? `<div class="st-sub">지금 ${esc(s.l)} — 다른 역할로 바꾸기</div><div class="st-list">` +
            now.map(p => row(p, [p.l, p.ph].filter(Boolean).join(' · '), `<span class="st-pair">${pickSelect(p)}${form('Role', { personId: p.id, code }, '바꾸기', 'st-go', '')}</span>`)).join('') + `</div>` : '') +
          `<div class="st-sub">다른 사람을 ${esc(s.l)}(으)로</div>` + finder('이름으로 찾기') +
          `<div class="st-list">${rest.map(p => row(p, [p.l, p.ph].filter(Boolean).join(' · '), roleForm(p, code, `${s.l}(으)로`))).join('')}</div><p class="st-none" data-none hidden>맞는 사람이 없어요.</p>`, b);
        // 지금 사람의 선택 칸 → 그 줄 폼의 code 를 바꾼다
        body.querySelectorAll('[data-pick]').forEach(sel => {
          const f = sel.parentElement.querySelector('form'); const p = ppl.find(x => x.id === +sel.dataset.pick);
          const sync = () => { f.querySelector('input[name=code]').value = sel.value; f.dataset.ask = askText(p, sel.value); f.querySelector('button').disabled = sel.value === p.c; };
          sel.addEventListener('change', sync); sync();
        });
      } else if (b.dataset.act === 'helper') {
        const rest = ppl.filter(p => !helpers.includes(p.c)).sort((a, b) => a.n.localeCompare(b.n, 'ko'));
        open('봉사 — 누구에게 맡길까요?',
          `<label class="st-lab">맡길 봉사<select class="st-sel" data-hcode>${helpers.map(c => `<option value="${c}">${esc(label(c))}</option>`).join('')}</select></label>` + finder('이름으로 찾기') +
          `<div class="st-list">${rest.map(p => row(p, [p.l, p.ph].filter(Boolean).join(' · '), roleForm(p, helpers[0], '맡기기'))).join('')}</div><p class="st-none" data-none hidden>맞는 사람이 없어요.</p>`, b);
        const sel = body.querySelector('[data-hcode]');
        const sync = () => body.querySelectorAll('form').forEach(f => { const p = ppl.find(x => x.id === +f.querySelector('input[name=personId]').value); f.querySelector('input[name=code]').value = sel.value; f.dataset.ask = askText(p, sel.value); });
        sel.addEventListener('change', sync); sync();
      }
    });
  }
})();
