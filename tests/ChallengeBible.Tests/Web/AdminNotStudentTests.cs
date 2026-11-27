using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>R-365 관리자는 학생이 될 수 없다: 관리자 계정(Admin · SuperAdmin)은 학생 · 새친구 소속을 받지 못하고, 교사 · 어드민은 된다.</summary>
public class AdminNotStudentTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly Tenant _lobby;

    public AdminNotStudentTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        _lobby = InMemoryDb.SeedTenant(_db, "welcome", "LOBBY1");
        using var raw = InMemoryDb.Raw(_db);
        raw.Tenants.Single(t => t.Id == _lobby.Id).IsLobby = true;
        raw.SaveChanges();
    }

    private AppUser Account(string email, string? identityRole)
    {
        var u = InMemoryDb.SeedAccount(_db, email, "김가나");
        if (identityRole is not null) InMemoryDb.AddRole(_db, u.Id, identityRole);
        return u;
    }

    private async Task<MoveError> Place(AppUser u, string? code)
    {
        await using var sys = new InMemorySystemDb(_db).Create("op");
        var user = sys.Users.Single(x => x.Id == u.Id);
        return (await DeptMembership.PlaceAsync(sys, user, _kids.Id, code, DateTime.UtcNow, CancellationToken.None)).Error;
    }

    private int Memberships(AppUser u) { using var raw = InMemoryDb.Raw(_db); return raw.DeptRoles.Count(d => d.UserId == u.Id); }

    [Theory]
    [InlineData(IdentityRoles.Admin, "T1001")]
    [InlineData(IdentityRoles.Admin, "T1020")]
    [InlineData(IdentityRoles.SuperAdmin, "T1001")]
    public async Task R365_관리자_계정은_학생_새친구로_소속되지_않고_아무것도_바뀌지_않는다(string role, string code)
    {
        var a = Account("boss@example.com", role);
        Assert.Equal(MoveError.AdminNotStudent, await Place(a, code));
        Assert.Equal(0, Memberships(a));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.People.Where(p => p.UserId == a.Id));   // 명부 줄도 만들지 않는다
    }

    [Fact]
    public async Task R365_관리자_계정도_교사_어드민은_되고_일반_계정은_학생이_된다()
    {
        var a = Account("boss@example.com", IdentityRoles.Admin);
        Assert.Equal(MoveError.None, await Place(a, UserRole.TeacherCode));
        Assert.Equal(1, Memberships(a));

        var kid = Account("kid@example.com", null);
        Assert.Equal(MoveError.None, await Place(kid, UserRole.StudentCode));
        Assert.Equal(1, Memberships(kid));
    }

    [Fact]
    public async Task R365_가입_때_관리자_계정은_임시_부서에_학생으로_넣지_않는다()
    {
        var lobby = new LobbyService(new InMemorySystemDb(_db), FixedTime.SeoulMorning());
        var a = Account("boss@example.com", IdentityRoles.SuperAdmin);
        Assert.False(await lobby.EnrollAsync(a.UserName!, a.Id));
        Assert.Equal(0, Memberships(a));

        var kid = Account("kid@example.com", null);
        Assert.True(await lobby.EnrollAsync(kid.UserName!, kid.Id));
        Assert.Equal(1, Memberships(kid));
    }

    [Fact]
    public void R365_학생과_새친구만_막는다()
    {
        Assert.True(DeptMembership.StudentLike("T1001"));
        Assert.True(DeptMembership.StudentLike(" T1020 "));
        foreach (var c in new[] { "T1002", "T1005", "T1030", "T1099" }) Assert.False(DeptMembership.StudentLike(c));
    }
}
