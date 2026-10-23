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
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Member(string loginId, params string[] roles)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김철수", ["LoginId"] = loginId, ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        var id = raw.Accounts.Single(a => a.LoginId == loginId).Id;
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
    public async Task 미션_홈은_남은_미션과_날짜_칩을_보여준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("김철수님, 안녕하세요", html);
        Assert.Contains("말씀 · 퀴즈가 남았어요", html);   // 기도 · 암송은 콘텐츠가 없어서 남은 것에 안 들어감
        Assert.Contains(">어제<", html);
        Assert.Contains(">1/3<", html);   // 서버 문화권과 상관없이 "/"
        Assert.Contains("다음 축하까지", html);
    }
}
