# 콘텐츠 입력 API 정의서

**도전!!! 성경말씀**에 하루치 **오늘의 말씀**과 **성경퀴즈**를 프로그램으로 넣는 API 입니다.
다른 사이트나 프로그램을 만드는 개발자가 이 문서만 보고 연동할 수 있도록 썼습니다.

| 항목 | 내용 |
|---|---|
| 넣을 수 있는 것 | 공용 말씀, 공용 퀴즈 |
| 넣을 수 없는 것 | 기도, 암송, 특정 부서에만 나오는 콘텐츠 |
| 읽기 · 지우기 | 없음. 넣기와 바꾸기만 됩니다 |
| 단위 | 한 번 호출에 하루치 한 건 |
| 방식 | HTTPS, JSON(UTF-8) |

## 1. API 목록

| 기능 | 메서드 | 주소 |
|---|---|---|
| 말씀 넣기 · 바꾸기 | `PUT` | `/api/platform/words/{date}` |
| 퀴즈 넣기 · 바꾸기 | `PUT` | `/api/platform/quizzes/{date}` |
| 말씀 영상 구간 바꾸기 · 지우기 | `PUT` | `/api/platform/words/{date}/clips` — [말씀 영상 구간 API 가이드](말씀-구간-API-가이드.md) |

## 2. 공통 규칙

### 2-1. 요청

```
PUT https://{사이트 주소}/api/platform/words/2026-11-05
Content-Type: application/json
X-Api-Key: {API 키}
```

| 자리 | 이름 | 필수 | 설명 |
|---|---|---|---|
| 주소 | `date` | 예 | 콘텐츠가 나올 날. `yyyy-MM-dd` 모양. 지난 날도 넣을 수 있습니다 |
| 헤더 | `Content-Type` | 예 | `application/json` |
| 헤더 | `X-Api-Key` | 예 | 플랫폼 관리자에게 받은 키. 말씀과 퀴즈에 같은 키를 씁니다 |

- 칸 이름은 이 문서의 표기대로 씁니다. 숫자 칸은 숫자로, 글자 칸은 글자로 보냅니다.
- 이 문서에 없는 칸을 보내면 무시합니다.
- `publish` 에 `null` 을 보내면 `BadJson` 입니다. 기본값을 쓰려면 칸을 빼고 보냅니다.

### 2-2. API 키

- **서버 코드에서만 씁니다.** 웹페이지 자바스크립트나 앱 안에 넣으면 누구나 볼 수 있습니다. 브라우저 → 내 서버 → 이 API 순서로 부릅니다.
- 환경 변수나 비밀 설정에 두고, Git · 채팅 · 로그에 남기지 않습니다.
- 키를 가진 쪽은 이미 공개된 콘텐츠를 바꿀 수 있습니다. 새어 나갔으면 관리자에게 바로 알려 교체합니다.

### 2-3. 같은 날을 다시 넣을 때

| 그날의 지금 상태 | 다시 넣으면 |
|---|---|
| 없음 | 새로 만듭니다. `created: true` |
| 초안 | 그 자리에서 고칩니다. `created: false` |
| 공개, `publish: true` 로 보냄 | 새 개정을 만들어 바꿉니다. `created: false` |
| 공개, `publish: false` 로 보냄 | 거절합니다. `400 Invalid`, `errors.status` |

- 이미 그날 미션을 한 사람의 기록과 점수는 바뀌지 않습니다.
- 같은 내용으로 다시 불러도 화면 결과는 같습니다. 다만 공개된 날은 부를 때마다 개정이 하나 늘어나니, 성공한 날을 반복해서 부르지 않습니다.

### 2-4. 성공 응답

상태 `200`

```json
{ "date": "2026-11-05", "id": 123, "status": "Published", "created": true }
```

| 칸 | 형식 | 설명 |
|---|---|---|
| `date` | 글자 | 넣은 날짜 |
| `id` | 숫자 | 저장된 행의 번호. 공개된 날을 바꾸면 번호도 바뀝니다 |
| `status` | 글자 | `Published`(공개) 또는 `Draft`(초안) |
| `created` | 참 · 거짓 | `true` 면 그날 처음 넣음, `false` 면 있던 것을 바꿈 |

### 2-5. 실패 응답

```json
{
  "code": "Invalid",
  "message": "입력 값을 확인해 주세요.",
  "errors": { "summary": "오늘 이야기를 써 주세요." }
}
```

