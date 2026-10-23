using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Web;

public class PlatformTenantServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly Account _op, _teacher;
    private PlatformTenantService Svc => new(new InMemorySystemDb(_db), TimeProvider.System);

    public PlatformTenantServiceTests()
    {
        _op = InMemoryDb.SeedAccount(_db, "operator", "운영자");
        using (var raw = InMemoryDb.Raw(_db)) { raw.Accounts.Single(a => a.Id == _op.Id).IsPlatformAdmin = true; raw.SaveChanges(); }
        _teacher = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
    }

    private static NewTenant Input(string slug = "jamsil-kids", string? admin = null, bool mission = true, bool reading = true) =>
        new("잠실새내교회 유년부", slug, "Asia/Seoul", "blue", mission, reading, admin);

    [Fact]
    public async Task 부서를_만들면_6자_참여_코드가_생긴다_0048()
    {
        var (t, e) = await Svc.CreateAsync(_op.Id, Input());
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
        var (t, e) = await Svc.CreateAsync(_op.Id, Input(slug));
        Assert.Null(t);
        Assert.True(e.ContainsKey(field));
    }

    [Fact]
    public async Task 같은_주소는_두_번_못_만든다()
    {
        await Svc.CreateAsync(_op.Id, Input());
        Assert.Contains("이미", (await Svc.CreateAsync(_op.Id, Input())).Errors["Slug"]);
    }

    [Fact]
    public async Task R150_프로그램은_하나_이상()
    {
        Assert.True((await Svc.CreateAsync(_op.Id, Input(mission: false, reading: false))).Errors.ContainsKey("Programs"));
        var (t, _) = await Svc.CreateAsync(_op.Id, Input());
        Assert.NotNull(await Svc.SetProgramsAsync(_op.Id, t!.Id, false, false));
        Assert.Null(await Svc.SetProgramsAsync(_op.Id, t.Id, false, true));
    }

    [Fact]
    public async Task 첫_관리자를_정하면_승인된_소속과_교사_관리자_역할이_생긴다()
    {
        var (t, e) = await Svc.CreateAsync(_op.Id, Input(admin: "EunHye"));
        Assert.True(e.Ok);
        using var raw = InMemoryDb.Raw(_db);
        var m = raw.Memberships.Include(x => x.Roles).Single(x => x.TenantId == t!.Id && x.AccountId == _teacher.Id);
        Assert.Equal(MembershipStatus.Approved, m.Status);
        Assert.Equal([Roles.Teacher, Roles.TenantAdmin], m.Roles.Select(r => r.Role).Order(StringComparer.Ordinal));
        Assert.Equal("이은혜", (await Svc.ListAsync(_op.Id)).Single().Admins.Single().Name);
    }

    [Fact]
    public async Task 없는_아이디로는_관리자를_못_정한다()
    {
        var (t, e) = await Svc.CreateAsync(_op.Id, Input(admin: "nobody"));
        Assert.Null(t);
        Assert.True(e.ContainsKey("AdminEmail"));
    }

    [Fact]
    public async Task 관리자_빼기는_역할만_빼고_소속은_그대로()
    {
        var (t, _) = await Svc.CreateAsync(_op.Id, Input(admin: "eunhye"));
        await Svc.RemoveAdminAsync(_op.Id, t!.Id, _teacher.Id);
        using var raw = InMemoryDb.Raw(_db);
        var m = raw.Memberships.Include(x => x.Roles).Single(x => x.TenantId == t.Id);
        Assert.Equal(MembershipStatus.Approved, m.Status);
        Assert.Equal([Roles.Teacher], m.Roles.Select(r => r.Role));
    }

    [Fact]
    public async Task 코드를_바꾸면_예전_코드로는_못_찾고_정지하면_찾을_수_없다()
    {
        var (t, _) = await Svc.CreateAsync(_op.Id, Input());
        var old = t!.JoinCode;
        var join = new JoinService(new InMemoryDb(_db, new RequestScope()), new RequestScope(), TimeProvider.System);

        var fresh = await Svc.RegenerateCodeAsync(_op.Id, t.Id);
        Assert.NotEqual(old, fresh);
        Assert.Null(await join.LookupAsync(old));
        Assert.NotNull(await join.LookupAsync(fresh));

        await Svc.SetStatusAsync(_op.Id, t.Id, TenantStatus.Suspended);
        Assert.Null(await join.LookupAsync(fresh));
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_아무것도_못_한다()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.ListAsync(_teacher.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.CreateAsync(_teacher.Id, Input()));
    }
}
