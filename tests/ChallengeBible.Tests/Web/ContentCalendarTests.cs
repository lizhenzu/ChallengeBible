using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>관리 · 콘텐츠 달력(R-120 ~ R-123, 0052 · 0053). 오늘 = 2027-01-05 화요일(서울). 1차는 보기만.</summary>
public partial class ContentCalendarTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;

    public ContentCalendarTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        // 1월 5일 ~ 2월 28일: 말씀 · 퀴즈는 매일 공용, 기도는 날짜 번호 1 ~ 31 공용, 암송은 1 ~ 53주 공용 → 기본은 빈 칸 없음
        for (var d = Jan5; d <= new DateOnly(2027, 2, 28); d = d.AddDays(1)) { ContentSeed.Word(_db, d, title: $"말씀 {d:M'/'d}"); ContentSeed.ChoiceQuiz(_db, d); }
        for (byte n = 1; n <= 31; n++) ContentSeed.Prayer(_db, n, $"성품 {n}");
        for (byte w = 1; w <= 53; w++) ContentSeed.Verse(_db, w, $"암송 {w}주");
    }

    private ContentCalendarService In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == t.Id), null);
        return new ContentCalendarService(new InMemoryDb(_db, scope), tc, new TenantClock(_time));
    }

    private static ContentItem Item(ContentMonth m, int day, string kind) => m.Days.Single(x => x.Date.Day == day).Items.Single(i => i.Kind == kind);

    [Fact]
    public async Task R120_부서_것이_있으면_그것_없으면_공용_종류마다_따로()
    {
        var me = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜").Id;
        var over = ContentSeed.Word(_db, Jan5.AddDays(1), _kids.Id, "새해 첫 주 — 우리 부서 말씀");
        using (var raw = InMemoryDb.Raw(_db))
        {
            var w = raw.DailyWords.Single(x => x.Id == over.Id);
            (w.CreatedByAccountId, w.CreatedUtc) = (me, new DateTime(2026, 12, 27, 3, 0, 0, DateTimeKind.Utc));
            raw.SaveChanges();
        }
        ContentSeed.Word(_db, Jan5.AddDays(2), _elem.Id, "다른 부서 말씀");
        ContentSeed.TenantPrayer(_db, _kids.Id, Jan5.AddDays(1), "우리 부서 기도");

        var m = await In(_kids).GetAsync(null);
        Assert.Equal(new DateOnly(2027, 1, 1), m.Month);
        var w6 = Item(m, 6, "w");
        Assert.Equal((ContentSource.Over, "새해 첫 주 — 우리 부서 말씀", "이은혜 · 12월 27일"), (w6.Source, w6.Title, w6.By));
        Assert.Equal(ContentSource.Over, Item(m, 6, "p").Source);
        Assert.Equal((ContentSource.Common, "말씀 1/7"), (Item(m, 7, "w").Source, Item(m, 7, "w").Title));   // 다른 부서 것은 안 보인다
        Assert.Equal(ContentSource.Common, Item(m, 6, "q").Source);                                              // 종류마다 따로
    }

    [Fact]
    public async Task 기도는_날짜_번호_암송은_그해_몇째_주로_공용을_찾는다_0052_0053()
    {
        var m = await In(_kids).GetAsync(new DateOnly(2027, 2, 1));
        Assert.Equal("성품 14", Item(m, 14, "p").Title);
        Assert.Equal("암송 1주", (await In(_kids).GetAsync(null)).Days.Single(x => x.Date == Jan5).Items.Single(i => i.Kind == "r").Title);
        Assert.EndsWith(" · 1/3 ~ 1/9",Item(await In(_kids).GetAsync(null), 5, "r").Detail);
    }

    [Fact]
    public async Task R122_R123_빈_날과_초안을_경고한다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.DailyQuizzes.RemoveRange(raw.DailyQuizzes.Where(q => q.ExposeDate == new DateOnly(2027, 1, 9) || q.ExposeDate == new DateOnly(2027, 1, 25)));
            foreach (var w in raw.DailyWords.Where(w => w.ExposeDate >= new DateOnly(2027, 1, 20))) w.Status = ContentStatus.Draft;
            raw.SaveChanges();
        }
        var m = await In(_kids).GetAsync(null);
        Assert.Equal(ContentSource.None, Item(m, 9, "q").Source);
        Assert.True(m.Days.Single(x => x.Date.Day == 9).HasNone);
        Assert.Equal(ContentSource.Draft, Item(m, 20, "w").Source);
        Assert.StartsWith("공용 · 초안(공개 전)", Item(m, 20, "w").Detail);
        Assert.Equal(ContentSource.None, Item(m, 3, "w").Source);                 // 콘텐츠를 넣기 전 날

        var soon = m.Alerts.Single(a => a.Bad);
        Assert.Equal("곧 비어요", soon.Label);
        Assert.Contains("1월 9일(토) 퀴즈", soon.Text);
        Assert.Equal(new DateOnly(2027, 1, 9), soon.Go);
        Assert.Contains(m.Alerts, a => a.Label == "공개 전" && a.Text.StartsWith("1월 20일(수)부터 12칸"));
        Assert.Contains(m.Alerts, a => a.Label == "없음" && a.Text.Contains("1월 25일(월) 퀴즈"));
    }

    [Fact]
    public async Task 달_이동은_지난_12달부터_다음_12달까지()
    {
        Assert.Equal(new DateOnly(2028, 1, 1), (await In(_kids).GetAsync(new DateOnly(2030, 5, 1))).Month);
        Assert.Equal(new DateOnly(2026, 1, 1), (await In(_kids).GetAsync(new DateOnly(2020, 5, 1))).Month);
        Assert.Equal(28, (await In(_kids).GetAsync(new DateOnly(2027, 2, 1))).Days.Count);
    }

    // ---- 화면 흐름 ----

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Member(string email, params string[] roles)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value,
            ["DisplayName"] = "이은혜", ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        int id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Accounts.Single(a => a.Email == email + "@example.com").Id;
        InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, roles);
        return c;
    }

    [Fact]
    public async Task 관리자는_달력을_보고_날짜와_종류를_고르고_관리_홈_카드에서_들어온다()
    {
        using (var raw = InMemoryDb.Raw(_db)) { raw.DailyQuizzes.RemoveRange(raw.DailyQuizzes.Where(q => q.ExposeDate == new DateOnly(2027, 1, 7))); raw.SaveChanges(); }
        var admin = await Member("eunhye", Roles.TenantAdmin);
        var page = await admin.GetStringAsync("/jamsil-kids/admin/content");
        Assert.Contains("href=\"/jamsil-kids/admin/content\"", page);                   // 관리 메뉴 "콘텐츠"
        Assert.Contains("2027년 1월", page);
        Assert.Contains("곧 비어요", page);
        Assert.Matches(@"1월 5일 \(화\)\s*<small>오늘</small>", page);                      // 고르지 않으면 오늘
        Assert.Contains("우리 부서 것으로 바꾸기 (2차)", page);

        var day = await admin.GetStringAsync("/jamsil-kids/admin/content?month=2027-01&date=2027-01-07&kind=q");
        Assert.Contains("1월 7일 (목)", day);
        Assert.Contains("콘텐츠가 없어요", day);
        Assert.Contains("class=\"grid one\"", day);

        var home = await admin.GetStringAsync("/jamsil-kids/admin");
        Assert.Matches("href=\"/jamsil-kids/admin/content\\?month=2027-01&(amp;)?date=2027-01-07#dT\"", home);   // 비어 있는 콘텐츠 카드 → 그 날
    }

    [Fact]
    public async Task 교사는_콘텐츠_달력을_못_본다()
    {
        var teacher = await Member("minsu", Roles.Teacher);
        var r = await teacher.GetAsync("/jamsil-kids/admin/content");
        Assert.StartsWith("/account/denied", r.Headers.Location!.PathAndQuery);
    }
}