| 상태 | `code` | 뜻 | 할 일 | 다시 불러도 되나 |
|---|---|---|---|---|
| 400 | `BadDate` | 날짜 모양이 틀림 | `yyyy-MM-dd` 로 고침 | 고친 뒤에 |
| 400 | `BadJson` | JSON 이 아님, 칸의 형식이 틀림, `Content-Type` 이 틀림 | 본문과 헤더를 고침 | 고친 뒤에 |
| 400 | `Invalid` | 칸 값이 틀림. `errors` 에 칸별 안내 | `errors` 를 보고 고침 | 고친 뒤에 |
| 401 | `BadKey` | 키가 없거나 틀림 | 키를 확인 | 고친 뒤에 |
| 404 | 없음(본문 없음) | 주소가 틀렸거나 서버에 키가 설정되지 않음 | 주소를 확인하고 관리자에게 문의 | 고친 뒤에 |
| 503 | `NotReady` | 서버 설정이 빠짐 | 관리자에게 문의 | 설정 뒤에 |
| 500 | | 서버 오류 | 잠시 뒤 다시 부르고, 계속되면 문의 | 예 |

검사 순서는 키 → 날짜 → JSON → 칸 값 → 서버 설정입니다. 네트워크가 끊겨 응답을 못 받았으면 같은 내용으로 다시 불러도 안전합니다.

## 3. 말씀 넣기

`PUT /api/platform/words/{date}`

### 3-1. 본문 칸

| 칸 | 형식 | 필수 | 허용 값 |
|---|---|---|---|
| `title` | 글자 | 예 | 1 ~ 100자 |
| `book` | 숫자 | 예 | 1 ~ 66 |
| `startChapter` | 숫자 | 예 | 1 ~ 150, 그 책의 장 수 이내 |
| `startVerse` | 숫자 | 예 | 1 ~ 176 |
| `endChapter` | 숫자 | 예 | 1 ~ 150, 그 책의 장 수 이내 |
| `endVerse` | 숫자 | 예 | 1 ~ 176 |
| `summary` | 글자 | 예 | 1 ~ 20,000자 |
| `commentary` | 글자 | | 20,000자까지 |
| `publish` | 참 · 거짓 | | 기본값 `true` |

글자 칸은 앞뒤 공백을 지우고 저장합니다.

### 3-2. 칸 자세한 설명

**`title` 제목**
말씀 미션 화면 맨 위 큰 제목입니다. 부서 홈에도 쓰입니다. 한 줄로 읽히게 짧게 씁니다. 예: `믿음의 시작`

