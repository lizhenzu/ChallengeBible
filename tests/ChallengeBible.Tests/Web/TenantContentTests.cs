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

/// <summary>부서 암송 · 기도 덮어쓰기(확정 시안: 관리-암송기도입력, 0052 · 0053 · R-120 ~ R-124). 오늘 = 2027-01-05 화요일(서울).</summary>
public partial class TenantContentTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly int _me;

    public TenantContentTests(WebApplicationFactory<Program> app)
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
        _me = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜").Id;
        ContentSeed.Prayer(_db, 6, "순종");
        ContentSeed.Verse(_db, 1, "공용 1주 암송");
    }

    private TenantContentService In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == t.Id), null);
        return new TenantContentService(new InMemoryDb(_db, scope), tc, new TenantClock(_time), _time);
    }

    [Fact]
    public async Task 처음에는_그날_공용_기도를_채워_두고_공개하면_그날만_부서_것이_보인다_0053()
    {
        var jan6 = Jan5.AddDays(1);
        var edit = await In(_kids).LoadAsync(ContentKind.Prayer, jan6);
        Assert.Null(edit.Own);
        Assert.Equal(("순종", 6), (edit.Input.TraitName, edit.CommonNo!.Value));
        edit.Input.TraitName = "우리 부서 순종";
        Assert.Empty(await In(_kids).SaveAsync(_me, ContentKind.Prayer, jan6, edit.Input, publish: true));

        using var raw = InMemoryDb.Raw(_db);
        var mine = raw.DailyPrayers.Single(p => p.TenantId == _kids.Id);
        Assert.Equal((jan6, (byte?)null, ContentStatus.Published, true), (mine.ExposeDate!.Value, mine.DayNo, mine.Status, mine.IsCurrent));
        var cal = new ContentCalendarService(new InMemoryDb(_db, Scope(_kids)), Ctx(_kids), new TenantClock(_time));
        var m = await cal.GetAsync(null);
        Assert.Equal((ContentSource.Over, "우리 부서 순종"), (m.Days[5].Items.Single(i => i.Kind == "p").Source, m.Days[5].Items.Single(i => i.Kind == "p").Title));   // 1월 6일
        var elem = await new ContentCalendarService(new InMemoryDb(_db, Scope(_elem)), Ctx(_elem), new TenantClock(_time)).GetAsync(null);
        Assert.Equal(ContentSource.Common, elem.Days[5].Items.Single(i => i.Kind == "p").Source);                                                       // 다른 부서는 공용
    }

    [Fact]
    public async Task 암송은_그_주_일요일부터_토요일까지_초안은_공용이_보이고_공개된_것을_고치면_새_개정_0052_R124()
    {
        var edit = await In(_kids).LoadAsync(ContentKind.Recite, Jan5);          // 1월 3일(일) ~ 9일
        Assert.Equal((new DateOnly(2027, 1, 3), new DateOnly(2027, 1, 9)), (edit.From, edit.To));
        edit.Input.Title = "마음을 다하여";
        Assert.Empty(await In(_kids).SaveAsync(_me, ContentKind.Recite, Jan5, edit.Input, publish: false));
        Assert.Equal(ContentStatus.Draft, (await In(_kids).LoadAsync(ContentKind.Recite, Jan5)).Own);

        var again = (await In(_kids).LoadAsync(ContentKind.Recite, Jan5)).Input;
        Assert.Empty(await In(_kids).SaveAsync(_me, ContentKind.Recite, Jan5, again, publish: true));    // 초안 → 공개(같은 행)
        again.Title = "마음을 다하여 (고침)";
        Assert.Empty(await In(_kids).SaveAsync(_me, ContentKind.Recite, Jan5, again, publish: true));    // 공개 → 새 개정
        using var raw = InMemoryDb.Raw(_db);
        var rows = raw.WeeklyVerses.Where(v => v.TenantId == _kids.Id).OrderBy(v => v.Revision).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal((false, ContentStatus.Archived), (rows[0].IsCurrent, rows[0].Status));
        Assert.Equal((true, 2, rows[0].Id, new DateOnly(2027, 1, 9)), (rows[1].IsCurrent, rows[1].Revision, rows[1].ReplacesId!.Value, rows[1].EndDate!.Value));
    }

    [Fact]
    public async Task 공용으로_되돌리면_보관하고_지난_날짜는_고칠_수_없다()
    {
        var jan6 = Jan5.AddDays(1);                                               // 공용 6번 기도가 있는 날
        var edit = await In(_kids).LoadAsync(ContentKind.Prayer, jan6);
        edit.Input.TraitName = "그날 기도";
        Assert.Empty(await In(_kids).SaveAsync(_me, ContentKind.Prayer, jan6, edit.Input, publish: true));
        Assert.True(await In(_kids).RevertAsync(ContentKind.Prayer, jan6));
        Assert.Null((await In(_kids).LoadAsync(ContentKind.Prayer, jan6)).Own);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(ContentStatus.Archived, raw.DailyPrayers.Single(p => p.TenantId == _kids.Id).Status);

        var past = await In(_kids).LoadAsync(ContentKind.Prayer, Jan5.AddDays(-1));
        Assert.True(past.Past);
        past.Input.TraitName = "어제";
        Assert.True((await In(_kids).SaveAsync(_me, ContentKind.Prayer, Jan5.AddDays(-1), past.Input, publish: true)).ContainsKey("Date"));
        Assert.False((await In(_kids).LoadAsync(ContentKind.Recite, Jan5)).Past);    // 이번 주는 아직 고칠 수 있다
    }

    private static RequestScope Scope(Tenant t) { var s = new RequestScope(); s.SetTenant(t.Id); return s; }
    private TenantContext Ctx(Tenant t) { var tc = new TenantContext(); using var raw = InMemoryDb.Raw(_db); tc.Set(raw.Tenants.Single(x => x.Id == t.Id), null); return tc; }

    // ---- 화면 흐름 ----

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Member(string login, params string[] roles)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value,
            ["DisplayName"] = "김민수", ["Email"] = login + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        int id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Accounts.Single(a => a.Email == login + "@example.com").Id;
        InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, roles);
        return c;
    }

    [Fact]
    public async Task 관리자는_화면에서_기도를_공개하고_교사는_못_들어온다()
    {
        var admin = await Member("minsu", Roles.TenantAdmin);
        var page = await admin.GetStringAsync("/jamsil-kids/admin/content/prayer?date=2027-01-06");
        Assert.Contains("우리 부서 기도 · 1월 6일(수)", page);
        Assert.Contains("공용 6번 기도 &quot;순종&quot; 대신", page);
        Assert.Contains("value=\"순종\"", page);                                                  // 공용 내용을 채워 시작
        var r = await admin.PostAsync("/jamsil-kids/admin/content/prayer?date=2027-01-06&handler=Publish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value,
            ["Input.TraitName"] = "우리 순종", ["Input.Book"] = "20", ["Input.StartChapter"] = "3", ["Input.StartVerse"] = "5",
            ["Input.EndChapter"] = "3", ["Input.EndVerse"] = "6", ["Input.Paragraphs"] = "하나님, {이름}을(를) 지켜 주세요.",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var after = await admin.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("공개했어요", after);
        Assert.Contains("우리 부서 것 · 공개함", after);
        Assert.Contains("공용으로 되돌리기", after);

        var teacher = await Member("eunwoo", Roles.Teacher);
        Assert.StartsWith("/account/denied", (await teacher.GetAsync("/jamsil-kids/admin/content/prayer?date=2027-01-06")).Headers.Location!.PathAndQuery);
    }
}
