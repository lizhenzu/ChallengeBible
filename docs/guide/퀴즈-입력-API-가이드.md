# 퀴즈 입력 API 사용 가이드

다른 사이트나 프로그램에서 **도전!!! 성경말씀**에 하루치 성경퀴즈를 넣는 방법입니다.
넣은 퀴즈는 **모든 부서가 함께 쓰는 공용 퀴즈**가 됩니다.

## 1. 시작 전에 받을 것

| 항목 | 설명 |
|---|---|
| 사이트 주소 | 예: `https://example.com`. `https://` 만 씁니다 |
| API 키 | 플랫폼 관리자에게 받습니다. 32자 이상의 글자입니다 |

## 2. 꼭 지킬 것

- **API 키는 서버 코드에서만 씁니다.** 웹페이지의 자바스크립트나 앱 안에 넣으면 누구나 키를 볼 수 있습니다. 브라우저에서 바로 부르면 다른 사이트 호출 제한(CORS)에 막히기도 합니다. 브라우저 → 내 서버 → 이 API 순서로 부르세요.
- **키를 Git · 채팅 · 로그에 남기지 않습니다.** 환경 변수나 비밀 설정에 둡니다. 키가 새어 나갔으면 관리자에게 바로 알려 교체합니다.
- 키를 가진 쪽은 공개된 퀴즈를 바꿀 수 있습니다. 필요한 곳에만 나눠 줍니다.

## 3. 호출 방법

```
PUT https://{사이트 주소}/api/platform/quizzes/{날짜}
Content-Type: application/json
X-Api-Key: {API 키}
```

- `{날짜}`는 퀴즈가 나올 날이고 `yyyy-MM-dd` 모양입니다. 예: `2026-11-05`
- **한 번에 하루치만** 넣습니다. 여러 날은 날짜마다 한 번씩 부릅니다.
- 같은 날을 다시 부르면 그날 퀴즈가 바뀝니다. 같은 내용으로 다시 불러도 문제없으니, 실패하면 그대로 다시 부르면 됩니다.
- 이미 그날 퀴즈를 푼 사람의 기록과 점수는 바뀌지 않습니다.

### 본문 칸

| 칸 | 형식 | 필수 | 설명 |
|---|---|---|---|
| `question` | 글자 | 예 | 문제. 1,000자까지 |
| `background` | 글자 | | 배경 설명. 4,000자까지. 줄바꿈(`\n`) 하나가 문단 하나 |
| `format` | 글자 | | `Choice`(객관식, 기본값) 또는 `Text`(주관식). 대소문자를 지켜 씁니다 |
| `choices` | 글자 배열 | 객관식 | 보기 2 ~ 4개. 하나에 300자까지 |
| `correctNumber` | 숫자 | 객관식 | 정답 보기 번호. **1부터** 셉니다 |
| `acceptedAnswers` | 글자 배열 | 주관식 | 맞는 답 하나 이상. 하나에 200자까지 |
| `explanation` | 글자 | | 해설. 4,000자까지 |
| `publish` | 참 · 거짓 | | `true`(기본값)면 공개, `false`면 초안 |

초안은 참여자에게 보이지 않습니다. 이미 공개된 날은 초안으로 되돌릴 수 없습니다.

### 객관식 예

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

### 주관식 예

```json
{
  "question": "하나님이 처음 지으신 사람의 이름은 무엇인가요?",
  "format": "Text",
  "acceptedAnswers": ["아담"],
  "explanation": "창세기 2장에 나옵니다."
}
```

## 4. 응답

성공하면 `200`을 돌려줍니다.

```json
{ "date": "2026-11-05", "id": 123, "status": "Published", "created": true }
```

| 칸 | 설명 |
|---|---|
| `status` | `Published`(공개) 또는 `Draft`(초안) |
| `created` | `true`면 그날 퀴즈를 처음 넣음, `false`면 있던 퀴즈를 바꿈 |

실패하면 아래처럼 돌려줍니다.

```json
{
  "code": "Invalid",
  "message": "입력 값을 확인해 주세요.",
  "errors": { "correctNumber": "정답 보기 번호(1부터)를 써 주세요." }
}
```

