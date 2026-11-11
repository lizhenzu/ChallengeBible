using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>명부(ADR 0093 사람 · 소속): R-226 생일 · 연락처, R-227 올리기 · 지우기, R-228 승인 때 연결, R-229 연결 풀기, R-230 진급 · 모두 받기.</summary>
public class PeopleTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem, _lobby;
    private readonly AppUser _admin, _elemAdmin;

    public PeopleTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _lobby = InMemoryDb.SeedTenant(_db, "lobby", "LOBBY1");
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(t => t.Id == _lobby.Id).IsLobby = true; raw.SaveChanges(); }
        _admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        _elemAdmin = InMemoryDb.SeedAccount(_db, "minsu@example.com", "박민수");
        InMemoryDb.SeedMembership(_db, _kids.Id, _admin.UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _elem.Id, _elemAdmin.UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
    }

    private (RequestScope Scope, TenantContext Ctx) In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) ctx.Set(raw.Tenants.Single(x => x.Id == t.Id), null);
        return (scope, ctx);
    }

    private PeopleService People(Tenant t) { var (s, c) = In(t); return new PeopleService(new InMemoryDb(_db, s), c, _time); }
    private MemberAdminService Members(Tenant t) { var (s, c) = In(t); return new MemberAdminService(new InMemoryDb(_db, s), new InMemorySystemDb(_db), c, _time); }
    private PromotionApprover Approver(Tenant t) { var (s, c) = In(t); return new PromotionApprover(new InMemoryDb(_db, s), new InMemorySystemDb(_db), c, _time); }
    private AppUser Account(string login, string name) => InMemoryDb.SeedAccount(_db, login + "@example.com", name);

    private int PendingPromotion(AppUser kid, Tenant from, Tenant to)
    {
        using var raw = InMemoryDb.Raw(_db);
        var r = new PromotionRequest { UserName = kid.UserName!, SourceTenantId = from.Id, TargetTenantId = to.Id, RequestedByUserName = "t" };
        raw.PromotionRequests.Add(r);
        raw.SaveChanges();
        return r.Id;
    }

    [Fact]
    public async Task R227_명부에_올리고_고치고_지운다_가입한_줄은_지우지_않는다()
    {
        var (e, id) = await People(_kids).AddAsync("eunhye", new PersonInput("박하린", "T1001", 3, 5, "010-1234-5678", null));
        Assert.Equal(PeopleError.None, e);
        Assert.Equal(PeopleError.None, await People(_kids).UpdateAsync(id, new PersonInput("박하린", "T1020", 3, 6, "010 1234 5678", "010-9876-5432")));

        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = raw.People.Single(x => x.Id == id);
            Assert.Equal((_kids.Id, "T1020", (byte?)6, "010-1234-5678", "010-9876-5432", (string?)null), (p.TenantId, p.RoleCode, p.BirthDay, p.Phone1, p.Phone2, p.UserId));
        }
        var adminRow = (await People(_kids).RosterAsync(null, null)).Single(r => r.Linked);   // 소속만 있던 관리자도 명부에 보인다
        Assert.Equal(PeopleError.Linked, await People(_kids).DeleteAsync(adminRow.PersonId));
        Assert.Equal(PeopleError.NotFound, await People(_elem).DeleteAsync(id));                // 다른 부서 줄은 보이지 않는다
        Assert.Equal(PeopleError.None, await People(_kids).DeleteAsync(id));
        using var after = InMemoryDb.Raw(_db);
        Assert.DoesNotContain(after.People, p => p.Id == id);
    }

    [Theory]
    [InlineData(2, 30, null, PeopleError.BadBirthday)]
    [InlineData(13, 1, null, PeopleError.BadBirthday)]
    [InlineData(3, null, null, PeopleError.BadBirthday)]
    [InlineData(null, null, "010-12ab", PeopleError.BadPhone)]
    [InlineData(null, null, "0101", PeopleError.BadPhone)]
    [InlineData(2, 29, "+82 10 1234 5678", PeopleError.None)]
    public void R226_생일은_월_일만_연락처는_숫자로(int? m, int? d, string? phone, PeopleError expected) =>
        Assert.Equal(expected, PeopleService.Check(new PersonInput("가", null, m, d, phone, null), needName: true).Error);

    [Fact]
    public async Task R228_승인할_때_같은_이름_명부_줄을_고르면_계정이_이어지고_새_줄은_생기지_않는다()
    {
        var (_, pid) = await People(_kids).AddAsync("eunhye", new PersonInput("김 철수", "T1001", 11, 2, null, null));
        var kid = Account("cheolsu", "김철수");
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);

        var cands = await People(_kids).CandidatesAsync(["김철수"]);
        Assert.Equal(pid, Assert.Single(cands[PeopleService.Key("김철수")]).PersonId);    // 띄어쓰기는 보지 않는다
        Assert.Equal(MemberError.None, await Members(_kids).ApproveAsync("eunhye", a.Id, "T1001", pid));

        using var raw = InMemoryDb.Raw(_db);
        var p = Assert.Single(raw.People.Where(x => x.UserId == kid.Id));
        Assert.Equal((pid, (byte?)11, (string?)null), (p.Id, p.BirthMonth, p.RoleCode));
    }

    [Fact]
    public async Task R228_새_사람으로_승인하면_명부에_새_줄이_생긴다()
    {
        var (_, pid) = await People(_kids).AddAsync("eunhye", new PersonInput("김철수", "T1001", null, null, null, null));
        var kid = Account("cheolsu", "김철수");
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);

        Assert.Equal(MemberError.None, await Members(_kids).ApproveAsync("eunhye", a.Id, "T1001", null));

        using var raw = InMemoryDb.Raw(_db);
        Assert.NotEqual(pid, raw.People.Single(x => x.UserId == kid.Id).Id);
        Assert.Null(raw.People.Single(x => x.Id == pid).UserId);
    }

    [Fact]
    public async Task R229_연결을_풀면_옛_줄은_가입_안_함으로_돌아가고_계정은_새_줄을_받는다()
    {
        var (_, pid) = await People(_kids).AddAsync("eunhye", new PersonInput("김철수", "T1001", 4, 20, "010-7777-8888", null));
        var kid = Account("cheolsu", "김철수");
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);
        await Members(_kids).ApproveAsync("eunhye", a.Id, "T1001", pid);

        Assert.Equal(PeopleError.None, await People(_kids).UnlinkAsync("eunhye", pid));
        Assert.Equal(PeopleError.NotLinked, await People(_kids).UnlinkAsync("eunhye", pid));

        using var raw = InMemoryDb.Raw(_db);
        var old = raw.People.Single(x => x.Id == pid);
        Assert.Equal(((string?)null, "T1001", "010-7777-8888"), (old.UserId, old.RoleCode, old.Phone1));
        var fresh = raw.People.Single(x => x.UserId == kid.Id);
        Assert.Equal((_kids.Id, (byte?)null), (fresh.TenantId, fresh.BirthMonth));
        Assert.Single(raw.DeptRoles.Where(d => d.UserId == kid.Id));                 // 소속은 그대로
    }

    [Fact]
    public async Task R230_진급하면_명부_줄이_새_부서로_옮겨지고_옛_부서에는_남지_않는다()
    {
        var kid = Account("cheolsu", "김철수");
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);
        await Members(_kids).ApproveAsync("eunhye", a.Id, "T1001");
        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = raw.People.Single(x => x.UserId == kid.Id);
            (p.BirthMonth, p.BirthDay, p.Phone1) = (11, 2, "010-1111-2222");
            raw.SaveChanges();
        }
        var id = PendingPromotion(kid, _kids, _elem);

        Assert.Equal(PromotionError.None, await Approver(_elem).ApproveAsync("minsu", id, "T1001"));

        using var after = InMemoryDb.Raw(_db);
        var moved = Assert.Single(after.People.Where(x => x.UserId == kid.Id));
        Assert.Equal((_elem.Id, (byte?)11, "010-1111-2222"), (moved.TenantId, moved.BirthMonth, moved.Phone1));
        Assert.DoesNotContain(after.People, x => x.TenantId == _kids.Id && x.Name == "김철수");
    }

    [Fact]
    public async Task R230_모두_받기는_기다리는_요청을_같은_역할로_한_번에_받는다()
    {
        var k1 = Account("cheolsu", "김철수");
        var k2 = Account("younghee", "김영희");
        InMemoryDb.SeedMembership(_db, _kids.Id, k1.UserName!, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _kids.Id, k2.UserName!, MembershipStatus.Approved, Roles.Participant);
        PendingPromotion(k1, _kids, _elem);
        PendingPromotion(k2, _kids, _elem);

        Assert.Equal((2, 0), await Approver(_elem).ApproveAllAsync("minsu", "T1001"));
        Assert.Equal((0, 0), await Approver(_kids).ApproveAllAsync("eunhye", "T1001"));   // 보낸 부서는 받을 것이 없다

        using var raw = InMemoryDb.Raw(_db);
        Assert.All(raw.DeptRoles.Where(d => d.UserId == k1.Id || d.UserId == k2.Id), d => Assert.Equal((_elem.Id, "T1001"), (d.TenantId, d.RoleCode)));
        Assert.Equal(2, raw.People.Count(p => p.TenantId == _elem.Id && (p.UserId == k1.Id || p.UserId == k2.Id)));
    }

    [Fact]
    public async Task 임시_부서로_가면_옛_명부_줄은_연결만_풀려_남는다()
    {
        var kid = Account("cheolsu", "김철수");
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);
        await Members(_kids).ApproveAsync("eunhye", a.Id, "T1001");
        await using (var sys = new InMemorySystemDb(_db).Create("eunhye"))
        {
            var user = sys.Users.Single(u => u.Id == kid.Id);
            await DeptMembership.PlaceAsync(sys, user, _lobby.Id, null, DateTime.UtcNow, default);
        }
        using var raw = InMemoryDb.Raw(_db);
        var p = raw.People.Single(x => x.Name == "김철수");
        Assert.Equal((_kids.Id, (string?)null, "T1001"), (p.TenantId, p.UserId, p.RoleCode));
        Assert.DoesNotContain(raw.People, x => x.TenantId == _lobby.Id);
    }

    [Fact]
    public async Task R226_본인은_생일_연락처를_직접_넣고_임시_부서뿐이면_넣지_못한다()
    {
        var kid = Account("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        var mine = new MyAccountService(new InMemorySystemDb(_db));

        Assert.Equal(PeopleError.BadPhone, await mine.SaveProfileAsync("cheolsu", 9, 1, "abc", null, DateTime.UtcNow));
        Assert.Equal(PeopleError.None, await mine.SaveProfileAsync("cheolsu", 9, 1, "010-5555-1212", null, DateTime.UtcNow));
        var p = await mine.ProfileAsync("cheolsu");
        Assert.Equal((_kids.Id, (byte?)9, "010-5555-1212"), (p!.TenantId, p.BirthMonth, p.Phone1));

        var guest = Account("guest", "손님");
        InMemoryDb.SeedMembership(_db, _lobby.Id, guest.UserName!, MembershipStatus.Approved, Roles.Participant);
        Assert.Null(await mine.ProfileAsync("guest"));
        Assert.Equal(PeopleError.NotFound, await mine.SaveProfileAsync("guest", 1, 1, null, null, DateTime.UtcNow));
    }
}

