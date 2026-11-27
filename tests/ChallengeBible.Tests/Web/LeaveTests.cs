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
/// R-342 탈퇴 신청 · R-343 탈퇴 승인(ADR 0119, 확정 시안 가입연결-탈퇴 ④ ⑤): 비밀번호 + "탈퇴" 로 신청, 승인 전엔 취소 · 평소처럼.
/// 부서 관리자 · 플랫폼 관리자의 할 일에 뜨고 둘 중 한 사람이 승인하면 계정 · 기록을 모두 지우고 명부 줄엔 탈퇴한 날만. 관리자 계정은 신청 · 승인 안 됨.
/// </summary>
public partial class LeaveTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;
    private readonly Dictionary<string, AppUser> _u = [];

    public LeaveTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        _elem = InMemoryDb.SeedTenant(_db, "elem", "ELEM01");
        Add("kid", "홍길동", _kids, Roles.Participant);
        Add("kadmin", "유년관리", _kids, Roles.Teacher, Roles.TenantAdmin);
        Add("eadmin", "초등관리", _elem, Roles.Teacher, Roles.TenantAdmin);
        Add("friend", "김친구", _kids, Roles.Participant);
        var sa = InMemoryDb.SeedAccount(_db, "superadmin@example.com", "운영자");
        InMemoryDb.AddRole(_db, sa.Id, IdentityRoles.SuperAdmin);
        using var raw = InMemoryDb.Raw(_db);
        raw.Users.Single(u => u.Id == sa.Id).UserName = "superadmin";
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        var kid = _u["kid"];
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "홍길동", UserId = kid.Id, Phone1 = "010-5555-1234", BirthMonth = 3, BirthDay = 15, CreatedUtc = DateTime.UtcNow });
        raw.SaveChanges();
        var pid = raw.People.Single(p => p.UserId == kid.Id).Id;
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = pid, Sunday = new DateOnly(2027, 1, 3), CreatedUtc = DateTime.UtcNow });
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = kid.UserName!, MissionItem = "word" });
        raw.Messages.Add(new Message { Kind = MessageKinds.Direct, SenderUserName = kid.UserName, RecipientUserName = _u["friend"].UserName, Body = "안녕", SentUtc = DateTime.UtcNow });
        raw.Messages.Add(new Message { Kind = MessageKinds.Direct, SenderUserName = _u["friend"].UserName, RecipientUserName = _u["kadmin"].UserName, Body = "남는 쪽지", SentUtc = DateTime.UtcNow });
        raw.Albums.Add(new Album { TenantId = _kids.Id, Title = "여름성경학교", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow });
        raw.SaveChanges();
        raw.Photos.Add(new Photo { TenantId = _kids.Id, AlbumId = raw.Albums.Single().Id, FileName = "a.jpg", StoredName = "a.jpg", ThumbName = "t_a.jpg", ContentType = "image/jpeg", Hash = "h", UploadedByUserName = kid.UserName, UploadedUtc = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private void Add(string key, string name, Tenant t, params string[] roles)
    {
        var a = InMemoryDb.SeedAccount(_db, key + "@example.com", name);
        InMemoryDb.SeedMembership(_db, t.Id, a.UserName!, MembershipStatus.Approved, roles);
        _u[key] = a;
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

    private static async Task<HttpResponseMessage> Post(HttpClient c, string page, string url, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync(page)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private static async Task<string> Text(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private async Task<HttpClient> Requested()
    {
        var kid = await Login("kid@example.com");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(kid, "/account", "/account?handler=Leave", new() { ["leavePw"] = "1234", ["leaveWord"] = "탈퇴" })).StatusCode);
        return kid;
    }

    private int LeaveId() { using var raw = InMemoryDb.Raw(_db); return raw.LeaveRequests.Single().Id; }

    [Fact]
    public async Task R342_비밀번호와_탈퇴_글자로_신청하고_승인_전엔_취소한다_관리자는_버튼이_없다()
    {
        var admin = await Login("kadmin@example.com");
        Assert.DoesNotContain("탈퇴 신청", WebUtility.HtmlDecode(await admin.GetStringAsync("/account")));

        var kid = await Login("kid@example.com");
        Assert.Contains("탈퇴 신청", WebUtility.HtmlDecode(await kid.GetStringAsync("/account")));
        Assert.Contains("비밀번호가 맞지 않아요", await Text(await Post(kid, "/account", "/account?handler=Leave", new() { ["leavePw"] = "9999", ["leaveWord"] = "탈퇴" })));
        Assert.Contains("탈퇴\" 라고 써 주세요", await Text(await Post(kid, "/account", "/account?handler=Leave", new() { ["leavePw"] = "1234", ["leaveWord"] = "네" })));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.LeaveRequests);

        await Post(kid, "/account", "/account?handler=Leave", new() { ["leavePw"] = "1234", ["leaveWord"] = "탈퇴" });
        var page = WebUtility.HtmlDecode(await kid.GetStringAsync("/account"));
        Assert.Contains("탈퇴 신청 중", page);
        Assert.Contains("승인 전까지 평소처럼 쓸 수 있어요", page);
        Assert.Equal(HttpStatusCode.OK, (await kid.GetAsync("/kids")).StatusCode);   // 평소처럼
        using (var raw = InMemoryDb.Raw(_db))
        {
            var to = raw.Messages.Where(m => m.Kind == MessageKinds.System && m.Body.Contains("탈퇴를 신청했어요")).Select(m => m.RecipientUserName).ToList();
            Assert.Contains(_u["kadmin"].UserName, to);
            Assert.Contains("superadmin", to);
            Assert.DoesNotContain(_u["eadmin"].UserName, to);
        }

        await Post(kid, "/account", "/account?handler=Stay", []);
        using var after = InMemoryDb.Raw(_db);
        Assert.Empty(after.LeaveRequests);
    }

    [Fact]
    public async Task R343_그_부서_관리자와_플랫폼_관리자_할_일에_뜨고_다른_부서_관리자는_못_본다()
    {
        await Requested();
        var kadmin = await Login("kadmin@example.com");
        var todo = WebUtility.HtmlDecode(await kadmin.GetStringAsync("/kids/todo"));
        Assert.Contains("탈퇴 신청 · 홍길동(", todo);
        Assert.Contains("탈퇴 승인(모두 지우기)", todo);
        var sa = await Login("superadmin@example.com");
        Assert.Contains("탈퇴 신청 · 홍길동", WebUtility.HtmlDecode(await sa.GetStringAsync("/platform/todo")));
        var eadmin = await Login("eadmin@example.com");
        Assert.DoesNotContain("탈퇴 신청 · 홍길동", WebUtility.HtmlDecode(await eadmin.GetStringAsync("/elem/todo")));   // (☰ 메뉴의 "탈퇴 신청" 항목과 구별)
        // 다른 부서 관리자가 번호로 승인해도 안 된다
        await Post(eadmin, "/elem/todo", "/elem/todo?handler=Leave", new() { ["leaveId"] = LeaveId().ToString(), ["leaveDo"] = "ok" });
        using var raw = InMemoryDb.Raw(_db);
        Assert.Single(raw.LeaveRequests);
        Assert.Contains(raw.Users, u => u.Id == _u["kid"].Id);
    }

    [Fact]
    public async Task R343_승인하면_계정과_기록을_모두_지우고_명부엔_탈퇴한_날만_남는다()
    {
        var kid = await Requested();
        var kadmin = await Login("kadmin@example.com");
        await Post(kadmin, "/kids/todo", "/kids/todo?handler=Leave", new() { ["leaveId"] = LeaveId().ToString(), ["leaveDo"] = "ok" });

        using (var raw = InMemoryDb.Raw(_db))
        {
            var id = _u["kid"].Id; var un = _u["kid"].UserName;
            Assert.DoesNotContain(raw.Users, u => u.Id == id);
            Assert.DoesNotContain(raw.DeptRoles, d => d.UserId == id);
            Assert.DoesNotContain(raw.MissionActivities, m => m.UserName == un);
            Assert.Empty(raw.AttendanceChecks);
            Assert.DoesNotContain(raw.Messages, m => m.SenderUserName == un || m.RecipientUserName == un);
            Assert.Contains(raw.Messages, m => m.Body == "남는 쪽지");   // 다른 사람끼리 쪽지는 그대로
            Assert.Empty(raw.LeaveRequests);
            var row = raw.People.Single(p => p.Name == "홍길동");   // 명부 줄은 남는다
            Assert.Null(row.UserId);
            Assert.Equal(((string?)null, (byte?)null, (byte?)null), (row.Phone1, row.BirthMonth, row.BirthDay));
            Assert.NotNull(row.LeftOn);
            var photo = raw.Photos.Single();   // 올린 사진은 남고 올린 사람만 비운다
            Assert.Null(photo.UploadedByUserName);
            Assert.Contains(raw.Users, u => u.Id == _u["kadmin"].Id);   // 관리자는 그대로
        }
        Assert.NotEqual(HttpStatusCode.InternalServerError, (await kid.GetAsync("/")).StatusCode);   // 지운 계정의 남은 로그인으로 와도 오류 없음
        Assert.Matches(@"\d+월 \d+일 탈퇴", WebUtility.HtmlDecode(await kadmin.GetStringAsync($"/kids/admin/members?Tab={MembershipStatus.Approved}")));   // 명부에 탈퇴한 날
    }

    [Fact]
    public async Task R343_반려하면_신청만_없어지고_본인에게_쪽지()
    {
        await Requested();
        var sa = await Login("superadmin@example.com");
        await Post(sa, "/platform/todo", "/platform/todo?handler=Leave", new() { ["leaveId"] = LeaveId().ToString(), ["leaveDo"] = "no" });
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.LeaveRequests);
        Assert.Contains(raw.Users, u => u.Id == _u["kid"].Id);
        Assert.Contains(raw.Messages, m => m.RecipientUserName == _u["kid"].UserName && m.Body.Contains("탈퇴 신청이 반려"));
    }

    [Fact]
    public async Task R343_관리자_계정은_신청도_승인도_안_된다()
    {
        using var scope = _app.Services.CreateScope();
        var leave = scope.ServiceProvider.GetRequiredService<LeaveService>();
        Assert.Equal(LeaveError.IsAdmin, await leave.RequestAsync(_u["kadmin"].UserName!, "1234", "탈퇴"));
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.LeaveRequests.Add(new LeaveRequest { TenantId = _kids.Id, UserId = _u["kadmin"].Id, UserName = _u["kadmin"].UserName!, FullName = "유년관리", RequestedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        Assert.Equal(LeaveError.IsAdmin, await leave.ApproveAsync("superadmin", LeaveId(), null));
        using var after = InMemoryDb.Raw(_db);
        Assert.Contains(after.Users, u => u.Id == _u["kadmin"].Id);
    }

    /// <summary>2026-11-20 사용자(확정 시안 탈퇴메뉴-관리자로그인): ☰ "탈퇴 신청"(기다리는 수) · 탈퇴 신청 화면 — 플랫폼은 모든 부서, 부서 관리자는 자기 부서만.</summary>
    [Fact]
    public async Task R343_메뉴_탈퇴_신청_화면에서_숫자를_보고_승인한다()
    {
        await Requested();
        var sa = await Login("superadmin@example.com");
        var page = WebUtility.HtmlDecode(await sa.GetStringAsync("/platform/leaves"));
        Assert.Contains("기다리는 탈퇴 1명", page);
        Assert.Contains("탈퇴 신청 · 홍길동(", page);
        Assert.Matches("href=\"/platform/leaves\"[^>]*>.*?탈퇴 신청 <em class=\"tdr2-k\"[^>]*>1</em>", page);   // ☰ 숫자

        var kadmin = await Login("kadmin@example.com");
        var mine = WebUtility.HtmlDecode(await kadmin.GetStringAsync("/kids/leaves"));
        Assert.Contains("기다리는 탈퇴 1명", mine);
        Assert.DoesNotContain("/kids/leaves", await kadmin.GetStringAsync("/kids"));   // 일반 메뉴판에서는 뺐다
        Assert.Matches("href=\"/kids/leaves\"[^>]*>.*?탈퇴 신청 <em class=\"tdr2-k\"[^>]*>1</em>", WebUtility.HtmlDecode(await kadmin.GetStringAsync("/kids/admin")));   // 관리 메뉴 ☰ 숫자
        Assert.Contains("class=\"cur\">탈퇴 신청</b>", await kadmin.GetStringAsync("/kids/leaves"));   // 관리 머리말 "도전!!!성경말씀 > 탈퇴 신청"
        Assert.Contains("data-pnav-open", await kadmin.GetStringAsync("/kids/leaves"));
        var eadmin = await Login("eadmin@example.com");
        Assert.Contains("기다리는 탈퇴 0명", WebUtility.HtmlDecode(await eadmin.GetStringAsync("/elem/leaves")));   // 다른 부서 것은 안 보인다
        var friend = await Login("friend@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await friend.GetAsync("/kids/leaves")).StatusCode);   // 관리자만
        Assert.DoesNotContain("/kids/leaves", await friend.GetStringAsync("/kids"));

        await Post(sa, "/platform/leaves", "/platform/leaves?handler=Leave", new() { ["leaveId"] = LeaveId().ToString(), ["leaveDo"] = "ok" });
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.LeaveRequests);
        Assert.DoesNotContain(raw.Users, u => u.Id == _u["kid"].Id);
    }

    [Fact]
    public async Task 로그인_화면에_플랫폼_관리자_로그인_버튼()
    {
        var html = WebUtility.HtmlDecode(await _app.CreateClient().GetStringAsync("/account/login"));
        Assert.Contains("href=\"/account/admin-login\">🔒 플랫폼 관리자 로그인</a>", html);
    }

    // R-348: 가장 중 탈퇴 — 비밀번호 · 확인 글자 · 승인 없이 바로. 관리자 계정은 안 된다(R-315)

    [Fact]
    public async Task R348_가장_중에는_비밀번호_없이_바로_탈퇴하고_플랫폼_홈으로_돌아온다()
    {
        var sa = await Login("superadmin@example.com");
        var kidName = _u["kid"].UserName!;
        await Post(sa, $"/platform/accounts?q=kid&id={kidName}&askImp=true", $"/platform/accounts?handler=Impersonate&q=kid&id={kidName}&askImp=true", []);
        var page = await Text(await sa.GetAsync("/account"));
        Assert.Contains("탈퇴 처리(가장)", page);
        Assert.DoesNotContain("leavePw", page);   // 비밀번호 칸 없음

        var r = await Post(sa, "/account", "/account?handler=LeaveNow", []);
        Assert.Equal("/platform", r.Headers.Location!.OriginalString);
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.DoesNotContain(raw.Users, u => u.UserName == kidName);
            Assert.DoesNotContain(raw.DeptRoles, d => d.UserId == _u["kid"].Id);
            Assert.Empty(raw.LeaveRequests);
            Assert.Contains(raw.AuditLogs, l => l.Action == LeaveService.Action && l.ActorUserName == "superadmin" && l.Detail!.Contains("탈퇴 승인"));
            Assert.NotNull(raw.People.Single(p => p.Name == "홍길동").LeftOn);
            Assert.Contains(raw.Users, u => u.UserName == "superadmin");   // 플랫폼 관리자는 그대로
        }
        Assert.Equal(HttpStatusCode.OK, (await sa.GetAsync("/platform/leaves")).StatusCode);   // 플랫폼 관리자로 돌아옴
        Assert.DoesNotContain("가장 중", await sa.GetStringAsync("/platform/leaves"));
    }

    [Fact]
    public async Task R348_가장하지_않으면_바로_탈퇴를_못_하고_관리자_계정은_가장해도_안_지운다()
    {
        var kid = await Login("kid@example.com");
        await Post(kid, "/account", "/account?handler=LeaveNow", []);   // 가장 중이 아니면 거절
        using (var raw = InMemoryDb.Raw(_db)) Assert.Contains(raw.Users, u => u.Id == _u["kid"].Id);

        var sa = await Login("superadmin@example.com");
        var an = _u["kadmin"].UserName!;
        await Post(sa, $"/platform/accounts?q=k&id={an}&askImp=true", $"/platform/accounts?handler=Impersonate&q=k&id={an}&askImp=true", []);
        var page = await Text(await sa.GetAsync("/account"));
        Assert.DoesNotContain("탈퇴 처리(가장)", page);   // 관리자 계정에는 버튼이 없다
        await Post(sa, "/account", "/account?handler=LeaveNow", []);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Contains(raw.Users, u => u.Id == _u["kadmin"].Id);   // R-315
    }

    [Fact]
    public async Task 오늘_할_일_머리말은_메뉴_버튼과_한_줄_제목이다()
    {
        var kadmin = await Login("kadmin@example.com");
        var page = await Text(await kadmin.GetAsync("/kids/todo"));
        Assert.Contains("data-pnav-open", page);   // ☰ 메뉴 버튼
        Assert.Contains("id=\"pnav\"", page);   // 메뉴판
        Assert.Contains("td-one", page);
        Assert.Matches(@"행정업무</a><span class=""td-sep"" aria-hidden=""true"">></span><b>오늘 할 일</b>", page);   // 행정업무 > 오늘 할 일(2026-11-25, 「도전」 없음)
    }
}
