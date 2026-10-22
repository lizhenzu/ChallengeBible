# API · 화면 계약 (초안)

- 상태: 초안 (2026-10-22)
- 기준: 04 문서 화면 목록, [물리 설계](물리-설계.md), ADR 0034(화면 = Razor Pages, 동작 = JSON API)
- 표기: **1차** = MVP(06 문서 4절), **2차** = 나중

## 1. 공통 규칙

### 1-1. 주소 구조
| 접두 | 용도 | 영역 컨텍스트 |
|---|---|---|
| `/` , `/account/…` | 랜딩 · 로그인 · 가입 · 내 계정 · 영역 선택 | 없음 (TenantId 세션 값 없음) |
| `/{slug}/…` | 영역 화면 | 주소의 영역 코드 → TenantId (0008) |
| `/api/{slug}/…` | 영역 안 버튼 동작(JSON) | 위와 같음 |
| `/api/me/…` | 계정 범위 동작(성경 읽음 · 책갈피) | AccountId 만 |
| `/platform/…`, `/api/platform/…` | 플랫폼 관리자 | 시스템 연결(PlatformScope) |

- 영역 코드가 없거나 정지된 영역이면 404. 로그인했지만 승인된 소속이 없으면 `/{slug}/join`(소속 신청)으로 보낸다(R-130).

### 1-2. 요청 처리 순서 (영역 경로)
1. 쿠키에서 AccountId → 2. 주소에서 TenantId → 3. 소속 · 역할 조회 → 4. 권한 정책 검사(3절 표)
→ 5. DB 연결에 세션 값(TenantId, AccountId) 읽기 전용 설정 → 6. 처리

### 1-3. 응답 형식 (JSON API)
- 성공: `200` + 본문(아래 각 API). 만들기는 `201`.
- 실패: `application/problem+json`
  ```json
  { "type": "about:blank", "title": "이 날짜는 수행할 수 없어요", "status": 422,
    "code": "OutsideGraceWindow", "detail": "오늘/어제/그저께만 가능해요" }
  ```
  - `code` 는 앱이 분기하는 고정 값, `title` · `detail` 은 화면에 그대로 보여 줄 문구(리소스 + 규칙 값으로 만듦 — X-06).
  - 401 로그인 필요, 403 권한 없음, 404 없음(다른 영역 ID 도 404 — R-131), 409 충돌, 422 업무 규칙 위반.
- 날짜는 `yyyy-MM-dd`(영역 시간대 날짜), 시각은 ISO 8601 UTC(`…Z`).
- 상태를 바꾸는 요청은 위조 방지 토큰(Antiforgery)을 확인한다.

### 1-4. 멱등
- 미션 저장은 같은 요청을 다시 보내도 결과가 같다(이미 했으면 `pointsEarned: 0` — R-21, R-31).
- 읽음 표시 · 책갈피는 `PUT`(켬) / `DELETE`(끔). 토글 API 없음(X-17).

## 2. 업무 오류 코드

| code | HTTP | 규칙 | 기본 문구 |
|---|---|---|---|
| `NotApprovedMember` | 403 | R-130 | 아직 승인되지 않았어요 |
| `NotParticipant` | 403 | R-138 | 참여자 역할이 없어요 |
| `FutureDate` | 422 | R-11 | 미래 날짜는 할 수 없어요 |
| `OutsideGraceWindow` | 422 | R-10, R-12 | 이 날짜는 수행할 수 없어요 ({인정 기간 문구}만 가능) |
| `SeasonNotStarted` | 422 | 0028 | 곧 시작해요 ({시작일}) |
| `NoContent` | 422 | R-22, R-122 | 오늘은 {미션}이 없어요 |
| `ItemDisabled` | 422 | 0032 | 사용하지 않는 항목이에요 |
| `NoItemSelected` | 422 | R-23 | 체크를 선택해 주세요 |
| `QuizAlreadyCorrect` | 422 | R-41 | 이미 정답을 맞혔어요 |
| `QuizNoAttemptsLeft` | 422 | R-41, R-44 | 오늘은 {N}번 도전했어요. 내일 새 퀴즈에 도전해요 |
| `ReciteAlreadyDone` | 422 | R-51 | 이번 주 암송은 이미 완료했어요 |
| `TenantSlugNotFound` | 422 | 0026 | 영역 코드를 찾을 수 없어요 (진급 처리) |
| `PromotionPending` | 409 | 0030 | 이미 진행 중인 진급 요청이 있어요 |
| `ResetNotAllowed` | 403 | 0035 | 이 계정은 플랫폼 관리자에게 초기화를 요청해 주세요 |
| `MustChangePassword` | 403 | 0035 | 새 비밀번호로 바꿔 주세요 |