| 상태 | `code` | 뜻 | 할 일 |
|---|---|---|---|
| 400 | `BadDate` | 날짜 모양이 틀림 | `yyyy-MM-dd` 로 고침 |
| 400 | `BadJson` | 본문이 JSON 이 아니거나 칸의 형식이 틀림 | 본문과 `Content-Type` 을 확인 |
| 400 | `Invalid` | 칸 값이 틀림 | `errors` 를 보고 고침 |
| 401 | `BadKey` | 키가 없거나 틀림 | 키를 확인 |
| 404 | | 서버에 키가 설정되지 않았거나 주소가 틀림 | 주소를 확인하고 관리자에게 문의 |
| 503 | `NotReady` | 서버 설정이 빠짐 | 관리자에게 문의 |

`400` · `401` · `404` 는 다시 불러도 같은 결과입니다. 고친 뒤에 다시 부르세요.
`503` 과 네트워크 오류는 잠시 뒤에 다시 불러도 안전합니다.

## 5. 호출 예

아래 예는 모두 키를 환경 변수 `QUIZ_API_KEY` 에서 읽습니다.

### curl

한글이 깨지지 않도록 본문을 UTF-8 파일(`quiz.json`)로 저장해서 보냅니다.

```bash
curl -X PUT "https://example.com/api/platform/quizzes/2026-11-05" -H "Content-Type: application/json" -H "X-Api-Key: $QUIZ_API_KEY" --data-binary @quiz.json
```

### C#

```csharp
using System.Net.Http.Json;

using var http = new HttpClient { BaseAddress = new Uri("https://example.com") };
http.DefaultRequestHeaders.Add("X-Api-Key", Environment.GetEnvironmentVariable("QUIZ_API_KEY"));

var res = await http.PutAsJsonAsync("/api/platform/quizzes/2026-11-05", new
{
    question = "삼손이 마지막 기도에서 구한 말은 무엇인가요?",
    choices = new[] { "옥에서 건져 주소서", "눈을 보게 하소서", "이번만 강하게 하소서", "원수를 용서하소서" },
    correctNumber = 3,
});
Console.WriteLine($"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
```

### Node.js (18 이상)

```js
const res = await fetch("https://example.com/api/platform/quizzes/2026-11-05", {
  method: "PUT",
  headers: { "Content-Type": "application/json", "X-Api-Key": process.env.QUIZ_API_KEY },
  body: JSON.stringify({
    question: "삼손이 마지막 기도에서 구한 말은 무엇인가요?",
    choices: ["옥에서 건져 주소서", "눈을 보게 하소서", "이번만 강하게 하소서", "원수를 용서하소서"],
    correctNumber: 3,
  }),
});
console.log(res.status, await res.text());
```

### Python

```python
import os, requests

res = requests.put(
    "https://example.com/api/platform/quizzes/2026-11-05",
    headers={"X-Api-Key": os.environ["QUIZ_API_KEY"]},
    json={
        "question": "삼손이 마지막 기도에서 구한 말은 무엇인가요?",
        "choices": ["옥에서 건져 주소서", "눈을 보게 하소서", "이번만 강하게 하소서", "원수를 용서하소서"],
        "correctNumber": 3,
    },
    timeout=30,
)
print(res.status_code, res.text)
```

## 6. 여러 날 넣기

날짜마다 한 번씩 차례로 부르고, 실패한 날짜만 기록해 두었다가 고쳐서 다시 부릅니다. 한 날이 실패해도 다른 날에는 영향이 없습니다.

## 7. 잘 들어갔는지 확인

- 응답의 `status` 와 `created` 를 봅니다.
- 플랫폼 관리자는 **플랫폼 › 공용 콘텐츠 › 퀴즈**의 그 날짜에서 내용을 볼 수 있습니다.
- 공개한 퀴즈는 그날 참여자의 퀴즈 미션에 나옵니다.

## 8. 넣을 수 없는 것

- 말씀 · 기도 · 암송
- 특정 부서에만 나오는 퀴즈
- 퀴즈 읽기 · 지우기
