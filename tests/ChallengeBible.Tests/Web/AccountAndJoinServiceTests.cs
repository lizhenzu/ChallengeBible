using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Web;

public class AccountAndJoinServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private static readonly TimeProvider Time = TimeProvider.System;

    private AccountService Accounts(RequestScope? scope = null) => new(new InMemoryDb(_db, scope ?? new RequestScope()), Time);
    private (JoinService Join, RequestScope Scope) Join()
    {
        var scope = new RequestScope();
        return (new JoinService(new InMemoryDb(_db, scope), scope, Time), scope);
    }

    private async Task<Account> SignUp(string id = "cheolsu@example.com", string name = "김철수", string pw = "1234")
    {
        var (a, e) = await Accounts().SignUpAsync(new SignUpInput(name, id, pw, pw));
        Assert.True(e.Ok, string.Join(",", e.Values));
        return a!;
    }

    [Fact]
    public async Task R137_가입은_이름_이메일_비밀번호만()
    {
        var a = await SignUp(id: "CheolSu@Example.com");
        Assert.Equal("cheolsu@example.com", a.Email);   // 이메일은 소문자로
        Assert.NotEqual("1234", a.PasswordHash);   // 비밀번호는 해시로만
        Assert.False(a.IsPlatformAdmin);
    }

    [Theory]
    [InlineData("", "cheolsu@example.com", "1234", "1234", "DisplayName")]
    [InlineData("철수", "cheolsu", "1234", "1234", "Email")]   // @ 없는 옛 아이디는 이메일로 못 쓴다(ADR 0050)
    [InlineData("철수", "ab@", "1234", "1234", "Email")]
    [InlineData("철수", "cheol su@example.com", "1234", "1234", "Email")]
    [InlineData("철수", "cheolsu@example.com", "123", "123", "Password")]
    [InlineData("철수", "cheolsu@example.com", "1234", "4321", "PasswordConfirm")]
    public async Task 가입_입력_검사(string name, string id, string pw, string pw2, string field)
    {
        var (a, e) = await Accounts().SignUpAsync(new SignUpInput(name, id, pw, pw2));
        Assert.Null(a);
        Assert.True(e.ContainsKey(field));
    }

    [Fact]
    public async Task 같은_이메일은_가입할_수_없다()
    {
        await SignUp();
        var (a, e) = await Accounts().SignUpAsync(new SignUpInput("다른 철수", "cheolsu@example.com", "5678", "5678"));
        Assert.Null(a);
        Assert.Contains("이미", e["Email"]);
    }

    [Fact]
    public async Task 로그인은_비밀번호가_맞아야()
    {
        await SignUp();
        Assert.NotNull((await Accounts().LoginAsync("CHEOLSU@EXAMPLE.COM", "1234")).Account);
        var wrong = await Accounts().LoginAsync("cheolsu@example.com", "0000");
        Assert.Null(wrong.Account);
        Assert.Equal("이메일 또는 비밀번호가 맞지 않아요. 다시 확인해 주세요.", wrong.Error);
        // 없는 이메일도 같은 문구(이메일이 있는지 알려 주지 않음)
        Assert.Equal(wrong.Error, (await Accounts().LoginAsync("nobody@example.com", "1234")).Error);
    }

    [Fact]
    public async Task 참여_코드로_찾기_정지된_부서는_없음과_같다_0048()
    {
        InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        InMemoryDb.SeedTenant(_db, "saebit-summer", "SBSUM6", TenantStatus.Suspended);
        var (join, _) = Join();
        Assert.Equal("잠실새내교회 유년부", (await join.LookupAsync(" jskids "))!.Name);
        Assert.Null(await join.LookupAsync("SBSUM6"));
        Assert.Null(await join.LookupAsync("NOPE00"));
    }

    [Fact]
    public async Task 신청하면_그_부서에_신청_상태_소속이_생기고_로그인_후_갈곳은_참여신청_0045()
    {
        var me = await SignUp();
        var t = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var (join, scope) = Join();

        var (err, id) = await join.ApplyAsync(me.Id, "JSKIDS", [Roles.Participant, Roles.Guardian]);

        Assert.Equal(JoinError.None, err);
        Assert.Equal(t.Id, scope.TenantId);
        using var raw = InMemoryDb.Raw(_db);
        var m = raw.Memberships.Include(x => x.Roles).Single(x => x.Id == id);
        Assert.Equal(MembershipStatus.Requested, m.Status);
        Assert.Equal(t.Id, m.TenantId);
        Assert.Equal([Roles.Guardian, Roles.Participant], m.Roles.Select(r => r.Role).Order());
        Assert.Equal(0, (await Accounts().WhereToGoAsync(me.Id)).ApprovedCount);   // 승인 전이라 0곳
    }

    [Fact]
    public async Task 교사_관리자는_신청할_수_없다()
    {
        var me = await SignUp();
        InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var (join, _) = Join();
        Assert.Equal(JoinError.BadRole, (await join.ApplyAsync(me.Id, "JSKIDS", [Roles.TenantAdmin])).Error);
        Assert.Equal(JoinError.NoRole, (await join.ApplyAsync(me.Id, "JSKIDS", [])).Error);
    }

    [Fact]
    public async Task 같은_부서에_두_번_신청할_수_없다()
    {
        var me = await SignUp();
        InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        Assert.Equal(JoinError.None, (await Join().Join.ApplyAsync(me.Id, "JSKIDS", [Roles.Participant])).Error);
        Assert.Equal(JoinError.AlreadyMember, (await Join().Join.ApplyAsync(me.Id, "JSKIDS", [Roles.Participant])).Error);
    }

    [Fact]
    public async Task 신청_취소는_신청_상태일_때만()
    {
        var me = await SignUp();
        var t = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        await Join().Join.ApplyAsync(me.Id, "JSKIDS", [Roles.Participant]);
        Assert.True(await Join().Join.CancelAsync(me.Id, t.Id));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.Memberships);
        Assert.Empty(raw.MembershipRoles);
    }

    [Fact]
    public async Task 로그인_후_갈곳은_승인된_소속_수로_0045()
    {
        var me = await SignUp();
        var a = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var b = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.Memberships.Add(new Membership { TenantId = a.Id, AccountId = me.Id, Status = MembershipStatus.Approved });
            raw.SaveChanges();
        }
        var go = await Accounts().WhereToGoAsync(me.Id);
        Assert.Equal((1, "jamsil-kids"), (go.ApprovedCount, go.OnlyTenantSlug));

        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.Memberships.Add(new Membership { TenantId = b.Id, AccountId = me.Id, Status = MembershipStatus.Approved });
            raw.SaveChanges();
        }
        go = await Accounts().WhereToGoAsync(me.Id);
        Assert.Equal((2, (string?)null), (go.ApprovedCount, go.OnlyTenantSlug));
    }

    [Fact]
    public async Task 임시_비밀번호_뒤에는_새_비밀번호로_바꾼다_0035()
    {
        var me = await SignUp();
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.Accounts.Single(x => x.Id == me.Id).MustChangePassword = true;
            raw.SaveChanges();
        }
        Assert.True(await Accounts().MustChangePasswordAsync(me.Id));
        Assert.True((await Accounts().ChangePasswordAsync(me.Id, "5678", "5678")).Ok);
        Assert.False(await Accounts().MustChangePasswordAsync(me.Id));
        Assert.NotNull((await Accounts().LoginAsync("cheolsu@example.com", "5678")).Account);
    }
}
