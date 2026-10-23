using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>미션 화면 + 버튼 API(API 계약 5-1). 오늘 = 2027-01-05 (서울).</summary>
public partial class MissionFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public MissionFlowTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        ContentSeed.Word(_db, Jan5);
        ContentSeed.ChoiceQuiz(_db, Jan5);
        ContentSeed.Bible(_db);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Member(string email, params string[] roles)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김철수", ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        var id = raw.Accounts.Single(a => a.Email == email + "@example.com").Id;
        var m = new Membership { TenantId = _kids.Id, AccountId = id, Status = MembershipStatus.Approved,
            Roles = roles.Select(r => new MembershipRole { TenantId = _kids.Id, Role = r }).ToList() };
        raw.Memberships.Add(m);
        raw.SaveChanges();
        if (roles.Contains(Roles.Participant)) { raw.Participants.Add(new Participant { TenantId = _kids.Id, MembershipId = m.Id }); raw.SaveChanges(); }
        return c;
    }

    /// <summary>미션 화면의 위조 방지 토큰을 API 헤더로.</summary>
    private static async Task<string> PageToken(HttpClient c, string url) => TokenRx().Match(await c.GetStringAsync(url)).Groups[1].Value;

    private static async Task<HttpResponseMessage> Api(HttpClient c, string token, string path, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        req.Headers.Add("RequestVerificationToken", token);
        return await c.SendAsync(req);
    }

    [Fact]
    public async Task 말씀_화면에서_누르면_점수가_오른다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        Assert.Contains("믿음의 종의 기도", html);
        Assert.Contains("창세기 24장 1~9절", html);

        var r = await Api(c, TokenRx().Match(html).Groups[1].Value, "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = new[] { "Word.Me" } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, j.GetProperty("pointsEarned").GetInt32());
        Assert.Equal(1, j.GetProperty("seasonTotal").GetInt32());
        Assert.Equal("+1점! 올해 총 1점", j.GetProperty("message").GetString());

        Assert.Contains("aria-pressed=\"true\"", await c.GetStringAsync("/jamsil-kids/word"));
    }

    [Fact]
    public async Task R23_아무것도_고르지_않으면_거절()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var t = await PageToken(c, "/jamsil-kids/word");
        var r = await Api(c, t, "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("NoItemSelected", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 퀴즈_정답이면_정답_보기와_해설을_돌려준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        int correct;
        using (var raw = InMemoryDb.Raw(_db)) correct = raw.DailyQuizzes.Single().CorrectChoiceId!.Value;
        var t = await PageToken(c, "/jamsil-kids/quiz");
        var r = await Api(c, t, "/api/jamsil-kids/missions/quiz/attempts", new { date = "2027-01-05", choiceId = correct });
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j.GetProperty("isCorrect").GetBoolean());
        Assert.Equal(correct, j.GetProperty("correctChoiceId").GetInt32());
        Assert.Contains("기도했어요", j.GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task 보호자는_보기만_하고_누를_수_없다()
    {
        var c = await Member("younghee", Roles.Guardian);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        Assert.Contains("보기만 해요", html);
        var r = await Api(c, TokenRx().Match(html).Groups[1].Value, "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = new[] { "Word.Me" } });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task 위조_방지_토큰이_없으면_거절()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var r = await Api(c, "nope", "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = new[] { "Word.Me" } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task 내_점수는_42칸_달력과_날짜별_표()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var t = await PageToken(c, "/jamsil-kids/word");
        await Api(c, t, "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = new[] { "Word.Me", "Word.Parent" } });

        var html = await c.GetStringAsync("/jamsil-kids/points");
        Assert.Equal(42, Regex.Matches(html, "class=\"c ").Count);   // R-70
        Assert.Contains("2027년 1월", html);
        Assert.Contains("aria-label=\"1월 5일 2점\"", html);
        Assert.Equal("/jamsil-kids/points?month=2027-01", (await c.GetAsync("/jamsil-kids/calendar?month=2027-01")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task R110_성경_읽기는_미션의_구절을_단위만큼_나눠_보여준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var p1 = await c.GetStringAsync("/jamsil-kids/read?from=word");   // 기본: 개역개정 · 3절씩
        Assert.Contains("창세기", p1);
        Assert.Contains("24장 1~9절", p1);
        Assert.Contains("KRV 1:24:1", p1);
        Assert.Contains("KRV 1:24:3", p1);
        Assert.DoesNotContain("KRV 1:24:4", p1);
        Assert.DoesNotContain("NIV 1:24:1", p1);
        Assert.Contains("1–3절 / 9", p1);
        var p2 = await c.GetStringAsync("/jamsil-kids/read?from=word&p=2");
        Assert.Contains("KRV 1:24:4", p2);
        Assert.Contains("4–6절 / 9", p2);
        Assert.Contains("말씀 미션으로 돌아가기", p2);
    }

    [Fact]
    public async Task R111_성경_읽기는_번역을_바꾸거나_모두_나란히_보여준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var niv = await c.GetStringAsync("/jamsil-kids/read?from=word&tr=NIV&unit=0");
        Assert.Contains("NIV 1:24:9", niv);          // 전체 = 9절 한 쪽
        Assert.DoesNotContain("KRV 1:24:1", niv);
        var all = await c.GetStringAsync("/jamsil-kids/read?from=word&tr=ALL&unit=1");
        Assert.Contains("KRV 1:24:1", all);
        Assert.Contains("KLB 1:24:1", all);
        Assert.Contains("NIV 1:24:1", all);
    }

    [Fact]
    public async Task R115_구절을_직접_지정해_읽을_수_있고_잘못된_범위는_안내한다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var john = await c.GetStringAsync("/jamsil-kids/read?book=43&sc=3&sv=16");
        Assert.Contains("요한복음", john);
        Assert.Contains("KRV 43:3:16", john);
        var bad = await c.GetStringAsync("/jamsil-kids/read?book=99&sc=1&sv=1");
        Assert.Contains("읽을 구절이 없어요", bad);
    }

    [Fact]
    public async Task 말씀_화면의_읽으러_가기는_성경_읽기로_연결된다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        Assert.Contains("href=\"/jamsil-kids/read?from=word\"", html);
        Assert.Contains("창세기 24장 읽으러 가기", html);
    }

    [Fact]
    public async Task 기도_화면과_암송_화면은_시안의_구성을_따른다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var prayer = await c.GetStringAsync("/jamsil-kids/prayer");
        // 이 픽스처에는 기도 · 암송 콘텐츠가 없다: 머리말 이름과 빈 상태만 본다(구성은 화면 캡처로 비교)
        Assert.Contains("오늘의 성품기도", prayer);
        Assert.Contains("이날은 기도가 없어요", prayer);
        var recite = await c.GetStringAsync("/jamsil-kids/recite");
        Assert.Contains("이번 주는 암송 말씀이 없어요", recite);
        // 선택 항목 이름은 시안 문구(RuleSet 기본값)
        Assert.Equal("나를 위해 기도했어요", global::ChallengeBible.Domain.Rules.RuleSet.Default.Item(global::ChallengeBible.Domain.Rules.MissionItem.PrayerMe).DisplayName);
    }

    [Fact]
    public async Task 말씀_화면은_이야기_접기와_읽는_시간_성경_읽기_줄을_보여준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        Assert.Contains("id=\"story\"", html);
        Assert.Contains("id=\"storyMore\"", html);      // 3줄을 넘길 때만 JS 가 보인다
        Assert.Contains("10초", html);                  // 읽는 시간
        Assert.Contains("읽으러 가기", html);
    }

    [Fact]
    public async Task 퀴즈_화면은_보기를_고른_뒤_답을_제출한다()
    {
        // 확정 시안: 03 정답을 골라요(라디오) → 답 제출하기, 해설은 끝난 뒤에만
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/quiz");
        Assert.Contains("정답을 골라요", html);
        Assert.Contains("type=\"radio\"", html);
        Assert.Contains("답 제출하기", html);
        Assert.Contains("id=\"explain\" hidden", html);
        Assert.Contains("기회 2번", html);
    }

    [Fact]
    public async Task 미션_홈은_남은_미션과_날짜_칩을_보여준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("김철수님, 안녕하세요", html);
        Assert.Contains("말씀 · 퀴즈가 남았어요", html);   // 기도 · 암송은 콘텐츠가 없어서 남은 것에 안 들어감
        // 확정 시안: 날짜 칩 대신 달력에서 날짜를 눌러 그날 미션을 본다(오늘 이전 날짜는 ?date= 링크)
        Assert.Contains("?date=2027-01-04", html);
        Assert.Contains("class=\"mh-grid\"", html);
        Assert.Contains("class=\"mh-m word", html);
        Assert.Contains("올해 총 점수", html);
    }
}
