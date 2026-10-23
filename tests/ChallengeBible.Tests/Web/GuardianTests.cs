using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>보호자 화면과 보호 관계(R-132). 오늘 = 2027-01-05.</summary>
public partial class GuardianTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids;
    private readonly Account _admin;

    public GuardianTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(_time);
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _admin = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, _admin.Id, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        ContentSeed.Word(_db, Jan5);
        ContentSeed.ChoiceQuiz(_db, Jan5);
    }

    private (TenantContext Ctx, RequestScope Scope) In(Membership? me)
    {
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        var ctx = new TenantContext();
        Membership? loaded = null;
        if (me is not null) { using var raw = InMemoryDb.Raw(_db); loaded = raw.Memberships.Where(m => m.Id == me.Id).Select(m => new Membership { Id = m.Id, TenantId = m.TenantId, AccountId = m.AccountId, Status = m.Status, Roles = m.Roles }).Single(); }
        ctx.Set(_kids, loaded);
        return (ctx, scope);
    }

    private (Membership Kid, int Pid) Kid(string login, string name)
    {
        var a = InMemoryDb.SeedAccount(_db, login, name);
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, a.Id, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        var p = new Participant { TenantId = _kids.Id, MembershipId = m.Id };
        raw.Participants.Add(p);
        raw.SaveChanges();
        return (m, p.Id);
    }

    private Membership Parent(string login, string name)
    {
        var a = InMemoryDb.SeedAccount(_db, login, name);
        return InMemoryDb.SeedMembership(_db, _kids.Id, a.Id, MembershipStatus.Approved, Roles.Guardian);
    }

    private MemberAdminService Admin()
    {
        var (ctx, scope) = In(null);
        return new MemberAdminService(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), ctx, _time);
    }

    [Fact]
    public async Task R132_보호자는_연결된_자녀만_본다()
    {
        var (cheolsu, pid) = Kid("cheolsu", "김철수");
        var (_, otherPid) = Kid("minjun", "최민준");
        var mom = Parent("younghee", "김영희");
        Assert.Equal(MemberError.None, await Admin().LinkGuardianAsync(_admin.Id, mom.Id, cheolsu.Id));

        var (ctx, scope) = In(mom);
        var svc = new GuardianService(new InMemoryDb(_db, scope), ctx);
        Assert.Equal(["김철수"], (await svc.ChildrenAsync()).Select(c => c.Name));
        Assert.True(await svc.IsMyChildAsync(pid));
        Assert.False(await svc.IsMyChildAsync(otherPid));
        Assert.Empty(await svc.DayLogAsync(otherPid, Jan5));   // 연결 안 된 아이 기록은 안 준다
    }

    [Fact]
    public async Task 그날_한_일에는_퀴즈_오답도_0점으로_보인다()
    {
        var (cheolsu, pid) = Kid("cheolsu", "김철수");
        var mom = Parent("younghee", "김영희");
        await Admin().LinkGuardianAsync(_admin.Id, mom.Id, cheolsu.Id);

        var (kctx, kscope) = In(cheolsu);
        var missions = new MissionService(new InMemoryDb(_db, kscope), kctx, new TenantClock(_time), _time);
        await missions.PressAsync(pid, MissionItem.WordMe, Jan5);
        int wrong;
        using (var raw = InMemoryDb.Raw(_db)) wrong = raw.DailyQuizChoices.First(c => c.Id != raw.DailyQuizzes.Single().CorrectChoiceId).Id;
        await missions.AnswerQuizAsync(pid, Jan5, wrong, null);

        var (ctx, scope) = In(mom);
        var log = await new GuardianService(new InMemoryDb(_db, scope), ctx).DayLogAsync(pid, Jan5);
        Assert.Contains(log, l => l.What == "혼자 읽었어요" && l.Points == 1);
        Assert.Contains(log, l => l.What == "오답 · 1번째 도전" && l.Points == 0);
    }

    [Fact]
    public async Task 보호자_연결은_보호자_역할이_있어야_하고_풀_수_있다()
    {
        var (cheolsu, _) = Kid("cheolsu", "김철수");
        var (notParent, _) = Kid("minjun", "최민준");
        Assert.Equal(MemberError.NotFound, await Admin().LinkGuardianAsync(_admin.Id, notParent.Id, cheolsu.Id));
        var mom = Parent("younghee", "김영희");
        await Admin().LinkGuardianAsync(_admin.Id, mom.Id, cheolsu.Id);
        Assert.Equal([mom.Id], (await Admin().LinksAsync())[cheolsu.Id]);
        Assert.Equal(MemberError.None, await Admin().UnlinkGuardianAsync(mom.Id, cheolsu.Id));
        Assert.False((await Admin().LinksAsync()).ContainsKey(cheolsu.Id));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string login)
    {
        // 계정은 이미 있으니 비밀번호만 정하고 로그인
        using (var raw = InMemoryDb.Raw(_db))
        {
            var a = raw.Accounts.Single(x => x.Email == login);
            a.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Account>().HashPassword(a, "1234");
            raw.SaveChanges();
        }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var h = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(h).Groups[1].Value, ["Email"] = login, ["Password"] = "1234",
        }));
        return c;
    }

    [Fact]
    public async Task 보호자_화면_흐름()
    {
        var (cheolsu, _) = Kid("cheolsu", "김철수");
        var (_, otherPid) = Kid("minjun", "최민준");
        var mom = Parent("younghee", "김영희");

        var c = await LoggedIn("younghee");
        Assert.Contains("아직 연결된 자녀가 없어요", await c.GetStringAsync("/jamsil-kids/guardian"));

        await Admin().LinkGuardianAsync(_admin.Id, mom.Id, cheolsu.Id);
        var html = await c.GetStringAsync("/jamsil-kids/guardian");
        Assert.Contains("김철수", html);
        Assert.Contains("오늘은 아직 미션을 하지 않았어요", html);
        Assert.Contains("자녀 미션 보기", await c.GetStringAsync("/jamsil-kids"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/jamsil-kids/guardian?child={otherPid}")).StatusCode);   // R-132

        var kid = await LoggedIn("cheolsu");
        Assert.StartsWith("/account/denied", (await kid.GetAsync("/jamsil-kids/guardian")).Headers.Location!.PathAndQuery);
    }
}
