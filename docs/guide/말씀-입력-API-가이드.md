# 말씀 입력 API 사용 가이드

다른 사이트나 프로그램에서 하루치 **오늘의 말씀**을 넣는 방법입니다. 넣은 말씀은 **모든 부서가 함께 쓰는 공용 말씀**이 됩니다.
칸마다 자세한 설명은 [콘텐츠 입력 API 정의서](콘텐츠-입력-API-정의서.md)에 있습니다.

주소와 본문 칸만 다르고, API 키 · 꼭 지킬 것 · 응답 · 오류 · 호출 코드 모양은 [퀴즈 입력 API 가이드](퀴즈-입력-API-가이드.md)와 같습니다. 키도 같은 키를 씁니다.

## 호출 방법

```
PUT https://{사이트 주소}/api/platform/words/{날짜}
Content-Type: application/json
X-Api-Key: {API 키}
```

- `{날짜}`는 `yyyy-MM-dd` 모양이고 **한 번에 하루치만** 넣습니다.
- 같은 날을 다시 부르면 그날 말씀이 바뀝니다. 이미 그날 미션을 한 사람의 기록과 점수는 바뀌지 않습니다.

## 본문 칸

| 칸 | 형식 | 필수 | 설명 |
|---|---|---|---|
| `title` | 글자 | 예 | 제목. 100자까지 |
| `book` | 숫자 | 예 | 성경 책 번호. 1 ~ 66 |
| `startChapter` | 숫자 | 예 | 시작 장 |
| `startVerse` | 숫자 | 예 | 시작 절 |
| `endChapter` | 숫자 | 예 | 끝 장 |
| `endVerse` | 숫자 | 예 | 끝 절 |
| `summary` | 글자 | 예 | 오늘 이야기. 20,000자까지 |
| `commentary` | 글자 | | 해설. 20,000자까지 |
| `publish` | 참 · 거짓 | | `true`(기본값)면 공개, `false`면 초안 |

- 장이 그 책의 장 수를 넘거나 끝이 시작보다 앞이면 `400` 이고 `errors.range` 로 알려 줍니다.
- 절은 1 ~ 176 만 봅니다. 그 장에 실제로 있는 절인지는 보지 않으니 넣기 전에 확인해 주세요.
- 이미 공개된 날은 초안으로 되돌릴 수 없습니다.

## 예

```json
{
  "title": "믿음의 시작",
  "book": 1,
  "startChapter": 12,
  "startVerse": 1,
  "endChapter": 12,
  "endVerse": 9,
  "summary": "하나님의 말씀을 듣고 아브람이 길을 떠났어요.",
  "commentary": "믿음은 말씀을 듣고 따라가는 것이에요.",
  "publish": true
}
```

```bash
curl -X PUT "https://example.com/api/platform/words/2026-11-05" -H "Content-Type: application/json" -H "X-Api-Key: $QUIZ_API_KEY" --data-binary @word.json
```

성공하면 `200`을 돌려줍니다.

```json
{ "date": "2026-11-05", "id": 123, "status": "Published", "created": true }
```
