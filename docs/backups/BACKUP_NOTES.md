# 백업 파일 안내 (docs/backups)

이 폴더는 화면을 크게 바꾸기 전에 원본을 남기기 위한 백업 모음이다. 파일은 절대 앱에서 직접 사용하지 않는다(빌드·배포 대상 아님). 되돌리기 용도로만 둔다.

## 1. `TenantHome.pre-dodan.cshtml`

- **무엇의 백업**: 부서 홈(`/{slug}`, 예: `/jamsil-kids`) 상단 화면의 Razor 뷰 — `src/ChallengeBible.Web/Pages/TenantHome.cshtml`
- **어느 시점**: 커밋 `85a7409`("Merge claude/dept-student-login: dept representative student login (R-367)") 시점의 원본
- **백업 날짜**: 2026-11-22
- **백업 이유**: 2026-11-22 사용자 요청 — 기존 화면이 사용하기 조악하다는 판단으로, 부서 홈 상단을 "dodan 시안(도전!!! 성경말씀)" 디자인으로 교체(커밋 `067e5eb`)하기 전 원본 보존
- **검증**: `git show 85a7409:src/ChallengeBible.Web/Pages/TenantHome.cshtml` 와 diff 비교 결과 **완전히 동일함** 확인
- **되돌리는 방법**:
  ```bash
  # 방법 A — 백업 파일로 되돌리기
  git checkout main && git pull origin main
  cp docs/backups/TenantHome.pre-dodan.cshtml src/ChallengeBible.Web/Pages/TenantHome.cshtml
  git commit -m "부서 홈을 dodan 이전 디자인으로 복구" && git push origin main

  # 방법 B — git으로 직접 (백업 파일 없이도 가능)
  git revert 067e5eb          # 디자인 교체 커밋을 되돌린다
  git push origin main
  ```
  되돌린 뒤에는 재배포가 필요하다(호스팅이 GitHub main 을 따라간다).

## 주의

- 이 폴더의 파일을 `src/` 로 복사 없이 그대로 쓰지 말 것 — 파일명이 다르므로 Razor 라우팅에 걸리지는 않지만, 복구 시에는 위 방법대로 원래 경로에 덮어써야 한다.
- 새로운 대규모 화면 교체 시 같은 방식으로 이 폴더에 `<원본파일명>.<시점별칭>.cshtml` 로 남기고 이 문서에 한 줄을 추가한다.