## 3. 권한 표

역할: **참** 참여자 · **보** 보호자 · **교** 교사 · **관** 영역 관리자 · **플** 플랫폼 관리자(대리 접속 포함, 감사 로그).
교사는 1차에서 "조회 전용 관리자"다(01 문서 6절): 현황 · 참여자 상세만 본다.

| 기능 | 참 | 보 | 교 | 관 | 플 |
|---|---|---|---|---|---|
| 미션 홈 · 미션 수행 · 달력 · 내 점수 | ● | | | | |
| 순위 · 가족팀 보기 (스위치 켜짐) | ● | ● | ● | ● | ● |
| 보호자 홈 · 자녀 상세 (연결된 자녀만, R-132) | | ● | | | |
| 관리 홈 · 현황 · 참여자 상세 | | | ● | ● | ● |
| 소속 승인 · 역할 · 정지 · 그룹 라벨 · 진급 처리 | | | | ● | ● |
| 보호 관계 연결 | | | | ● | ● |
| 콘텐츠 덮어쓰기 · 규칙 · 시즌 · 기능 스위치 · 대표 색 · 서비스 이름 | | | | ● | ● |
| 가족팀 관리 · 단계 카드 숨김 | | | | ● | ● |
| 참여자 성경 읽음 초기화 (0029) | | | | ● | ● |
| 비밀번호 임시 초기화 (0035, 다른 영역 관리자 · 플랫폼 관리자 계정 제외) | | | | ● | ● |
| 영역 · 공용 콘텐츠 · 퀴즈 은행 · 성경 · 플랫폼 규칙 · 감사 로그 · 사용량 | | | | | ● |

- 한 사람이 역할을 여러 개 가지면 합집합(0027). 권한은 역할 · 정책으로만 판단한다(X-11).

## 4. 화면 목록 (Razor Pages)

### 4-1. 공통 · 계정
| 주소 | 화면 | 차수 |
|---|---|---|
| `/` | 랜딩 | 1차 |
| `/account/login`, `/account/register`, `/account/logout` | 로그인 · 가입(아이디 · 비밀번호 · 이름만, R-137) | 1차 |
| `/account/password` | 비밀번호 변경. 찾기는 없음 — 영역 관리자가 임시 비밀번호로 초기화(0035). 초기화된 계정은 로그인 직후 이 화면으로 강제 이동 | 1차 |
| `/account` | 내 계정: 이름 · 사진 · 소속 영역 목록 | 1차 |
| `/account/tenants` | 영역 선택 · 전환(R-135) | 1차 |
| `/{slug}/join` | 소속 신청(희망 역할: 참여자 · 보호자) | 1차 |

### 4-2. 참여자
| 주소 | 화면 | ViewModel 주요 값 | 차수 |
|---|---|---|---|
| `/{slug}` | 미션 홈 | 인사, 시즌 점수, 최근 7일 막대 · 합계, 미션 4종 상태(완료 · 없음 · 꺼짐), 다음 단계까지 남은 점수, 한 줄 말씀, 날짜 칩(인정 기간), 다른 날짜 안내(R-75), 시작 전 안내(0028), 미확인 단계 도달 | 1차 |
| `/{slug}/word?date=` | 말씀 | 요약 · 해설 · 질문, 항목 버튼(켜진 것만, 표시 이름 — 0032), 각 항목 완료 여부, 수행 가능 여부 · 사유 | 1차 |
| `/{slug}/bible?date=` | 본문 읽기 | 범위 본문(번역별), 절별 읽음 · 책갈피, 책 소개, 영상 구간 | 1차(번역 대조 스위치) |
| `/{slug}/bible/range?book=&from=&to=` | 범위 읽기(점수 버튼 없음, R-115) | 위와 같음 | 2차 |
| `/{slug}/prayer?date=` | 기도 | 제목 · 시작 · 본문 · 끝, 근거 구절, 항목 버튼 | 1차 |
| `/{slug}/quiz?date=` | 퀴즈 | 문제 · 배경 · 보기, 시도 내역(고른 답), 남은 시도, 정답 · 해설(끝난 경우만) | 1차 |
| `/{slug}/recite?date=` | 암송 | 이번 주 구절(한국어), 공과 제목, 이번 주 완료 여부 | 1차 |
| `/{slug}/calendar?month=` | 달력 | 42칸: 날짜 · 그날 점수(이번 달만) · 완주일 · 오늘 · 수행 가능 | 1차 |
| `/{slug}/points?month=` | 내 점수 | 날짜별 말씀 · 기도 · 퀴즈 · 암송 · 합계 | 1차 |
| `/{slug}/ranking?year=` | 순위 | 카드(R-81), 본인 강조 | 2차 |
| `/{slug}/teams?year=` | 가족팀 | 팀 카드 · 구성원 점수 · 가족 단계 | 2차 |

