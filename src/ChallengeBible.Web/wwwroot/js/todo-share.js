// 오늘 할 일 · 선물 안내글(확정 시안 생일링크-부모안내 ①): 처음 누를 때 서버에서 안내글을 받아(링크가 없으면 만듦)
// [카톡으로 보내기](2026-11-22 사용자: 카톡 하나만) = 휴대폰 공유 창에서 카톡을 고른다, 안 되면 안내글 복사
(() => {
  const root = document.querySelector('[data-td]');
  if (!root) return;
  const token = root.querySelector('input[name=__RequestVerificationToken]')?.value ?? '';
  const cache = new Map();
  const load = async (id) => {
    if (cache.has(id)) return cache.get(id);
    const r = await fetch(location.pathname + '?handler=Share', {
      method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': token },
      body: new URLSearchParams({ id, __RequestVerificationToken: token }),
    });
    if (!r.ok) throw new Error('share');
    const d = await r.json();
    cache.set(id, d);
    return d;
  };
  const copy = async (text) => {
    try { await navigator.clipboard.writeText(text); return true; }
    catch {
      const t = document.createElement('textarea'); t.value = text; t.style.position = 'fixed'; t.style.opacity = '0';
      document.body.appendChild(t); t.select(); let ok = false; try { ok = document.execCommand('copy'); } catch { } t.remove(); return ok;
    }
  };
  const say = (box, msg) => { const p = box.querySelector('.td-toast'); p.textContent = msg; p.hidden = false; };
  root.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-share]');
    if (!b) return;
    const box = b.closest('[data-td-share]');
    let d;
    try { d = await load(box.dataset.tdShare); } catch { say(box, '안내글을 만들 수 없어요. 화면을 새로 고쳐 주세요.'); return; }
    if (navigator.share) {
      try { await navigator.share({ text: d.text }); say(box, '공유 창을 열었어요. 카톡으로 보냈다면 부모님이 고르면 이 카드가 사라져요.'); return; }
      catch (err) { if (err && err.name === 'AbortError') return; }
    }
    say(box, (await copy(d.text)) ? '안내글을 복사했어요 · 카톡에 붙여 넣어 보내 주세요' : '복사가 안 돼요. 화면을 새로 고치고 다시 눌러 주세요.');
  });
})();
