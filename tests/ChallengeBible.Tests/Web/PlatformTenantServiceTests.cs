using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Web;

public class PlatformTenantServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly AppUser _op, _teacher;
    private PlatformTenantService Svc => new(new InMemorySystemDb(_db), TimeProvider.System);

    public PlatformTenantServiceTests()
    {
        _op = InMemoryDb.SeedAccount(_db, "operator", "운영자");
        using (var raw = InMemoryDb.Raw(_db)) { InMemoryDb.AddRole(_db, raw.Users.Single(a => a.UserName == _op.UserName!).Id, IdentityRoles.SuperAdmin); raw.SaveChanges(); }
        _teacher = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
        InMemoryDb.AddRole(_db, "seed", IdentityRoles.Admin);
    }

    private static NewTenant Input(string slug = "jamsil-kids", string? admin = null, bool mission = true, bool reading = true) =>
        new("잠실새내교회 유년부", slug, "Asia/Seoul", "blue", mission, reading, admin);

    [Fact]
    public async Task 부서_관리자로_추가하면_기다리던_참여_신청도_승인으로_닫힌다()
    {
        var t = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        var u = InMemoryDb.SeedAccount(_db, "admin456@example.com", "초등부관리자");
        var app = InMemoryDb.SeedMembership(_db, t.Id, u.UserName!, MembershipStatus.Requested, Roles.Teacher);

        Assert.Null(await Svc.AddAdminAsync(_op.UserName!, t.Id, "admin456@example.com"));

        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(SignupStatus.Approved, raw.SignupApplications.Single(a => a.Id == app.Id).Status);
        Assert.Single(raw.DeptRoles.Where(d => d.UserId == u.Id && d.TenantId == t.Id));
    }

    [Fact]
    public async Task 부서를_만들면_6자_참여_코드가_생긴다_0048()
    {
        var (t, e) = await Svc.CreateAsync(_op.UserName!, Input());
        Assert.True(e.Ok);
        Assert.Matches("^[A-HJ-NP-Z2-9]{6}$", t!.JoinCode);   // 0 · O · 1 · I 없음
        Assert.Equal(TenantStatus.Active, t.Status);
    }

    [Theory]
    [InlineData("A", "Slug")]
    [InlineData("jamsil kids", "Slug")]
    [InlineData("-kids", "Slug")]
    [InlineData("platform", "Slug")]
    [InlineData("join", "Slug")]
    public async Task 주소_검사(string slug, string field)
    {
        var (t, e) = await Svc.CreateAsync(_op.UserName!, Input(slug));
        Assert.Null(t);
        Assert.True(e.ContainsKey(field));
    }

    [Fact]
    public async Task 같은_주소는_두_번_못_만든다()
    {
        await Svc.CreateAsync(_op.UserName!, Input());
        Assert.Contains("이미", (await Svc.CreateAsync(_op.UserName!, Input())).Errors["Slug"]);
    }

    [Fact]
    public async Task R150_프로그램은_하나_이상()
    {
        Assert.True((await Svc.CreateAsync(_op.UserName!, Input(mission: false, reading: false))).Errors.ContainsKey("Programs"));
        var (t, _) = await Svc.CreateAsync(_op.UserName!, Input());
        Assert.NotNull(await Svc.SetProgramsAsync(_op.UserName!, t!.Id, false, false));
        Assert.Null(await Svc.SetProgramsAsync(_op.UserName!, t.Id, false, true));
    }

    [Fact]
    public async Task 기본_정보를_고치면_이름_시간대_색이_바뀌고_주소는_그대로()
    {
        var (t, _) = await Svc.CreateAsync(_op.UserName!, Input());
        var e = await Svc.UpdateInfoAsync(_op.UserName!, t!.Id, "  잠실새내교회 초등부 ", "America/New_York", "green");
        Assert.True(e.Ok);
        var row = (await Svc.ListAsync(_op.UserName!)).Single();
        Assert.Equal(("잠실새내교회 초등부", "America/New_York", "green", "jamsil-kids"), (row.Name, row.TimeZoneId, row.ThemeColor, row.Slug));
    }

    [Theory]
    [InlineData("", "Asia/Seoul", "blue", "Name")]
    [InlineData("초등부", "Mars/Base", "blue", "TimeZoneId")]
    [InlineData("초등부", "Asia/Seoul", "pink", "ThemeColor")]
    public async Task 기본_정보_검사(string name, string tz, string color, string field)
    {
        var (t, _) = await Svc.CreateAsync(_op.UserName!, Input());
        var e = await Svc.UpdateInfoAsync(_op.UserName!, t!.Id, name, tz, color);
        Assert.True(e.ContainsKey(field));
        Assert.Equal("잠실새내교회 유년부", (await Svc.ListAsync(_op.UserName!)).Single().Name);   // 하나라도 틀리면 아무것도 바꾸지 않는다
    }

    [Fact]
    public async Task 첫_관리자를_정하면_승인된_소속과_교사_관리자_역할이_생긴다()
    {
        var (t, e) = await Svc.CreateAsync(_op.UserName!, Input(admin: "EunHye"));
        Assert.True(e.Ok);
        using var raw = InMemoryDb.Raw(_db);
        var m = raw.DeptRoles.Single(x => x.TenantId == t!.Id && x.UserId == _teacher.Id);
        Assert.Equal("T1002", m.RoleCode);
        Assert.True(raw.UserRoles.Any(r => r.UserId == _teacher.Id));
        Assert.Equal("이은혜", (await Svc.ListAsync(_op.UserName!)).Single().Admins.Single().Name);
    }

    [Fact]
    public async Task 없는_아이디로는_관리자를_못_정한다()
    {
        var (t, e) = await Svc.CreateAsync(_op.UserName!, Input(admin: "nobody"));
        Assert.Null(t);
        Assert.True(e.ContainsKey("AdminEmail"));
    }

    [Fact]
    public async Task 관리자_빼기는_역할만_빼고_소속은_그대로()
    {
        var (t, _) = await Svc.CreateAsync(_op.UserName!, Input(admin: "eunhye"));
        await Svc.RemoveAdminAsync(_op.UserName!, t!.Id, _teacher.UserName!);
        using var raw = InMemoryDb.Raw(_db);
        Assert.True(raw.DeptRoles.Any(x => x.TenantId == t.Id && x.UserId == _teacher.Id));   // 소속은 그대로
        Assert.DoesNotContain(raw.UserRoles.ToList(), r => r.UserId == _teacher.Id);
    }

    [Fact]
    public async Task 코드를_바꾸면_예전_코드로는_못_찾고_정지하면_찾을_수_없다()
    {
        var (t, _) = await Svc.CreateAsync(_op.UserName!, Input());
        var old = t!.JoinCode;
        var join = new JoinService(new InMemoryDb(_db, new RequestScope()), new RequestScope(), TimeProvider.System);

        var fresh = await Svc.RegenerateCodeAsync(_op.UserName!, t.Id);
        Assert.NotEqual(old, fresh);
        Assert.Null(await join.LookupAsync(old));
        Assert.NotNull(await join.LookupAsync(fresh));

        await Svc.SetStatusAsync(_op.UserName!, t.Id, TenantStatus.Suspended);
        Assert.Null(await join.LookupAsync(fresh));
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_아무것도_못_한다()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.ListAsync(_teacher.UserName!));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.CreateAsync(_teacher.UserName!, Input()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.UpdateInfoAsync(_teacher.UserName!, 1, "초등부", "Asia/Seoul", "blue"));
    }
}