### 4-2b. 성경통독 참여자 (0039, 0040, 0041 — Bible2 구성) — 1차
| 주소 | 화면 | ViewModel 주요 값 |
|---|---|---|
| `/{slug}/reading` | 대시보드 | 진도 링 · 장 수, 구약 · 신약, 연속 · 읽은 날 · 오늘, 이어 읽기(멈춘 곳 · 첫 절 미리보기), 다음에 읽을 곳, 통독 진도 막대, 최근 8주 달력, 책별 진도(구약 · 신약 탭). 오늘의 말씀은 2차 |
| (목차 메뉴) | 왼쪽 서랍 | 대시보드 · 구약 · 신약 · 검색 · 북마크 · 참여자 현황, 빠른 이동, 성경말씀으로 가기 |
| `/{slug}/reading/list?t=ot\|nt` | 구약 · 신약 읽기 | 전체 진도, 책 버튼, 펼친 책의 장 번호 칸(완료 · 다음 · 안 읽음, 눌러 바로 완료 — R-151), 이어 읽기 |
| `/{slug}/reading/read/{book}/{chapter}` | 본문 읽기 | 경로, 글자 크기, 장 진도(n절 읽음), 장 번호 줄, 읽기 단위 · 번역(모두 포함) · 듣기 · 해설, 절별 ★, 이전 · 다음(R-160) |
| `/{slug}/reading/search?q=` | 성경 검색 | 검색창, 추천 단어, 결과(번역 선택), 영역 인기 검색어(R-163, R-164) |
| `/{slug}/reading/bookmarks` | 북마크 | ★ 말씀 카드, 이 장 읽기 |
| `/{slug}/reading/participants` | 참여자 현황 | 통계(참여자 · 완독자 · 평균 진도 · 누적 완독), 명예의 전당, 참여자 카드, 진도율순 · 최근순 · 이름 찾기(R-159). 공개 꺼짐이면 관리자 · 교사만 |
| `/{slug}/reading/rounds` | 회차 기록 | 회차별 시작 · 완료일, [새 회차 시작] |

- 영역 홈 `/{slug}` 은 켠 프로그램이 둘이면 아래 탭(성경말씀 · 성경통독)으로 오간다. 하나면 바로 그 프로그램.
- 관리자: `/{slug}/admin/reading` — 소속원별 진도율 · 완독 횟수 · 마지막 읽은 날(R-159). 1차.

### 4-3. 보호자
| 주소 | 화면 | 차수 |
|---|---|---|
| `/{slug}/guardian` | 보호자 홈: 연결된 자녀 카드(오늘 미션 상태 · 이번 주 점수 · 연속일) | 2차 |
| `/{slug}/guardian/children/{participantId}` | 자녀 상세: 달력 · 월별 점수(읽기 전용) | 2차 |

### 4-4. 영역 관리자 · 교사
| 주소 | 화면 | 차수 |
|---|---|---|
| `/{slug}/admin` | 관리 홈: 오늘 참여자 수, 최근 7일 추이, 미참여자 | 1차 |
| `/{slug}/admin/members` | 소속 관리: 신청 승인 · 거절, 역할 체크박스, 정지 · 해제, 그룹 라벨, 진급 처리 | 1차 |
| `/{slug}/admin/members/{membershipId}` | 참여자 상세: 달력 · 점수 · 최근 활동, 성경 읽음 초기화 | 1차 |
| `/{slug}/admin/promotions` | 받은 진급 요청 승인 · 거절, 보낸 요청 상태 | 1차 |
| `/{slug}/admin/guardians` | 보호 관계 연결 · 해제 | 2차 |
| `/{slug}/admin/content?month=` | 콘텐츠 달력(종류별 출처: 공용 · 덮어쓰기 · 없음, 빈 날 경고 — R-122) | 1차(보기) |
| `/{slug}/admin/content/{kind}/{date}` | 덮어쓰기 편집 · 미리보기 · 공용으로 되돌리기 | 2차 |
| `/{slug}/admin/settings` | 규칙(항목 사용 · 이름 · 점수, 인정 기간, 퀴즈 횟수, 단계), 기능 스위치, 대표 색, 변경 이력 | 2차(1차는 플랫폼 기본값 — 06 문서) |
| `/{slug}/admin/branding` | 서비스 이름 · 짧은 이름(비우면 기본 "도전!!!성경말씀"), 대표 색 — 0038 | 1차 |
| `/{slug}/manifest.webmanifest` | 영역별 PWA 매니페스트(이름 · 짧은 이름 · 대표 색 · start_url `/{slug}/`) — 0038 | 1차 |
| `/{slug}/admin/season` | 시즌 시작일(빠지는 점수 경고 — 0028), 지난 시즌 보기 | 1차 |
| `/{slug}/admin/teams` | 가족팀 관리 · 단계 카드 숨김 | 2차 |

