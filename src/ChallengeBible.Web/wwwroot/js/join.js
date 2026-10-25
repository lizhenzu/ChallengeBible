// 참여 신청 v3: 부서 칸을 고르면 아래 띠에 그 부서 이름을 보인다(띠는 CSS 로 나타남)
document.querySelectorAll('.jv3 input[name="TenantId"]').forEach((r) => r.addEventListener('change', () => {
  const b = document.querySelector('[data-join-name]');
  if (b && r.checked) b.textContent = r.dataset.name;
}));
