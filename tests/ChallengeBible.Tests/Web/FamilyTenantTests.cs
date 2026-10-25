using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 가족과 함께할 부서(R-168, 0046, 확정 시안: 가족부서고르기). 김영희 = 유년부(아들 김철수, 참여자 · 가족팀) · 초등부(딸 김민지) 보호자.
/// </summary>
public partial class FamilyTenantTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly int _mom, _momKidsPid, _cheolsuPid;

    public FamilyTenantTests(WebApplicationFactory<Program> app)
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
        _mom = InMemoryDb.SeedAccount(_db, "younghee@example.com", "김영희").Id;
        var momKids = InMemoryDb.SeedMembership(_db, _kids.Id, _mom, MembershipStatus.Approved, Roles.Participant, Roles.Guardian);
        var momElem = InMemoryDb.SeedMembership(_db, _elem.Id, _mom, MembershipStatus.Approved, Roles.Guardian);
        var cheolsu = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").Id, MembershipStatus.Approved, Roles.Participant);
        var minji = InMemoryDb.SeedMembership(_db, _elem.Id, InMemoryDb.SeedAccount(_db, "minji@example.com", "김민지").Id, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        var pMom = new Participant { TenantId = _kids.Id, MembershipId = momKids.Id };
        var pCheolsu = new Participant { TenantId = _kids.Id, MembershipId = cheolsu.Id };
        var pMinji = new Participant { TenantId = _elem.Id, MembershipId = minji.Id };
        raw.Participants.AddRange(pMom, pCheolsu, pMinji);
        raw.SaveChanges();
        (_momKidsPid, _cheolsuPid) = (pMom.Id, pCheolsu.Id);
        raw.GuardianLinks.AddRange(
            new GuardianLink { TenantId = _kids.Id, GuardianMembershipId = momKids.Id, ParticipantId = pCheolsu.Id, CreatedByAccountId = _mom },
            new GuardianLink { TenantId = _elem.Id, GuardianMembershipId = momElem.Id, ParticipantId = pMinji.Id, CreatedByAccountId = _mom });
        raw.FamilyTeams.Add(new FamilyTeam { TenantId = _kids.Id, Members =
        [
            new FamilyMember { TenantId = _kids.Id, ParticipantId = pCheolsu.Id, Relation = FamilyRelation.Student, SortOrder = 0 },
            new FamilyMember { TenantId = _kids.Id, ParticipantId = pMom.Id, Relation = FamilyRelation.Mother, SortOrder = 1 },
        ] });
        raw.PointLedgers.AddRange(
            new PointLedger { TenantId = _kids.Id, ParticipantId = pMom.Id, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = 22 },
            new PointLedger { TenantId = _kids.Id, ParticipantId = pCheolsu.Id, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = 42 });
        raw.SaveChanges();
    }

    private FamilyTenantService Svc() => new(new InMemorySystemDb(_db), _time);

    [Fact]
    public async Task R168_R179_다른_부서로_바꾸면_이전_부서_참여와_가족팀_자리만_멈추고_점수는_남는다()
    {
        Assert.True(await Svc().ChooseAsync(_mom, _mom, _elem.Id));
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal(_elem.Id, raw.Accounts.Single(a => a.Id == _mom).FamilyTenantId);
            var m = raw.Memberships.Single(x => x.TenantId == _kids.Id && x.AccountId == _mom);
            Assert.Equal([Roles.Guardian], raw.MembershipRoles.Where(r => r.MembershipId == m.Id).Select(r => r.Role));   // 보호자는 그대로
            Assert.False(raw.Participants.Single(p => p.Id == _momKidsPid).IsActive);
            Assert.False(raw.FamilyMembers.Single(f => f.ParticipantId == _momKidsPid).IsActive);
            Assert.True(raw.FamilyMembers.Single(f => f.ParticipantId == _cheolsuPid).IsActive);           // 철수는 팀에 그대로
            Assert.Equal(2, raw.PointLedgers.Count());                                                       // 점수 · 기록은 남음
            var momElem = raw.Memberships.Single(x => x.TenantId == _elem.Id && x.AccountId == _mom).Id;
            Assert.False(raw.MembershipRoles.Any(r => r.MembershipId == momElem && r.Role == Roles.Participant)); // 새 부서 참여자는 선생님이 준다
            Assert.Equal(FamilyTenantService.SetAction, raw.AuditLogs.Single().Action);
        }
        Assert.True(await Svc().ChooseAsync(_mom, _mom, _elem.Id));   // 같은 부서 다시 → 그대로
        Assert.Single(InMemoryDb.Raw(_db).AuditLogs);
        Assert.False(await Svc().ChooseAsync(_mom, _mom, InMemoryDb.SeedTenant(_db, "other", "OTHERX").Id));   // 소속이 없는 부서
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string email)
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var a = raw.Accounts.Single(x => x.Email == email);
            a.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Account>().HashPassword(a, "1234");
            raw.SaveChanges();
        }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var h = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(h).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    [Fact]
    public async Task R179_부모는_시상_부서를_보기만_하고_정하기는_플랫폼_관리자()
    {
        var c = await LoggedIn("younghee@example.com");
        Assert.Contains("시상 부서가 아직 정해지지 않았어요", await c.GetStringAsync("/jamsil-kids"));     // 부서 홈 안내(고르기 버튼 없음)
        Assert.Contains("플랫폼 관리자가 정해요", await c.GetStringAsync("/account/tenants"));

        var page = await c.GetStringAsync("/account/family");                                              // 이제 온가족 화면(0065)
        Assert.Contains("내 시상 부서", page);
        Assert.DoesNotContain("name=\"pick\"", page);                                                       // 부모가 고르는 칸 없음

        Assert.True(await Svc().ChooseAsync(_mom, _mom, _elem.Id));                                         // (플랫폼 관리자 처리기가 부르는 것과 같은 일)
        Assert.DoesNotContain("시상 부서가 아직 정해지지 않았어요", await c.GetStringAsync("/jamsil-elem"));
        Assert.Contains("시상 부서는", await c.GetStringAsync("/account/tenants"));

        // 보호자가 아니어도 온가족 화면은 열리고 시상 부서 칸은 없다
        var kid = await LoggedIn("cheolsu@example.com");
        var kidPage = await kid.GetStringAsync("/account/family");
        Assert.Contains("온가족이 아직 없어요", kidPage);
        Assert.DoesNotContain("내 시상 부서", kidPage);
    }
}