### 4-5. 플랫폼 관리자
| 주소 | 화면 | 차수 |
|---|---|---|
| `/platform` | 플랫폼 홈 | 1차 |
| `/platform/tenants` | 영역 목록 · 만들기(코드 · 이름 · 시간대 · 대표 색) · 정지 · 영역 관리자 지정 | 1차 |
| `/platform/content/{kind}?month=` | 공용 콘텐츠 달력 · 편집 · 공개 · 월 단위 날짜 생성 | 1차 |
| `/platform/quizbank` | 퀴즈 문제 은행 | 2차 |
| `/platform/bible` | 성경 참조 데이터 적재 | 1차(초기 적재 도구) |
| `/platform/rules` | 플랫폼 기본 규칙 | 1차 |
| `/platform/accounts` | 계정 찾기 · 비밀번호 초기화 | 1차 |
| `/platform/audit`, `/platform/usage` | 감사 로그 · 사용량 | 2차 |
| `/platform/enter/{slug}` | 영역 진입 · 대리 접속(감사 로그) | 2차 |

## 5. JSON API

### 5-1. 미션 (참여자) — 1차

**`POST /api/{slug}/missions/word`** · **`POST /api/{slug}/missions/prayer`**
```json
요청 { "date": "2027-01-05", "items": ["Word.Me", "Word.Parent"] }
응답 { "pointsEarned": 1, "dailyTotal": 3, "seasonTotal": 120,
       "completedItems": ["Word.Me", "Word.Parent"],
       "milestonesReached": [], "message": "+1점! 올해 총 120점" }
```
- `items` 가 비면 `NoItemSelected`. 이미 한 항목은 무시(점수 0). 켠 항목은 끌 수 없다(R-21, R-31).
- 기도 요청의 `items` 는 `Prayer.Me` · `Prayer.Parent` · `Prayer.Friend`.

**`POST /api/{slug}/missions/quiz/attempts`**
```json
요청 { "date": "2027-01-05", "choiceId": 812 }        // 주관식은 { "date": …, "answerText": "모세" }
응답 { "attemptNo": 1, "isCorrect": false, "attemptsLeft": 1,
       "pointsEarned": 0, "seasonTotal": 120,
       "correctChoiceId": null, "explanation": null,     // 끝났을 때(정답 또는 시도 소진)만 값
       "milestonesReached": [], "message": "오답이에요 (1/2) 한 번 더" }
```

**`POST /api/{slug}/missions/recite`**
```json
요청 { "date": "2027-01-05" }
응답 { "pointsEarned": 1, "seasonTotal": 121, "milestonesReached": [], "message": "+1점! 올해 총 121점" }
```

공통: `milestonesReached` 는 이번 저장으로 새로 도달한 단계 `[{ "threshold": 3000, "year": 2027 }]`(R-101).

### 5-2. 미션 홈 보조 — 1차
| 메서드 · 주소 | 용도 |
|---|---|
| `GET /api/{slug}/calendar?month=2027-01` | 달력 42칸(달 이동 시 화면 일부 갱신) |
| `POST /api/{slug}/milestones/{threshold}/snooze` `{ "days": 1 \| 3 \| 7 }` | 단계 알림 숨김(R-103) |

### 5-3. 성경 읽기 (계정 범위) — 1차
| 메서드 · 주소 | 용도 |
|---|---|
| `PUT /api/me/bible/read/{book}/{chapter}/{verse}` | 절 읽음 |
| `DELETE /api/me/bible/read/{book}/{chapter}/{verse}` | 절 읽음 해제 |
| `PUT /api/me/bible/read-range` `{ book, startChapter, startVerse, endChapter, endVerse }` | 범위 전체 읽음 |
| `DELETE /api/me/bible/read-range` (같은 본문) | 범위 전체 해제 |
| `PUT` · `DELETE /api/me/bible/bookmarks/{book}/{chapter}/{verse}` | 책갈피 |

