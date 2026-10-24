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

    private FamilyTenantService Svc() => new(new InMemorySystemDb(_db), new TenantClock(_time), _time);

    [Fact]
    public async Task 보호자에게_내가_보호자인_부서마다_자녀_가족팀_내_점수를_보여_준다()
    {
        var c = await Svc().GetAsync(_mom);
        Assert.NotNull(c);
        Assert.Null(c.Current);
        var kids = c.Options.Single(o => o.TenantId == _kids.Id);
        Assert.Equal(["김철수"], kids.Children);
        Assert.Equal(("김철수 가족", 64, true, 22), (kids.TeamName, kids.TeamPoints, kids.IAmParticipant, kids.MyPoints));
        var elem = c.Options.Single(o => o.TenantId == _elem.Id);
        Assert.Equal(["김민지"], elem.Children);
        Assert.Equal(((string?)null, false), (elem.TeamName, elem.IAmParticipant));
        Assert.Equal([_kids.Id], c.StopsIf(_elem.Id).Select(s => s.TenantId));   // 초등부를 고르면 유년부가 멈춘다
        Assert.Empty(c.StopsIf(_kids.Id));

        var cheolsuAccount = InMemoryDb.Raw(_db).Accounts.Single(a => a.Email == "cheolsu@example.com").Id;
        Assert.Null(await Svc().GetAsync(cheolsuAccount));   // 보호자가 아니면 없음
    }

    [Fact]
    public async Task R168_다른_부서로_바꾸면_이전_부서_참여와_가족팀_자리만_멈추고_점수는_남는다()
    {
        Assert.True(await Svc().ChooseAsync(_mom, _elem.Id));
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
            Assert.Equal("FamilyTenant", raw.AuditLogs.Single().Action);
        }
        Assert.True(await Svc().ChooseAsync(_mom, _elem.Id));   // 같은 부서 다시 → 그대로
        Assert.Single(InMemoryDb.Raw(_db).AuditLogs);
        Assert.False(await Svc().ChooseAsync(_mom, InMemoryDb.SeedTenant(_db, "other", "OTHERX").Id));   // 보호자가 아닌 부서
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

    private static Task<HttpResponseMessage> Post(HttpClient c, string token, params (string, string)[] form) =>
        c.PostAsync("/account/family", new FormUrlEncodedContent(form.Append(("__RequestVerificationToken", token)).Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))));

    [Fact]
    public async Task 화면_흐름_안내_바뀌는_점_확인_뒤에_바꾼다()
    {
        var c = await LoggedIn("younghee@example.com");
        Assert.Contains("가족과 함께할 부서를 골라 주세요", await c.GetStringAsync("/jamsil-kids"));          // 부서 홈 안내(0046)
        Assert.Contains("href=\"/account/family\"", await c.GetStringAsync("/account/tenants"));

        var page = await c.GetStringAsync("/account/family");
        Assert.Contains("김민지", page);
        Assert.Contains("김철수 가족 · 64점", page);
        var token = TokenRx().Match(page).Groups[1].Value;

        // 확인 없이 보내면 바뀌는 점을 보여 주고 바꾸지 않는다
        var ask = await (await Post(c, token, ("pick", _elem.Id.ToString()))).Content.ReadAsStringAsync();
        Assert.Contains("이렇게 돼요", ask);
        Assert.Contains("김철수 가족 자리", ask);
        Assert.Null(InMemoryDb.Raw(_db).Accounts.Single(a => a.Id == _mom).FamilyTenantId);

        var done = await Post(c, token, ("pick", _elem.Id.ToString()), ("agree", "true"));
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Contains("선생님이 참여자 역할을 주면 미션과 가족팀을 시작할 수 있어요", await c.GetStringAsync(done.Headers.Location!.OriginalString));
        Assert.Equal(_elem.Id, InMemoryDb.Raw(_db).Accounts.Single(a => a.Id == _mom).FamilyTenantId);
        Assert.DoesNotContain("가족과 함께할 부서를 골라 주세요", await c.GetStringAsync("/jamsil-elem"));

        // 보호자가 아니면 부서 고르기로
        var kid = await LoggedIn("cheolsu@example.com");
        Assert.Equal("/account/tenants", (await kid.GetAsync("/account/family")).Headers.Location!.OriginalString);
    }
}