/// <summary>명부 화면 권한(R-226 · R-227): 부서 관리자 · 교사만. 교사는 올리기 · 고치기만, 학생 · 임원은 들어오지 못한다.</summary>
public partial class PeopleFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public PeopleFlowTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Member(string login, string name, string? roleCode, bool admin = false)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var tok = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value;
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tok, ["DisplayName"] = name, ["Email"] = login + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        string userName;
        using (var raw = InMemoryDb.Raw(_db)) userName = raw.Users.Single(a => a.Email == login + "@example.com").UserName!;
        InMemoryDb.SeedMembership(_db, _kids.Id, userName, MembershipStatus.Approved, admin ? Roles.TenantAdmin : Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var id = raw.Users.Single(u => u.UserName == userName).Id;
            raw.DeptRoles.Single(d => d.UserId == id).RoleCode = roleCode;
            raw.SaveChanges();
        }
        return c;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string page, string handler, params (string, string)[] form)
    {
        var tok = TokenRx().Match(await c.GetStringAsync(page)).Groups[1].Value;
        var data = new Dictionary<string, string> { ["__RequestVerificationToken"] = tok };
        foreach (var (k, v) in form) data[k] = v;
        return await c.PostAsync($"{page}{(page.Contains('?') ? "&" : "?")}handler={handler}", new FormUrlEncodedContent(data));
    }

    private static void Denied(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/account/denied", r.Headers.Location!.PathAndQuery);
    }

    private const string Page = "/jamsil-kids/admin/members";

    [Fact]
    public async Task R227_교사는_명부를_보고_올리고_고치지만_지우거나_승인하지_못한다()
    {
        var t = await Member("teacher", "정교사", "T1002");
        var home = await t.GetStringAsync(Page + "?Tab=" + MembershipStatus.Requested);
        Assert.Contains("+ 명부에 올리기", home);                 // 교사는 늘 명부 탭
        Assert.DoesNotContain("?Tab=" + MembershipStatus.Requested + "\"", home);

        var r = await Post(t, Page, "AddPerson", ("name", "박하린"), ("roleCode", "T1001"), ("month", "3"), ("day", "5"), ("phone1", "010-1234-5678"));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        int pid;
        using (var raw = InMemoryDb.Raw(_db)) pid = raw.People.Single(p => p.Name == "박하린").Id;
        Assert.Contains("010-1234-5678", await t.GetStringAsync(Page));     // 교사는 연락처를 본다(R-226)

        Assert.Equal(HttpStatusCode.Redirect, (await Post(t, Page, "Person", ("pid", pid.ToString()), ("name", "박하린"), ("phone1", "010-1234-0000"))).StatusCode);
        Denied(await Post(t, Page, "DeletePerson", ("pid", pid.ToString())));
        Denied(await Post(t, Page, "Approve", ("id", "1")));
        using var after = InMemoryDb.Raw(_db);
        Assert.Equal("010-1234-0000", after.People.Single(p => p.Id == pid).Phone1);
    }

    [Theory]
    [InlineData("T1001")]   // 학생
    [InlineData("T1005")]   // 임원(부장)
    [InlineData(null)]      // 학부모
    public async Task R226_학생_임원_학부모는_명부와_연락처를_볼_수_없다(string? code)
    {
        var c = await Member("someone", "아무개", code);
        Denied(await c.GetAsync(Page));
    }

    [Fact]
    public async Task 관리자는_명부에서_지우고_교사_홈에는_명부_링크가_보인다()
    {
        var a = await Member("admin", "이관리", "T1002", admin: true);
        var added = await Post(a, Page + "?Tab=" + MembershipStatus.Approved, "AddPerson", ("name", "새친구"), ("roleCode", "T1020"));
        Assert.True(added.StatusCode == HttpStatusCode.Redirect, $"{added.StatusCode} {added.Headers.Location}");
        int pid;
        using (var raw = InMemoryDb.Raw(_db)) pid = raw.People.Single(p => p.Name == "새친구").Id;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(a, Page + "?Tab=" + MembershipStatus.Approved, "DeletePerson", ("pid", pid.ToString()))).StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) Assert.DoesNotContain(raw.People, p => p.Id == pid);

        var t = await Member("teacher", "정교사", "T1002");
        Assert.Contains("href=\"/jamsil-kids/admin/members\"", await t.GetStringAsync("/jamsil-kids"));
    }
}