**`book` 성경 책 번호**
[5장 책 번호표](#5-성경-책-번호표)의 번호입니다. 1 창세기, 40 마태복음, 66 요한계시록.

**`startChapter` · `startVerse` · `endChapter` · `endVerse` 읽을 범위**
읽을 본문의 시작과 끝입니다. 한 절만 읽으면 시작과 끝을 같게 씁니다. 끝이 시작보다 앞이면 안 됩니다.
화면에는 아래처럼 나옵니다.

| 보낸 값 | 화면 표시 |
|---|---|
| 창세기 12:1 ~ 12:9 (같은 장) | `창세기 12장 1~9절` |
| 요한복음 3:16 ~ 3:16 (한 절) | `요한복음 3:16` |
| 창세기 1:1 ~ 2:3 (여러 장) | `창세기 1:1 ~ 2:3` |

**시작 구절의 주의점:** 성경 읽기 칸에 시작 구절의 개역개정 본문을 먼저 보여 줍니다. 서버는 **그 장에 실제로 있는 절인지 검사하지 않습니다.** 없는 절을 넣으면 저장은 되지만 본문 대신 "읽으러 가기" 버튼만 나옵니다. 절 번호는 보내기 전에 확인합니다.

**`summary` 오늘 이야기**
아이들이 읽는 본문 이야기입니다. 말씀 미션 화면 "01 오늘 이야기"에 나옵니다.
- 줄바꿈(`\n`)은 그대로 보입니다.
- 처음에는 3줄까지 보이고, 넘으면 "이야기 더 보기" 버튼이 나옵니다. 첫 3줄에 핵심을 씁니다.
- 글자 수로 읽는 시간(예: `30초`)을 계산해 옆에 보여 줍니다.
- 글자 그대로 보여 줍니다. HTML 태그나 마크다운 표시는 꾸밈이 되지 않고 글자로 보입니다.

**`commentary` 해설**
지도용 해설입니다. **지금은 참여자 화면에 나오지 않습니다.** 저장되고 플랫폼 관리자의 공용 콘텐츠 화면에서만 볼 수 있습니다. 없으면 칸을 빼도 됩니다.

**`publish` 공개 여부**
`true` 면 그날 바로 참여자에게 보이고, `false` 면 초안이라 보이지 않습니다. 초안을 공개하려면 같은 날을 `publish: true` 로 다시 부릅니다.

### 3-3. `errors` 칸 이름

`title` · `summary` · `commentary` · `book` · `startChapter` · `startVerse` · `endChapter` · `endVerse` · `range`(장이 책의 장 수를 넘거나 끝이 시작보다 앞) · `status`(공개된 날을 초안으로 보냄)

### 3-4. 예

```json
{
  "title": "믿음의 시작",
  "book": 1,
  "startChapter": 12,
  "startVerse": 1,
  "endChapter": 12,
  "endVerse": 9,
  "summary": "하나님은 아브람에게 고향을 떠나라고 말씀하셨어요.\n아브람은 어디로 가는지 몰랐지만 말씀을 믿고 길을 떠났어요.",
  "commentary": "믿음은 말씀을 듣고 따라가는 것임을 나눕니다.",
  "publish": true
}
```

## 4. 퀴즈 넣기

`PUT /api/platform/quizzes/{date}`

### 4-1. 본문 칸

| 칸 | 형식 | 필수 | 허용 값 |
|---|---|---|---|
| `question` | 글자 | 예 | 1 ~ 1,000자 |
| `background` | 글자 | | 4,000자까지 |
| `format` | 글자 | | `Choice`(기본값) 또는 `Text`. 대소문자를 지킵니다 |
| `choices` | 글자 배열 | 객관식 | 2 ~ 4개, 하나에 300자까지, 빈 보기 없음 |
| `correctNumber` | 숫자 | 객관식 | 1 ~ 보기 개수 |
| `acceptedAnswers` | 글자 배열 | 주관식 | 1개 이상, 하나에 200자까지, 빈 답 없음 |
| `explanation` | 글자 | | 4,000자까지 |
| `publish` | 참 · 거짓 | | 기본값 `true` |

### 4-2. 칸 자세한 설명

**`question` 문제**
아이가 답할 질문입니다. 앞에 `Q.` 를 붙이지 않고 질문만 씁니다.

**`background` 배경 설명**
문제 위에 나오는 이야기입니다. 줄바꿈(`\n`) 하나가 문단 하나이고, 빈 줄은 무시합니다.

**`format` 문제 형식**
`Choice` 는 보기를 고르는 객관식, `Text` 는 답을 쓰는 주관식입니다. 빼면 객관식입니다.

**`choices` 보기 (객관식)**
화면에 보낸 순서대로 나옵니다. 주관식이면 무시합니다.

**`correctNumber` 정답 번호 (객관식)**
`choices` 의 정답 위치입니다. **0 이 아니라 1 부터** 셉니다. 첫 보기가 정답이면 `1` 입니다.

**`acceptedAnswers` 맞는 답 (주관식)**
맞다고 인정할 답을 모두 씁니다. 비교할 때 공백과 문장부호를 빼고 영문 대소문자를 무시하므로 `아 담` 과 `아담!` 은 `아담` 과 같습니다. 다른 표현은 따로 넣습니다. 예: `["예수", "예수님"]`

**`explanation` 해설**
정답을 맞혔거나 기회를 모두 쓴 뒤에 보입니다. 그 전에는 서버가 내보내지 않습니다.

**`publish` 공개 여부**
말씀과 같습니다.

### 4-3. `errors` 칸 이름

`question` · `background` · `format` · `choices` · `correctNumber` · `acceptedAnswers` · `explanation` · `status`

### 4-4. 예

객관식

```json
{
  "question": "삼손이 마지막 기도에서 구한 말은 무엇인가요?",
  "background": "눈을 잃은 삼손은 다곤 신전으로 끌려 나왔습니다.",
  "format": "Choice",
  "choices": ["옥에서 건져 주소서", "눈을 보게 하소서", "이번만 강하게 하소서", "원수를 용서하소서"],
  "correctNumber": 3,
  "explanation": "삼손은 이번만 강하게 해 달라고 구했습니다(삿 16:28).",
  "publish": true
}
```

주관식

```json
{
  "question": "하나님이 처음 지으신 사람의 이름은 무엇인가요?",
  "format": "Text",
  "acceptedAnswers": ["아담"],
  "explanation": "창세기 2장에 나옵니다."
}
```

## 5. 성경 책 번호표

| 번호 | 책 (장 수) | 번호 | 책 (장 수) | 번호 | 책 (장 수) |
|---|---|---|---|---|---|
| 1 | 창세기 (50) | 23 | 이사야 (66) | 45 | 로마서 (16) |
| 2 | 출애굽기 (40) | 24 | 예레미야 (52) | 46 | 고린도전서 (16) |
| 3 | 레위기 (27) | 25 | 예레미야애가 (5) | 47 | 고린도후서 (13) |
| 4 | 민수기 (36) | 26 | 에스겔 (48) | 48 | 갈라디아서 (6) |
| 5 | 신명기 (34) | 27 | 다니엘 (12) | 49 | 에베소서 (6) |
| 6 | 여호수아 (24) | 28 | 호세아 (14) | 50 | 빌립보서 (4) |
| 7 | 사사기 (21) | 29 | 요엘 (3) | 51 | 골로새서 (4) |
| 8 | 룻기 (4) | 30 | 아모스 (9) | 52 | 데살로니가전서 (5) |
| 9 | 사무엘상 (31) | 31 | 오바댜 (1) | 53 | 데살로니가후서 (3) |
| 10 | 사무엘하 (24) | 32 | 요나 (4) | 54 | 디모데전서 (6) |
| 11 | 열왕기상 (22) | 33 | 미가 (7) | 55 | 디모데후서 (4) |
| 12 | 열왕기하 (25) | 34 | 나훔 (3) | 56 | 디도서 (3) |
| 13 | 역대상 (29) | 35 | 하박국 (3) | 57 | 빌레몬서 (1) |
| 14 | 역대하 (36) | 36 | 스바냐 (3) | 58 | 히브리서 (13) |
| 15 | 에스라 (10) | 37 | 학개 (2) | 59 | 야고보서 (5) |
| 16 | 느헤미야 (13) | 38 | 스가랴 (14) | 60 | 베드로전서 (5) |
| 17 | 에스더 (10) | 39 | 말라기 (4) | 61 | 베드로후서 (3) |
| 18 | 욥기 (42) | 40 | 마태복음 (28) | 62 | 요한일서 (5) |
| 19 | 시편 (150) | 41 | 마가복음 (16) | 63 | 요한이서 (1) |
| 20 | 잠언 (31) | 42 | 누가복음 (24) | 64 | 요한삼서 (1) |
| 21 | 전도서 (12) | 43 | 요한복음 (21) | 65 | 유다서 (1) |
| 22 | 아가 (8) | 44 | 사도행전 (28) | 66 | 요한계시록 (22) |

## 6. 호출 예

키는 환경 변수 `CONTENT_API_KEY` 에서 읽습니다. `example.com` 은 실제 사이트 주소로 바꿉니다.

**curl** (한글이 깨지지 않도록 본문을 UTF-8 파일로 저장해서 보냅니다)

```bash
curl -X PUT "https://example.com/api/platform/words/2026-11-05" -H "Content-Type: application/json" -H "X-Api-Key: $CONTENT_API_KEY" --data-binary @word.json
```

**Node.js 18 이상**

```js
const res = await fetch("https://example.com/api/platform/words/2026-11-05", {
  method: "PUT",
  headers: { "Content-Type": "application/json", "X-Api-Key": process.env.CONTENT_API_KEY },
  body: JSON.stringify({
    title: "믿음의 시작",
    book: 1, startChapter: 12, startVerse: 1, endChapter: 12, endVerse: 9,
    summary: "아브람은 말씀을 믿고 길을 떠났어요.",
  }),
});
console.log(res.status, await res.text());
```

**Python**

```python
import os, requests

res = requests.put(
    "https://example.com/api/platform/words/2026-11-05",
    headers={"X-Api-Key": os.environ["CONTENT_API_KEY"]},
    json={
        "title": "믿음의 시작",
        "book": 1, "startChapter": 12, "startVerse": 1, "endChapter": 12, "endVerse": 9,
        "summary": "아브람은 말씀을 믿고 길을 떠났어요.",
    },
    timeout=30,
)
print(res.status_code, res.text)
```

**C#**

```csharp
using System.Net.Http.Json;

using var http = new HttpClient { BaseAddress = new Uri("https://example.com") };
http.DefaultRequestHeaders.Add("X-Api-Key", Environment.GetEnvironmentVariable("CONTENT_API_KEY"));

var res = await http.PutAsJsonAsync("/api/platform/words/2026-11-05", new
{
    title = "믿음의 시작",
    book = 1, startChapter = 12, startVerse = 1, endChapter = 12, endVerse = 9,
    summary = "아브람은 말씀을 믿고 길을 떠났어요.",
});
Console.WriteLine($"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
```

## 7. 연동 순서 권장

1. 앞으로 올 날짜 하나를 정해 `publish: false` 로 넣어 봅니다. 초안은 참여자에게 보이지 않습니다.
2. `200` 과 `status: "Draft"` 를 확인합니다.
3. 플랫폼 관리자가 공용 콘텐츠 화면에서 내용을 확인합니다.
4. 같은 날을 `publish: true` 로 다시 불러 공개합니다.
5. 여러 날을 넣을 때는 날짜마다 차례로 부르고, 실패한 날짜만 기록해 두었다가 고쳐서 다시 부릅니다. 한 날이 실패해도 다른 날에는 영향이 없습니다.
6. 말씀과 퀴즈는 따로 부릅니다. 그날 퀴즈는 "이날 말씀에서 한 문제"로 나오니 같은 날의 말씀 내용으로 문제를 만듭니다.
