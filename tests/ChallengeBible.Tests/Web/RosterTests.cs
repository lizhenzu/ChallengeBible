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

/// <summary>부서 명단(ADR 0085, R-214 · R-215, 확정 시안: dept-roster). 오늘 = 2027-01-05 화요일(서울).</summary>
public partial class RosterTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly Dictionary<string, (SeededMember M, int? Pid)> _p = [];

    public RosterTests(WebApplicationFactory<Program> app)
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
        Add(_kids, "cheolsu", "김철수", [Roles.Participant], true, "사랑반");
        Add(_kids, "harin", "강하린", [Roles.Participant], true);                // 점수 없음
        Add(_kids, "younghee", "김영희", [Roles.Guardian], false);                // 부모님 · 참여자 아님
        Add(_kids, "eunhye", "이은혜", [Roles.Participant, Roles.Teacher], true);  // 선생님 · 참여자
        Add(_kids, "park", "박선생", [Roles.TenantAdmin], false);                 // 관리자 → 선생님 · 참여자 아님
        Add(_kids, "gone", "그만둔", [Roles.Participant], false, active: true);   // 참여자 자리가 멈춤(IsActive = false)
        Add(_elem, "jiho", "윤지호", [Roles.Participant], true);                  // 다른 부서
        var req = InMemoryDb.SeedAccount(_db, "wait@example.com", "신청중").UserName!;   // 승인 전
        InMemoryDb.SeedMembership(_db, _kids.Id, req, MembershipStatus.Requested, Roles.Participant);
        Pts("cheolsu", (3, 5), (5, 7));
        Pts("eunhye", (4, 2));
        Pts("jiho", (5, 50));
        Done("cheolsu");
    }

    private void Add(Tenant t, string login, string name, string[] roles, bool participant, string? group = null, bool active = false)
    {
        var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name).UserName!;
        var m = InMemoryDb.SeedMembership(_db, t.Id, a, MembershipStatus.Approved, roles);
        using var raw = InMemoryDb.Raw(_db);
        int? pid = null;
        if (participant || active)
        {
            raw.SaveChanges();
            pid = m.UserName;
        }
        raw.SaveChanges();
        _p[login] = (m, pid);
    }

    private void Pts(string login, params (int Day, int Points)[] days)
    {
        using var raw = InMemoryDb.Raw(_db);
        var p = raw.DeptRoles.Single(x => x.Id == _p[login].Pid);
        foreach (var (day, pts) in days)
            raw.MissionActivities.Add(new MissionActivity { TenantId = p.TenantId, UserName = p.Id, MissionDate = new DateOnly(2027, 1, day), MissionItem = "Word.Me", Points = pts, PerformedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private void Done(string login)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _p[login].Pid!.Value, MissionItem = "Word.Me", MissionDate = Jan5, Points = 1, PerformedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private RosterService As(string login)
    {
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db))
            tc.Set(raw.Tenants.Single(t => t.Id == _kids.Id), new Membership { Id = _p[login].M.Id, Status = MembershipStatus.Approved, Roles = [new MembershipRole { Role = Roles.Participant }] });
        return new RosterService(new InMemoryDb(_db, scope), tc, new TenantClock(_time), InMemoryDb.Rules(_db));
    }

    [Fact]
    public async Task R214_전체는_이_부서_승인_소속원_모두_이름순이고_이메일과_구분을_싣는다()
    {
        var r = await As("cheolsu").GetAsync();
        Assert.Equal(["강하린", "그만둔", "김영희", "김철수", "박선생", "이은혜"], r.People.Select(x => x.Name));   // 승인 전 · 다른 부서는 없다
        Assert.Equal(6, r.AllCount);
        var kim = r.People.Single(x => x.Name == "김철수");
        Assert.Equal(("cheolsu@example.com", "사랑반", RankKind.Student, true), (kim.Email, kim.GroupLabel, kim.Kind, kim.Me));
        Assert.Equal(RankKind.Parent, r.People.Single(x => x.Name == "김영희").Kind);
        Assert.Equal(RankKind.Teacher, r.People.Single(x => x.Name == "박선생").Kind);     // 관리자도 선생님
        Assert.Equal(RankKind.Teacher, r.People.Single(x => x.Name == "이은혜").Kind);
        Assert.Equal(1, r.People.Count(x => x.Me));
    }

    [Fact]
    public async Task R214_참여자는_활성_참여자만이고_점수가_없어도_들어_있다()
    {
        var r = await As("cheolsu").GetAsync();
        var people = r.People.Where(x => x.IsParticipant).ToList();
        Assert.Equal(["강하린", "김철수", "이은혜"], people.Select(x => x.Name));        // 멈춘 참여자 · 참여자가 아닌 부모님 · 관리자는 없다
        Assert.Equal(3, r.ParticipantCount);
        Assert.Equal([0, 12, 2], people.Select(x => x.Points));
        Assert.Equal(["김철수"], people.Where(x => x.TodayDone).Select(x => x.Name));     // 오늘 미션을 한 사람
    }

    [Fact]
    public async Task R214_기본_규칙에서_순위를_끄면_점수는_싣지_않는다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = 2, EffectiveFrom = Jan5,
                Values = RuleService.ToJson(RuleSet.Default with { Features = RuleSet.Default.Features with { Ranking = false } }), ChangedByUserName = "t" });
            raw.SaveChanges();
        }
        var r = await As("cheolsu").GetAsync();
        Assert.False(r.ShowPoints);
        Assert.All(r.People, x => Assert.Equal(0, x.Points));
        Assert.Equal(6, r.AllCount);
    }

    // ---- 화면 ----

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string email, string name, bool participant)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value,
            ["DisplayName"] = name, ["Email"] = email, ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        int id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Users.Single(a => a.Email == email).UserName!;
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, participant ? [Roles.Participant] : [Roles.Guardian]);
        if (participant)
        {
            using var raw = InMemoryDb.Raw(_db);
            raw.SaveChanges();
        }
        return c;
    }

    [Fact]
    public async Task 화면_홈_카드의_숫자는_명단의_숫자와_같고_눌러_명단으로_간다()
    {
        ContentSeed.Word(_db, Jan5);
        var c = await LoggedIn("seoyeon@example.com", "박서연", true);   // 소속원 7 · 참여자 4
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Matches(@"<a class=""b"" href=""/jamsil-kids/roster""><b class=""num"">7<u>명</u></b><small>전체</small>", home);
        Assert.Matches(@"<a class=""pn"" href=""/jamsil-kids/roster\?t=participant""><b class=""num"">4<u>명</u></b><small>참여자</small>", home);
        Assert.Matches(@"<b class=""num"">1<u>명</u></b><small>오늘 참여</small>", home);

        var all = await c.GetStringAsync("/jamsil-kids/roster");
        Assert.Matches(@"전체<em class=""num"">7</em>", all);
        Assert.Matches(@"참여자<em class=""num"">4</em>", all);
        var part = await c.GetStringAsync("/jamsil-kids/roster?t=participant");
        Assert.Equal(4, Regex.Matches(part, "class=\"rst-row").Count);
    }

    [Fact]
    public async Task 화면_전체_명단은_구분별로_이름_아래에_이메일_참여자_표시()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연", true);
        var html = await c.GetStringAsync("/jamsil-kids/roster");
        Assert.Contains("<h1>명단</h1>", html);
        Assert.Matches(@"학생 <span class=""num"">\d+</span>", html);
        Assert.Matches(@"부모님 <span class=""num"">1</span>", html);
        Assert.Matches(@"선생님 <span class=""num"">2</span>", html);
        Assert.Contains("<small>cheolsu@example.com · 사랑반</small>", html);          // 이름 아래 이메일 · 반
        Assert.Contains("<small>younghee@example.com</small>", html);
        Assert.Matches(@"박서연</b>\s*<em>나</em>", html);
        Assert.DoesNotContain("jiho@example.com", html);                                // 다른 부서
        Assert.DoesNotContain("wait@example.com", html);                                // 승인 전
        Assert.DoesNotContain("점수 있는 분", html);                                      // 전체 탭엔 점수 구역이 없다
        Assert.Matches(@"<span class=""tag"">참여자</span>", html);
    }

    [Fact]
    public async Task 화면_참여자_명단은_점수_있는_분과_없는_분으로_나누고_순위로_안내한다()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연", true);
        var html = await c.GetStringAsync("/jamsil-kids/roster?t=participant");
        Assert.Matches(@"점수 있는 분 <span class=""num"">2</span>", html);              // 김철수 12 · 이은혜 2
        Assert.Matches(@"아직 점수가 없는 분 <span class=""num"">2</span>", html);       // 강하린 · 박서연
        Assert.True(html.IndexOf("김철수", StringComparison.Ordinal) < html.IndexOf("이은혜", StringComparison.Ordinal));   // 점수 높은 순
        Assert.Contains("12점", html);
        Assert.Contains("오늘 참여", html);
        Assert.Contains("<small>harin@example.com</small>", html);
        Assert.DoesNotContain("younghee@example.com", html);                            // 참여자가 아닌 부모님
        Assert.DoesNotContain("gone@example.com", html);                                // 멈춘 참여자
        Assert.Contains("href=\"/jamsil-kids/ranking\"", html);
    }

    [Fact]
    public async Task 화면_구분과_찾기는_이름과_이메일로_걸러진다()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연", true);
        var teachers = await c.GetStringAsync("/jamsil-kids/roster?f=teacher");
        Assert.Contains("박선생", teachers);
        Assert.Contains("이은혜", teachers);
        Assert.DoesNotContain("강하린", teachers);
        var byMail = await c.GetStringAsync("/jamsil-kids/roster?q=younghee%40");
        Assert.Contains("김영희", byMail);
        Assert.DoesNotContain("강하린", byMail);
        Assert.Contains("찾는 사람이 없어요", await c.GetStringAsync("/jamsil-kids/roster?q=%EC%97%86%EB%8A%94%EC%82%AC%EB%9E%8C"));
    }

    [Fact]
    public async Task 화면_순위_화면은_제목이_순위이고_숫자는_점수_있는_참여자라고_쓴다()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연", true);
        var html = await c.GetStringAsync("/jamsil-kids/ranking");
        Assert.Contains("<h1>순위</h1>", html);
        Assert.Contains("<small>점수 있는 참여자</small>", html);
    }

    [Fact]
    public async Task 화면_임시_부서에서는_명단이_없다()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연", true);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/welcome/roster")).StatusCode);
    }
}
