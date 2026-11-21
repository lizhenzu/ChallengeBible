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
        Assert.Contains("홈으로", page);
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
    public async Task R309_플랫폼_관리자가_가장으로_들어가도_로그인처럼_할_일이_있으면_한_번_할_일_화면으로()
    {
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        string booId;
        using (var raw = InMemoryDb.Raw(_db))
        {
            var row = raw.Users.Single(u => u.Id == op.Id);
            row.PasswordHash = new PasswordHasher<AppUser>().HashPassword(row, "1234");
            raw.SaveChanges();
            booId = raw.Users.Single(u => u.Email == "boo@example.com").UserName!;   // 계정 찾기의 id = 아이디
        }
        var c = await Login("operator@example.com");
        await c.GetAsync("/platform");   // 운영자 자신의 로그인 직후 한 번은 소모
        var token = TokenRx().Match(await c.GetStringAsync($"/platform/accounts?id={booId}")).Groups[1].Value;
        var go = await c.PostAsync($"/platform/accounts?handler=Impersonate&id={booId}", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, go.StatusCode);
        Assert.Equal("/kids/todo", (await c.GetAsync("/kids")).Headers.Location!.OriginalString);   // 가장도 로그인 직후처럼
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/kids")).StatusCode);                      // 두 번째부터는 홈
    }

    [Fact]
    public async Task R310_선물은_카드에서_보기만_하고_관리_선물_처리로_이동해_사고_전하면_사라진다()
    {
        var boo = await Login("boo@example.com");
        var page = await boo.GetStringAsync("/kids/todo");
        Assert.Contains("사 주세요 · 권동석 · 레고 블록", page);
        Assert.Matches("[3-9]일째 아무도 안 삼", page);   // 12/30 고름 → 1/5, 3일 넘음(타임라인)
        Assert.DoesNotContain("구매 완료", page);          // 카드에서는 완료하지 않는다
        Assert.DoesNotContain("🎁", page);                 // 이모지 없음(2026-11-16 사용자)
        var teacher = await Login("teacher@example.com");
        Assert.DoesNotContain("레고 블록", await teacher.GetStringAsync("/kids/todo"));   // 일반 교사(업무 없음)

        var admin = await Login("admin@example.com");
        var id = GiftId();
        var adminPage = await admin.GetStringAsync("/kids/todo");
        Assert.Contains($"/kids/admin/gifts?s=buy&amp;hit={id}#g{id}", adminPage);   // [선물 처리로 이동] = 실제 메뉴, 그 건 강조
        var hit = await admin.GetStringAsync($"/kids/admin/gifts?s=buy&hit={id}");
        Assert.Contains("할 일에서 왔어요", hit);
        Assert.Contains($"class=\"go-r go-hit\" id=\"g{id}\"", hit);   // 떨어진 카드 하나만 강조(확정 시안 관리-선물카드)
        // 관리 머리말(확정 시안 관리-햄버거): 가로 탭 대신 ☰ 메뉴판, 제목 = 지금 화면
        Assert.Contains("data-pnav-open", hit);
        Assert.DoesNotContain("class=\"wrap wide tabs\"", hit);
        Assert.Contains("<span class=\"gt-brand\"><em>도전!!!</em>성경말씀</span>", hit);   // 일반 ☰ 머리말(2026-11-21)
        Assert.Contains("<b>선물 처리</b>", hit);
        Assert.Contains("href=\"/kids/admin/gifts\" aria-current=\"page\"", hit);
        await GiftMove(admin, id, "Purchase");
        var after = await boo.GetStringAsync("/kids/todo");
        Assert.Contains("전해 주세요 · 권동석 · 레고 블록", after);
        Assert.Contains("윤동호 · 1/5", after);   // 산 사람 · 날짜
        await GiftMove(admin, id, "Deliver");
        Assert.DoesNotContain("레고 블록", await boo.GetStringAsync("/kids/todo"));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(GiftStates.Delivered, raw.Gifts.Single().State);
    }

    private static async Task GiftMove(HttpClient c, long id, string act)
    {
        var tok = TokenRx().Match(await c.GetStringAsync($"/kids/admin/gifts?s={(act == "Deliver" ? "give" : "buy")}")).Groups[1].Value;
        var r = await c.PostAsync("/kids/admin/gifts?handler=Move", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok, ["id"] = id.ToString(), ["act"] = act }));
        Assert.Contains($"ok={act}", r.Headers.Location?.OriginalString ?? r.StatusCode.ToString());
    }

    [Fact]
    public async Task R311_교사에게_선물_담당_업무를_맡기면_선물_일이_뜨고_관리자가_된다()
    {
        var admin = await Login("admin@example.com");
        var token = TokenRx().Match(await admin.GetStringAsync($"/kids/admin/members/{_teacherMembership}")).Groups[1].Value;
        await admin.PostAsync($"/kids/admin/members/{_teacherMembership}?handler=Duty", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["key"] = TodoRoles.GiftTag, ["on"] = "true" }));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Single(raw.MemberTags.Where(t => t.Tag == TodoRoles.GiftTag));
        var detail = await admin.GetStringAsync($"/kids/admin/members/{_teacherMembership}");
        Assert.Contains("✓ 선물 담당", detail);
        Assert.Contains("부서 관리자 · 업무 \"선물 담당\" 때문", System.Net.WebUtility.HtmlDecode(detail));
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
