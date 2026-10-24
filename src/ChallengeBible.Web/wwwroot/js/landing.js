// 로그인 전 첫 화면: 미션 네 가지 탭(누르면 그 미션 설명으로 바뀜)
document.querySelectorAll('.ld .tabs button').forEach((b) => b.addEventListener('click', () => {
  document.querySelectorAll('.ld .tabs button').forEach((x) => x.setAttribute('aria-selected', String(x === b)));
  document.querySelectorAll('.ld .pane').forEach((p) => { p.hidden = p.dataset.pane !== b.dataset.p; });
}));
