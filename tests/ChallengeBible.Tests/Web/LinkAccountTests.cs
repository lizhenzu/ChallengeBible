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
/// R-324 명부 · 계정 잇기(확정 시안 명부계정연결, 2026-11-17 사용자): 승인 뒤에도 명부 줄(가입 안 함)에 같은 이름 계정을 잇는다(부서 관리자만).
/// 계정의 빈 명부 줄은 지우고 선물을 옮긴다. 계정이 생일 · 반 · 출석이 있는 줄과 이어져 있으면 하지 않는다. 참여 신청 생일(선택) → 승인 후보 "생일 같음" 맨 위.
/// </summary>
public partial class LinkAccountTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly string _kidId;

    public LinkAccountTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var t = InMemoryDb.SeedAccount(_db, "teacher@example.com", "김선생");
        InMemoryDb.SeedMembership(_db, _kids.Id, t.UserName!, MembershipStatus.Approved, Roles.Teacher);
        var kid = InMemoryDb.SeedAccount(_db, "jjc@example.com", "조진찬");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        _kidId = kid.Id;
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "윤동호", UserId = adm.Id, CreatedUtc = DateTime.UtcNow });
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "조진찬", UserId = kid.Id, CreatedUtc = DateTime.UtcNow });   // 승인 때 새 사람으로 올라간 빈 줄
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "조진찬", RoleCode = "T1001", BirthMonth = 2, BirthDay = 3, Phone1 = "010-1111-2222", CreatedUtc = DateTime.UtcNow });   // 명부(가입 안 함)
        raw.SaveChanges();
        var roster = raw.People.Single(p => p.Name == "조진찬" && p.UserId == null);
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = roster.Id, Sunday = new DateOnly(2027, 1, 3), CreatedUtc = DateTime.UtcNow });
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

    private const string List = "/kids/admin/members?Tab=Approved";

    private int RosterId() { using var raw = InMemoryDb.Raw(_db); return raw.People.Single(p => p.Name == "조진찬" && p.BirthMonth == 2).Id; }

    private static async Task<HttpResponseMessage> Link(HttpClient c, int pid, string uid)
    {
        var tok = TokenRx().Match(await c.GetStringAsync(List)).Groups[1].Value;
        return await c.PostAsync("/kids/admin/members?handler=LinkAccount", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = tok, ["pid"] = pid.ToString(), ["uid"] = uid }));
    }

    [Fact]
    public async Task R324_명부에서_같은_이름_계정과_잇고_빈_줄은_합친다()
    {
        var admin = await Login("admin@example.com");
        await admin.GetAsync("/kids");
        var page = await admin.GetStringAsync(List);
        Assert.Contains("같은 이름 계정 있음", page);
        Assert.Contains("조진찬 — 어느 계정과 연결할까요?", page);
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(page, "46-명부-계정과연결");

        var pid = RosterId();
        int blankId;
        using (var raw = InMemoryDb.Raw(_db))
        {
            blankId = raw.People.Single(p => p.UserId == _kidId).Id;
            raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Copy, Year = 2027, UserName = "x", PersonId = blankId, SourceKey = "C1", State = GiftStates.Open, CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var r = await Link(admin, pid, _kidId);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = raw.People.Single(x => x.Id == pid);
            Assert.Equal(_kidId, p.UserId);                                   // 명부 줄(생일 · 출석)과 이어짐
            Assert.False(raw.People.Any(x => x.Id == blankId));              // 빈 줄은 지움
            Assert.Single(raw.People.Where(x => x.UserId == _kidId));
            Assert.Equal(1, raw.AttendanceChecks.Count(a => a.PersonId == pid));
            Assert.Equal(pid, raw.Gifts.Single(g => g.SourceKey == "C1").PersonId);   // 선물도 옮김
            Assert.Contains(raw.AuditLogs, a => a.Action == "PersonLinkAccount");
        }
        Assert.DoesNotContain("같은 이름 계정 있음", await admin.GetStringAsync(List));
    }

    [Fact]
    public async Task R324_계정이_이미_채워진_줄과_이어져_있으면_잇지_않고_관리자만_한다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var blank = raw.People.Single(p => p.UserId == _kidId);
            blank.BirthMonth = 5; blank.BirthDay = 5;   // 생일이 있는 줄 = 채워진 줄
            raw.SaveChanges();
        }
        var admin = await Login("admin@example.com");
        await admin.GetAsync("/kids");
        Assert.DoesNotContain("같은 이름 계정 있음", await admin.GetStringAsync(List));
        var r = await Link(admin, RosterId(), _kidId);
        Assert.Contains("먼저 그 줄의 연결을 풀어 주세요", await r.Content.ReadAsStringAsync());
        using (var raw = InMemoryDb.Raw(_db)) Assert.Null(raw.People.Single(p => p.Id == RosterId()).UserId);

        var teacher = await Login("teacher@example.com");
        await teacher.GetAsync("/kids");
        Assert.DoesNotContain("계정과 연결", await teacher.GetStringAsync(List));   // 교사에게는 버튼 없음
        await Link(teacher, RosterId(), _kidId);                                    // 보내도 막힌다(부서 관리자만)
        using (var raw = InMemoryDb.Raw(_db)) Assert.Null(raw.People.Single(p => p.Id == RosterId()).UserId);
    }

    [Fact]
    public async Task R324_참여_신청에_쓴_생일이_같은_명부_후보를_맨_위에_생일_같음으로()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.People.Add(new Person { TenantId = _kids.Id, Name = "박서윤", RoleCode = "T1001", BirthMonth = 7, BirthDay = 21, CreatedUtc = DateTime.UtcNow });
            raw.People.Add(new Person { TenantId = _kids.Id, Name = "박서윤", RoleCode = "T1001", BirthMonth = 2, BirthDay = 3, CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var lobby = InMemoryDb.SeedTenant(_db, "welcome", "WELCOM");
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(t => t.Id == lobby.Id).IsLobby = true; raw.Tenants.Single(t => t.Id == _kids.Id).JoinListed = true; raw.SaveChanges(); }
        var sy = InMemoryDb.SeedAccount(_db, "sy@example.com", "박서윤");
        using var scope = _app.Services.CreateScope();
        var join = scope.ServiceProvider.GetRequiredService<JoinService>();
        Assert.Equal(JoinError.None, (await join.ApplyAsync(sy.UserName!, _kids.Id, null, SignupRoleKind.Student, CancellationToken.None, 2, 3)).Error);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal((2, 3), raw.SignupApplications.Where(a => a.UserName == sy.UserName).Select(a => new { a.BMonth, a.BDay }).AsEnumerable().Select(a => (a.BMonth, a.BDay)).Single());

        var admin = await Login("admin@example.com");
        await admin.GetAsync("/kids");
        var page = await admin.GetStringAsync("/kids/admin/members?Tab=Requested");
        var same = page.IndexOf("생일 같음", StringComparison.Ordinal);
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(page, "47-신청-생일같음");
        Assert.True(same > 0);
        Assert.True(same < page.IndexOf("7월 21일", StringComparison.Ordinal));   // 생일 같은 줄(2월 3일)이 먼저
    }
}
