using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 오늘 할 일(ADR 0100, 확정 시안 오늘할일, R-309 ~ R-312): 로그인 직후 할 일이 있으면 할 일 화면으로(안 보기 기간 제외), 선물은 선물 그룹(관리자 · 부장 부감 서기 · 지도 · 선물 담당)
/// 누구나 사고(구매자 표시) 누구나 전하고 [전달 완료]면 사라진다. 누가 보나 = 부서 규칙 → 공통 규칙 → 기본값. 오늘 = 2027-01-05(화, 서울).
/// </summary>
public partial class TodoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly int _teacherMembership;

    public TodoTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var kid = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "dongseok@example.com", "권동석").UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호").UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "boo@example.com", "박부장").UserName!, MembershipStatus.Approved, Roles.Teacher);
        InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "teacher@example.com", "김선생").UserName!, MembershipStatus.Approved, Roles.Teacher);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        var booId = raw.Users.Single(u => u.Email == "boo@example.com").Id;
        raw.DeptRoles.Single(d => d.UserId == booId).RoleCode = "T1005";   // 부장(임원)
        var tId = raw.Users.Single(u => u.Email == "teacher@example.com").Id;
        _teacherMembership = raw.DeptRoles.Single(d => d.UserId == tId).Id;
        var item = new GiftItem { Name = "레고 블록", Price = 15000, CoupangUrl = "https://www.coupang.com/vp/products/1", Status = GiftItemStatus.Active };
        raw.GiftItems.Add(item);
        raw.SaveChanges();
        raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Personal, Year = 2027, UserName = kid, SourceKey = "P1", MaxAmount = 20000, GiftItemId = item.Id,
            ItemName = "레고 블록", ItemPrice = 15000, State = GiftStates.Picked, PickedUtc = new DateTime(2026, 12, 30, 1, 0, 0, DateTimeKind.Utc), CreatedUtc = new DateTime(2026, 12, 30) });
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string handler, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/kids/todo")).Groups[1].Value;
        return await c.PostAsync($"/kids/todo?handler={handler}", new FormUrlEncodedContent(form));
    }

    private long GiftId() { using var raw = InMemoryDb.Raw(_db); return raw.Gifts.Single().Id; }

    [Fact]
    public async Task R309_로그인_직후_할_일이_있으면_한_번_할_일_화면으로_안_보기를_고르면_그동안은_홈으로()
    {
        var boo = await Login("boo@example.com");
        Assert.Equal("/kids/todo", (await boo.GetAsync("/kids")).Headers.Location!.OriginalString);   // 로그인 직후 한 번
        Assert.Equal(HttpStatusCode.OK, (await boo.GetAsync("/kids")).StatusCode);                      // 두 번째부터는 홈
        var page = await boo.GetStringAsync("/kids/todo");
        Assert.Contains("⌂ 홈으로", page);
        Assert.Contains("오늘 안 보기", page);
        Assert.Contains("한 달", page);

        Assert.Equal("/kids", (await Post(boo, "Snooze", new() { ["days"] = "3" })).Headers.Location!.OriginalString);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(new DateOnly(2027, 1, 8), raw.UserSettings.Single(s => s.UserName == "boo").TodoSnoozedUntil);
        var again = await Login("boo@example.com");
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/kids")).StatusCode);   // 안 보기 기간: 할 일은 남아도 홈으로
        Assert.Contains("사 주세요 · 권동석 · 레고 블록", await again.GetStringAsync("/kids/todo"));   // 할 일은 ☰ 에서 그대로

        var kid = await Login("dongseok@example.com");
        Assert.Equal(HttpStatusCode.OK, (await kid.GetAsync("/kids")).StatusCode);   // 할 일이 없는 학생은 홈
    }

    [Fact]
    public async Task R310_선물은_그룹_누구나_사고_구매자가_보이고_전달_완료면_사라진다_일반_교사는_안_보인다()
    {
        var boo = await Login("boo@example.com");
        var page = await boo.GetStringAsync("/kids/todo");
        Assert.Contains("사 주세요 · 권동석 · 레고 블록", page);
        Assert.Matches("[3-9]일째 아무도 안 삼", page);   // 12/30 고름 → 1/5, 3일 넘음
        Assert.Contains("https://www.coupang.com/vp/products/1", page);
        var teacher = await Login("teacher@example.com");
        Assert.DoesNotContain("레고 블록", await teacher.GetStringAsync("/kids/todo"));   // 일반 교사(꼬리표 없음)

        await Post(boo, "Buy", new() { ["id"] = GiftId().ToString() });
        var admin = await Login("admin@example.com");
        var adminPage = await admin.GetStringAsync("/kids/todo");
        Assert.Contains("전해 주세요 · 권동석 · 레고 블록", adminPage);
        Assert.Contains("박부장님이 1/5 구매", adminPage);
        await Post(admin, "Deliver", new() { ["id"] = GiftId().ToString() });
        Assert.DoesNotContain("레고 블록", await boo.GetStringAsync("/kids/todo"));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(GiftStates.Delivered, raw.Gifts.Single().State);
        Assert.Equal(new[] { ("Purchase", "boo"), ("Deliver", "admin") }, raw.GiftLogs.OrderBy(l => l.Id).AsEnumerable().Select(l => (l.Action, l.ActorUserName!)).ToArray());

        var t2 = await Login("teacher@example.com");   // 꼬리표 없는 사람은 버튼을 눌러도 안 된다
        Assert.Contains("처리할 수 없는", await (await Post(t2, "Buy", new() { ["id"] = GiftId().ToString() })).Content.ReadAsStringAsync() + (await t2.GetStringAsync("/kids/todo")));
    }

    [Fact]
    public async Task R311_선물_담당_꼬리표를_켜면_교사에게도_선물_일이_뜬다()
    {
        var admin = await Login("admin@example.com");
        var token = TokenRx().Match(await admin.GetStringAsync($"/kids/admin/members/{_teacherMembership}")).Groups[1].Value;
        await admin.PostAsync($"/kids/admin/members/{_teacherMembership}?handler=GiftTag", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["on"] = "true" }));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Single(raw.MemberTags.Where(t => t.Tag == TodoRoles.GiftTag));
        Assert.Contains("선물 담당 끄기", await admin.GetStringAsync($"/kids/admin/members/{_teacherMembership}"));
        var teacher = await Login("teacher@example.com");
        Assert.Contains("사 주세요 · 권동석 · 레고 블록", await teacher.GetStringAsync("/kids/todo"));
    }

    [Fact]
    public async Task R312_누가_보나는_부서_규칙이_먼저고_따름이면_공통_규칙()
    {
        var admin = await Login("admin@example.com");
        Assert.Contains("공통 규칙 따름", await admin.GetStringAsync("/kids/admin/todo"));
        var form = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", TokenRx().Match(await admin.GetStringAsync("/kids/admin/todo")).Groups[1].Value), new("mode", "custom") };
        foreach (var k in TodoKinds.All) foreach (var r in TodoRoles.Default(k).Where(r => !(k == TodoKinds.Gift && r == TodoRoles.Officer))) form.Add(new($"r_{k}", r));   // 선물에서 부장 · 부감 · 서기 빼기
        await admin.PostAsync("/kids/admin/todo", new FormUrlEncodedContent(form));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(TodoKinds.All.Length, raw.TodoRules.Count(r => r.TenantId == _kids.Id));
        var boo = await Login("boo@example.com");
        Assert.DoesNotContain("레고 블록", await boo.GetStringAsync("/kids/todo"));

        var back = new Dictionary<string, string> { ["__RequestVerificationToken"] = TokenRx().Match(await admin.GetStringAsync("/kids/admin/todo")).Groups[1].Value, ["mode"] = "common" };
        await admin.PostAsync("/kids/admin/todo", new FormUrlEncodedContent(back));   // 공통 규칙 따름 → 부서 줄 지움
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.TodoRules);
        Assert.Contains("사 주세요 · 권동석 · 레고 블록", await boo.GetStringAsync("/kids/todo"));
    }

    [Fact]
    public async Task R312_플랫폼_관리자는_부서_현황을_보기만_한다()
    {
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        using (var raw = InMemoryDb.Raw(_db)) { raw.Users.Single(u => u.Id == op.Id).PasswordHash = new PasswordHasher<AppUser>().HashPassword(raw.Users.Single(u => u.Id == op.Id), "1234"); raw.SaveChanges(); }
        var c = await Login("operator@example.com");
        var page = await c.GetStringAsync("/platform/todo");
        Assert.Contains("부서 현황", page);
        Assert.Contains("🎁 사 주세요 1 · 1건은 3일 넘음", page);
        Assert.Contains("밀림 1", page);
        Assert.DoesNotContain("구매 완료", page);   // 처리 버튼 없음
        Assert.Contains("공통 규칙 · 누가 보나", page);
    }
}
