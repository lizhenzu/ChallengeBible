using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>부서 홈 메인(확정 시안: 홈메인, 사용자 확정 8가지). 오늘 = 2027-01-05 (서울), 말씀 · 퀴즈만 있는 날.</summary>
public partial class HomeMainTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public HomeMainTests(WebApplicationFactory<Program> app)
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

    private async Task<(HttpClient C, int AccountId)> Member(string email, params string[] roles)
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
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, roles);
        if (roles.Contains(Roles.Participant)) { raw.Participants.Add(new Participant { TenantId = _kids.Id, MembershipId = m.Id }); raw.SaveChanges(); }
        return (c, id);
    }

    [Fact]
    public async Task 참여자_홈은_말씀띠_미션카드_다음할일_계정줄을_보인다()
    {
        ContentSeed.Prayer(_db, Jan5);
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("김철수님, 안녕하세요", home);
        Assert.Contains("<small>오늘의 기도</small><b>신뢰 · 잠언 3장 5~6절</b>", home);   // 성품 + 본문 범위
        Assert.Contains("창세기 24:1", home);                                       // 오늘의 말씀 띠: 첫 절
        Assert.Contains("0/3<small>오늘 미션</small>", home);                        // 오늘 있는 미션은 말씀 · 퀴즈 · 기도 세 가지
        Assert.Contains("<a class=\"go\" href=\"/jamsil-kids/word\">말씀 읽으러 가기", home);   // 아래 버튼 = 다음 할 일(말씀)
        Assert.Contains("<a class=\"cover\" href=\"/jamsil-kids/word-mission\"", home);     // 카드 아무 데나 = 미션 홈
        Assert.DoesNotContain("<a class=\"p m\"", home);                              // 링크 안에 링크 없음(카드는 div)
        Assert.Contains("점 올해 점수", home);
        Assert.Contains("href=\"/account\"", home);                                  // 확정 7: 계정 줄 3개(비밀번호 바꾸기는 내 계정 안으로)
        Assert.Contains(">부서 바꾸기</a>", home);
        Assert.Contains("로그아웃</button>", home);
        Assert.DoesNotContain("▼", home);                                            // 확정 6: 소속이 하나면 ▼ 없음
        Assert.DoesNotContain(">관리</a>", home);                                    // 확정 1: 관리 버튼은 관리자만
    }

    [Fact]
    public async Task 통독을_시작하지_않았으면_홈은_회차를_열지_않는다()
    {
        var (c, me) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("통독 시작하기", home);
        Assert.Contains("href=\"/jamsil-kids/reading\"", home);
        using var raw = InMemoryDb.Raw(_db);
        Assert.False(raw.ReadingRounds.Any(r => r.AccountId == me));               // R-153: 회차는 통독 화면에서 처음 열린다
    }

    [Fact]
    public async Task 통독을_시작했으면_진도와_이어읽기가_보인다()
    {
        var (c, me) = await Member("cheolsu", Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var at = new DateTime(2027, 1, 4, 1, 0, 0, DateTimeKind.Utc);
            raw.ReadingRounds.Add(new ReadingRound { AccountId = me, RoundNo = 1, StartedUtc = at });
            for (short ch = 1; ch <= 3; ch++) raw.ChapterReads.Add(new ChapterRead { AccountId = me, RoundNo = 1, BookId = 1, Chapter = ch, ReadUtc = at.AddMinutes(ch) });
            raw.SaveChanges();
        }
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("1독째", home);
        Assert.Contains("3 / 1,189장", home);
        Assert.Contains("이어 읽기 · 창세기 4장", home);                              // 이어 읽기 = 창세기 4장
        Assert.Contains("class=\"p r\" href=\"/jamsil-kids/reading\"", home);       // 카드를 누르면 통독 홈
        Assert.DoesNotContain("/jamsil-kids/reading/read/", home);
        Assert.Contains("장 읽은 성경", home);
        Assert.Contains("통독<br />장 수", home);                                    // 이번 주 나의 기록에 통독 줄
    }

    [Fact]
    public async Task 참여자가_아니면_보기만_하고_점수칩이_없다()
    {
        var (c, _) = await Member("eunhye", Roles.Teacher);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("보기만 해요", home);                                        // 확정 4
        Assert.Contains("<a class=\"cover\" href=\"/jamsil-kids/word-mission\"", home);
        Assert.Contains("<a class=\"go\" href=\"/jamsil-kids/word-mission\">미션 홈 가기", home);   // 참여자가 아니면 버튼도 미션 홈
        Assert.DoesNotContain("점 올해 점수", home);
        Assert.DoesNotContain("말씀<br />점수", home);
    }

    [Fact]
    public async Task 관리자는_머리말에_관리_링크가_있다()
    {
        var (c, _) = await Member("eunhye", Roles.Teacher, Roles.TenantAdmin);
        Assert.Contains("class=\"adm\" href=\"/jamsil-kids/admin\">관리</a>", await c.GetStringAsync("/jamsil-kids"));
    }

    [Fact]
    public async Task 말씀이_없는_날은_띠를_숨기고_카드에_알린다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.DailyWords.RemoveRange(raw.DailyWords);
            raw.DailyQuizzes.RemoveRange(raw.DailyQuizzes);
            raw.SaveChanges();
        }
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("aria-label=\"오늘의 말씀\"", home);                   // 확정 8
        Assert.Contains("오늘은 말씀이 없어요", home);
    }

    [Fact]
    public async Task 꺼진_프로그램_카드는_보이지_않는다()
    {
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(t => t.Id == _kids.Id).MissionEnabled = false; raw.SaveChanges(); }
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("class=\"nm\">도전!!!성경말씀", home);                  // 확정 5
        Assert.Contains("class=\"nm\">도전!!!성경통독", home);
    }

    [Fact]
    public async Task 소속이_둘이면_부서_화살표가_부서_고르기로_간다()
    {
        var (c, me) = await Member("cheolsu", Roles.Participant);
        var other = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        InMemoryDb.SeedMembership(_db, other.Id, me, MembershipStatus.Approved, Roles.Participant);
        Assert.Contains("class=\"tenant\" href=\"/account/tenants\"", await c.GetStringAsync("/jamsil-kids"));   // 확정 6
    }

    [Fact]
    public async Task 우리_부서에_오늘_참여_순서가_보인다()
    {
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/jamsil-kids/missions/word")
            { Content = System.Net.Http.Json.JsonContent.Create(new { date = "2027-01-05", items = new[] { "Word.Me" } }) };
        req.Headers.Add("RequestVerificationToken", TokenRx().Match(html).Groups[1].Value);
        (await c.SendAsync(req)).EnsureSuccessStatusCode();
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("나는 오늘 1번째로 참여", home);
        Assert.Contains("1/2<small>오늘 미션</small>", home);
        Assert.Contains("href=\"/jamsil-kids/quiz\"", home);                         // 다음 할 일 = 퀴즈
    }
}
