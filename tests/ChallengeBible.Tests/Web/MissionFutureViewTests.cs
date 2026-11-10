using System.Net.Http.Json;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Tests.Support;

namespace ChallengeBible.Tests.Web;

/// <summary>오늘 이후 날짜도 달력에서 눌러 미리 본다(보기만, 2026-11-04 사용자). 오늘 = 2027-01-05.</summary>
public partial class MissionFlowTests
{
    private static readonly DateOnly Jan7 = new(2027, 1, 7);

    [Fact]
    public async Task 달력에서_오늘_이후_날짜도_눌러서_미리_볼_수_있고_안내는_보기만_가능해요()
    {
        ContentSeed.Word(_db, Jan7);
        ContentSeed.ChoiceQuiz(_db, Jan7);
        var c = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("href=\"/jamsil-kids/word-mission?date=2027-01-07&amp;month=2027-01\"", home);       // 미래 날짜도 링크
        Assert.Contains("1월 7일 미리 보기 · 보기만 가능", home);
        Assert.DoesNotContain("아직 오지 않은 날", home);

        var future = await c.GetStringAsync("/jamsil-kids/word-mission?date=2027-01-07");
        Assert.Contains("<b>1월 7일 목요일</b> 미션을 미리 보고 있어요. 오늘 이후의 미션은 <b>보기만 할 수 있고</b>, 그날이 되면 할 수 있어요.", future);   // 주황 카드 ①
        Assert.Contains("오늘이 아닌 날을 보고 있어요", future);
        Assert.Contains("class=\"go\" href=\"/jamsil-kids/word-mission\">오늘 날짜로 이동", future);        // 큰 버튼(오른쪽 "오늘로" 링크는 없음)
        Assert.DoesNotContain(">오늘로<", future);
        Assert.DoesNotContain("class=\"hView\"", future);
        Assert.DoesNotContain("네 가지 모두 했어요", future);                                                  // 미래에는 "다 했어요" 칸이 없다
        Assert.Contains("<span class=\"hGo\">수행예정</span>", future);                                       // 미션 카드 문구
        Assert.DoesNotContain("미수행", future);

        var word = await c.GetStringAsync("/jamsil-kids/word?date=2027-01-07");                             // 그날 말씀도 열림
        Assert.Contains("믿음의 종의 기도", word);
        Assert.Contains("오늘 이후의 미션은 <b>보기만 할 수 있고</b>", word);
    }

    [Theory]
    [InlineData("word")]
    [InlineData("quiz")]
    [InlineData("prayer")]
    [InlineData("recite")]
    public async Task 각_미션_화면에도_다른_날_카드가_있고_버튼은_그_미션의_오늘로_간다(string page)
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync($"/jamsil-kids/{page}?date=2027-01-07");
        Assert.Contains("오늘이 아닌 날을 보고 있어요", html);
        Assert.Contains("<b>1월 7일 목요일</b> 미션을 미리 보고 있어요.", html);
        Assert.Contains($"class=\"go\" href=\"/jamsil-kids/{page}\">오늘 날짜로 이동", html);
        Assert.DoesNotContain(">오늘로<", html);
        Assert.DoesNotContain("오늘 이후는 보기만 가능해요.", html);                                            // 카드와 같은 안내를 또 쓰지 않는다

        Assert.DoesNotContain("오늘이 아닌 날을 보고 있어요", await c.GetStringAsync($"/jamsil-kids/{page}"));   // 오늘은 카드 없음
    }

    [Fact]
    public async Task 지난_날은_할_수_있으면_지금도_할_수_있어요_아니면_보기만_할_수_있어요()
    {
        ContentSeed.Word(_db, new DateOnly(2027, 1, 2));
        var c = await Member("cheolsu", Roles.Participant);
        var ok = await c.GetStringAsync("/jamsil-kids/word-mission?date=2027-01-04");                       // 어제 = 인정 기간 안
        Assert.Contains("<b>1월 4일 월요일</b> 미션을 보고 있어요. 이 날의 미션은 <b>지금도 할 수 있어요</b>(오늘 포함 ", ok);
        Assert.Contains("일 안의 날만 할 수 있어요).", ok);
        Assert.Contains("class=\"go\" href=\"/jamsil-kids/word-mission\">오늘 날짜로 이동", ok);

        var old = await c.GetStringAsync("/jamsil-kids/word-mission?date=2027-01-02");                      // 인정 기간 밖
        Assert.Contains("<b>1월 2일 토요일</b> 미션을 보고 있어요. 지난 날의 미션은 <b>보기만 할 수 있어요</b>(오늘 포함 ", old);
        Assert.Contains("<span class=\"hGo\">미수행</span>", old);                                            // 미션 카드 문구
        Assert.DoesNotContain("수행예정", old);

        Assert.DoesNotContain("오늘이 아닌 날을 보고 있어요", await c.GetStringAsync("/jamsil-kids/word-mission"));   // 오늘은 카드 없음
    }

    [Fact]
    public async Task 다음_달로도_넘어가고_미래에는_미션을_할_수_없다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("aria-label=\"다음 달\"", home);                                                      // 이달이 마지막이 아니다
        Assert.Contains("2027년 2월", await c.GetStringAsync("/jamsil-kids/word-mission?month=2027-02"));
        Assert.Contains("2028년 1월", await c.GetStringAsync("/jamsil-kids/word-mission?month=2028-01"));     // 12개월 뒤까지
        Assert.Contains("2028년 1월", await c.GetStringAsync("/jamsil-kids/word-mission?month=2030-05"));     // 그보다 먼 달은 마지막 달로

        ContentSeed.Word(_db, Jan7);
        var token = await PageToken(c, "/jamsil-kids/word?date=2027-01-07");
        var r = await Api(c, token, "/api/jamsil-kids/missions/word", new { date = Jan7, items = new[] { "Word.Me" } });
        var res = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();                         // 미래는 기록 불가(규칙 그대로): 200 + ok=false
        Assert.False(res.GetProperty("ok").GetBoolean());
        Assert.Equal("오늘 이후는 보기만 가능해요.", res.GetProperty("message").GetString());
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.MissionActivities);
    }

    [Fact]
    public async Task 미래_퀴즈는_문제만_보이고_해설로_답이_새지_않는다()
    {
        ContentSeed.ChoiceQuiz(_db, Jan7);
        var c = await Member("cheolsu", Roles.Participant);
        var future = await c.GetStringAsync("/jamsil-kids/quiz?date=2027-01-07");
        Assert.Contains("종은 우물가에서 가장 먼저 무엇을 했나요?", future);                                    // 문제는 미리 볼 수 있고
        Assert.Contains("오늘 이후의 미션은 <b>보기만 할 수 있고</b>", future);
        Assert.DoesNotContain("먼저 하나님께 기도했어요", future);                                              // 해설은 끝난 뒤에만(서버가 안 내보냄)
        Assert.DoesNotContain("정답은 이것", future);
        Assert.Contains("disabled", future);                                                                 // 고르기 · 제출은 막힘
    }
}