### 5-3b. 성경통독 (계정 범위) — 1차
| 메서드 · 주소 | 용도 |
|---|---|
| `PUT /api/me/reading/chapters/{book}/{chapter}` | 현재 회차에 장 읽음(R-151). 그 장의 절 읽음 표시도 기록(R-152). 응답 `{ progress, roundCompleted, newRoundNo }` |
| `DELETE /api/me/reading/chapters/{book}/{chapter}` | 현재 회차에서 장 읽음 해제 |
| `POST /api/me/reading/rounds` | 새 회차 시작(R-154). 현재 회차는 닫힘 |
| `GET /api/me/reading/summary` | 진도율 · 완독 횟수 · 오늘 읽은 장 수 · 연속 읽은 날 |
| `PUT /api/me/reading/progress/{book}/{chapter}` `{ throughVerse }` | "다음"으로 넘긴 절까지 읽음(R-160). 멱등(이미 더 읽었으면 그대로). 마지막 절이면 장 자동 완료(R-162). 응답 `{ chapterDone, progress, roundCompleted }` |
| `GET /api/{slug}/reading/search?q=&translation=` | 성경 검색(R-163). 검색어를 영역 SearchLog 에 남김(R-164) |
| `GET /api/{slug}/reading/search/popular` | 영역 인기 검색어(최근 30일 상위 10) |

- 1,189번째 장을 읽으면 응답의 `roundCompleted: true` 로 축하 화면을 띄운다(ReadingRoundCompleted).
- 성경통독을 켜지 않은 영역 경로에서 부르면 404(R-150).

### 5-4. 영역 관리 — 1차
| 메서드 · 주소 | 용도 |
|---|---|
| `POST /api/{slug}/admin/members/{id}/approve` `{ roles: [...] }` | 승인 + 역할 |
| `POST /api/{slug}/admin/members/{id}/reject` | 거절 |
| `PUT /api/{slug}/admin/members/{id}/roles` `{ roles: [...] }` | 역할 변경 |
| `POST /api/{slug}/admin/members/{id}/suspend` · `/resume` | 정지 · 해제 |
| `PUT /api/{slug}/admin/members/{id}/group-label` `{ label }` | 그룹 라벨 |
| `GET /api/{slug}/admin/tenants/lookup?code=` | 진급 대상 영역 이름 확인(이름만 — 0026) |
| `POST /api/{slug}/admin/members/{id}/promote` `{ targetSlug }` | 진급 요청 보내기 |
| `POST /api/{slug}/admin/promotions/{id}/approve` · `/reject` | 받은 진급 요청 처리(시스템 연결) |
| `DELETE /api/{slug}/admin/members/{id}/bible-reads` | 성경 읽음 · 책갈피 초기화(시스템 연결, 0029) |
| `POST /api/{slug}/admin/members/{id}/reset-password` | 임시 비밀번호 발급(시스템 연결, 0035) → `{ "temporaryPassword": "…" }`. 대상이 플랫폼 관리자이거나 다른 영역 관리자면 403 `ResetNotAllowed` |
| `GET /api/{slug}/admin/season/{year}/impact?startDate=` | 시작일 변경 시 빠지는 점수가 있는 참여자 수 |
| `PUT /api/{slug}/admin/season/{year}` `{ startDate }` | 시즌 시작일 변경(0028) |
| `PUT /api/{slug}/admin/branding` `{ appName, appShortName, themeColor }` | 서비스 이름 · 짧은 이름 · 대표 색. 빈 값(`null`)이면 기본값으로 되돌림(0038) |
| `PUT /api/platform/tenants/{id}/programs` `{ mission, reading }` | 영역의 프로그램 켜기(플랫폼 관리자, 하나 이상 — R-150) |

### 5-5. 외부 연동 — 2차 (04 문서 4절)
| 메서드 · 주소 | 용도 |
|---|---|
| `GET /api/{slug}/external/points?year=` | 참여자별 시즌 점수(영역 API 키) |
| `GET /api/{slug}/external/participants/{id}/points?year=` | 미션별 내역 |
| 웹훅 발송 | `MilestoneReached`, `FamilyMilestoneReached`, `ActivityRecorded`(아웃박스) |

## 6. 테스트 기준 (8단계 준비)
- 오류 코드 표(2절)의 각 행 = 최소 1개 통합 테스트.
- 권한 표(3절)의 빈칸 = "403 또는 404" 테스트.
- **격리 테스트**: 영역 A 로 로그인해 영역 B 의 ID(참여자 · 소속 · 콘텐츠 덮어쓰기 · 진급 요청)로 모든 API 를 호출 → 404, DB 변경 없음(R-131).
