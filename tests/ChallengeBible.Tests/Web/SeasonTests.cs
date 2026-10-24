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

/// <summary>부서 시즌(0028, R-62, 확정 시안: 관리-시즌). 오늘 = 2027-01-05 화요일(서울).</summary>
public partial class SeasonTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly int _p1, _p2, _other;

    public SeasonTests(WebApplicationFactory<Program> app)
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
        using var raw = InMemoryDb.Raw(_db);
        Participant P(int tenantId) { var p = new Participant { TenantId = tenantId, MembershipId = 0 }; raw.Participants.Add(p); return p; }
        var p1 = P(_kids.Id); var p2 = P(_kids.Id); var other = P(_elem.Id);
        raw.SaveChanges();
        (_p1, _p2, _other) = (p1.Id, p2.Id, other.Id);
        void Pt(int tenantId, int pid, int y, int m, int d, int points) => raw.PointLedgers.Add(new PointLedger
            { TenantId = tenantId, ParticipantId = pid, MissionDate = new DateOnly(y, m, d), MissionItem = "Word.Me", Points = points, RecordedUtc = DateTime.UtcNow });
        Pt(_kids.Id, _p1, 2027, 1, 1, 3); Pt(_kids.Id, _p1, 2027, 1, 4, 2);
        Pt(_kids.Id, _p2, 2027, 1, 3, 4);
        Pt(_kids.Id, _p2, 2026, 12, 30, 7);                 // 지난 시즌
        Pt(_elem.Id, _other, 2027, 1, 2, 9);                // 다른 부서
        raw.SaveChanges();
    }

    private SeasonService In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == t.Id), null);
        return new SeasonService(new InMemoryDb(_db, scope), tc, new TenantClock(_time), _time);
    }

    private int Actor() => InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜").Id;

    [Fact]
    public async Task R62_올해_시즌은_기본_1월_1일부터_이_부서_점수만_센다()
    {
        var v = await In(_kids).GetAsync();
        Assert.Equal(new DateOnly(2027, 1, 1), v.Current.StartDate);
        Assert.True(v.CurrentIsDefault);
        Assert.Equal((9, 2, 2), (v.SeasonPoints, v.Scorers, v.ParticipantCount));
        var past = Assert.Single(v.Past);
        Assert.Equal((2026, 1, 7), (past.Year, past.Scorers, past.Points));
    }

    [Fact]
    public async Task 시작일을_늦추면_빠지는_점수와_참여자를_미리_보여_준다_0028()
    {
        var im = (await In(_kids).ImpactAsync(new DateOnly(2027, 1, 4)))!;
        Assert.True(im.Later);
        Assert.False(im.Future);
        Assert.Equal((new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 3)), (im.From, im.To));
        Assert.Equal((2, 7), (im.Participants, im.Points));         // 1/1 의 3점 + 1/3 의 4점

        var future = (await In(_kids).ImpactAsync(new DateOnly(2027, 3, 7)))!;
        Assert.True(future.Future);
        Assert.Equal((2, 9), (future.Participants, future.Points));   // 지금까지 모은 점수 모두(오늘까지)
        Assert.Null(await In(_kids).ImpactAsync(new DateOnly(2028, 1, 1)));
    }

    [Fact]
    public async Task 시작일을_바꾸면_시즌_점수에_바로_반영되고_기록이_남는다()
    {
        var me = Actor();
        var (changed, error) = await In(_kids).SaveAsync(me, new DateOnly(2027, 1, 3));
        Assert.True(changed);
        Assert.Null(error);
        var v = await In(_kids).GetAsync();
        Assert.Equal(new DateOnly(2027, 1, 3), v.Current.StartDate);
        Assert.Equal(6, v.SeasonPoints);                              // 원장은 그대로, 1/1 의 3점만 빠진다
        Assert.Equal("2027 시즌 시작일 1월 1일 → 1월 3일", Assert.Single(v.Log).Detail);

        await In(_kids).SaveAsync(me, new DateOnly(2027, 1, 1));       // 다시 앞당기면 돌아온다
        Assert.Equal(9, (await In(_kids).GetAsync()).SeasonPoints);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(5, raw.PointLedgers.Count());
    }

    [Fact]
    public async Task 다음_시즌은_미리_정하고_그_밖의_해는_못_고른다()
    {
        var me = Actor();
        Assert.True((await In(_kids).SaveAsync(me, new DateOnly(2028, 1, 2))).Changed);
        var v = await In(_kids).GetAsync();
        Assert.Equal(new DateOnly(2028, 1, 2), v.Next.StartDate);
        Assert.False(v.NextIsDefault);
        Assert.NotNull((await In(_kids).SaveAsync(me, new DateOnly(2029, 1, 1))).Error);
        Assert.NotNull((await In(_kids).SaveAsync(me, new DateOnly(2026, 6, 1))).Error);
        Assert.Empty((await In(_elem).GetAsync()).Log);               // 기록은 그 부서 것만
    }

    [Fact]
    public async Task 기본값을_그대로_정하면_정함_기록_같은_값을_다시_고르면_아무것도_안_한다()
    {
        var me = Actor();
        Assert.True((await In(_kids).SaveAsync(me, new DateOnly(2027, 1, 1))).Changed);
        Assert.Equal("2027 시즌 시작일 1월 1일로 정함(기본값)", Assert.Single((await In(_kids).GetAsync()).Log).Detail);
        Assert.False((await In(_kids).SaveAsync(me, new DateOnly(2027, 1, 1))).Changed);
    }

    [Fact]
    public void 빠른_고르기는_1월_1일과_첫_주일()
    {
        Assert.Equal((new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 3)), SeasonService.QuickDates(2027));
        Assert.Equal((new DateOnly(2028, 1, 1), new DateOnly(2028, 1, 2)), SeasonService.QuickDates(2028));
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
    public async Task 관리자는_빠지는_점수를_보고_확인해야_시작일을_바꾼다()
    {
        var admin = await Member("eunhye", Roles.TenantAdmin);
        var page = await admin.GetStringAsync("/jamsil-kids/admin/season");
        Assert.Contains("href=\"/jamsil-kids/admin/season\"", page);                     // 관리 메뉴 "시즌"
        Assert.Contains("2027 시즌 · 진행 중", page);
        Assert.Contains("지금 시작일이에요 (1월 1일)", page);
        Assert.Contains("1월 3일(첫 주일)", page);

        var impact = await admin.GetStringAsync("/jamsil-kids/admin/season?D=2027-01-04");
        Assert.Contains("1월 4일로 늦추면 이렇게 돼요", impact);
        Assert.Contains("7점", impact);
        var token = TokenRx().Match(impact).Groups[1].Value;
        async Task<HttpResponseMessage> Post(string url, Dictionary<string, string> form)
        {
            form["__RequestVerificationToken"] = token;
            return await admin.PostAsync(url, new FormUrlEncodedContent(form));
        }

        var no = await Post("/jamsil-kids/admin/season", new() { ["start"] = "2027-01-04" });   // 확인 체크 없음
        Assert.Equal(HttpStatusCode.OK, no.StatusCode);
        Assert.Contains("확인했어요", await no.Content.ReadAsStringAsync());
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.TenantSeasons);

        var yes = await Post("/jamsil-kids/admin/season", new() { ["start"] = "2027-01-04", ["agree"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, yes.StatusCode);
        var after = await admin.GetStringAsync("/jamsil-kids/admin/season?Ok=save");
        Assert.Contains("시작일을 1월 4일로 바꿨어요", after);
        Assert.Contains("2027 시즌 시작일 1월 1일 → 1월 4일", after);

        var next = await Post("/jamsil-kids/admin/season?handler=Next", new() { ["start"] = "2028-01-02" });
        Assert.Equal(HttpStatusCode.Redirect, next.StatusCode);
        Assert.Contains("(미리 정해 둠)", await admin.GetStringAsync("/jamsil-kids/admin/season"));
    }

    [Fact]
    public async Task 교사는_시즌_화면을_못_본다()
    {
        var teacher = await Member("minsu", Roles.Teacher);
        var r = await teacher.GetAsync("/jamsil-kids/admin/season");
        Assert.StartsWith("/account/denied", r.Headers.Location!.PathAndQuery);
    }
}
