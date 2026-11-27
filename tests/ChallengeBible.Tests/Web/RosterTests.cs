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
    private readonly Dictionary<string, (SeededMember M, string Pid, string UserId)> _p = [];

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
        Add(_kids, "cheolsu", "김철수", Roles.Participant);               // 학생 · 13점 · 오늘 함 · 사랑반
        Add(_kids, "harin", "강하린", Roles.Participant);                  // 학생 · 점수 없음 → 나오지 않는다
        Add(_kids, "dad", "이아빠", Roles.Participant, Roles.Guardian);     // 부모님 · 50점 · 가족팀(아빠)
        Add(_kids, "mom", "박엄마", Roles.Participant, Roles.Guardian);     // 부모님 · 40점 · 팀 없음 → 나오지 않는다
        Add(_kids, "eunhye", "이은혜", Roles.Participant, Roles.Teacher);   // 선생님 · 8점 · 사랑반 담당(팀 없이 혼자)
        Add(_kids, "park", "박선생", Roles.TenantAdmin);                   // 관리자 → 선생님 · 점수 없음
        Add(_elem, "jiho", "윤지호", Roles.Participant);                   // 다른 부서
        var req = InMemoryDb.SeedAccount(_db, "wait@example.com", "신청중").UserName!;   // 승인 전
        InMemoryDb.SeedMembership(_db, _kids.Id, req, MembershipStatus.Requested, Roles.Participant);
        Pts("cheolsu", (3, 5), (5, 8));
        Pts("dad", (4, 50));
        Pts("mom", (4, 40));
        Pts("eunhye", (2, 8));
        Pts("jiho", (5, 50));
        using var raw = InMemoryDb.Raw(_db);
        var team = new FamilyTeam { TenantId = _kids.Id, Name = "웃음폭탄네", CreatorUserName = _p["dad"].Pid };
        raw.FamilyTeams.Add(team);
        raw.SaveChanges();
        raw.FamilyMembers.Add(new FamilyMember { TenantId = _kids.Id, FamilyTeamId = team.Id, UserName = _p["dad"].Pid, Relation = FamilyRelation.Father });
        var term = new ClassTerm { TenantId = _kids.Id, Year = 2027, StartDate = Jan5, CreatedUtc = DateTime.UtcNow };
        raw.ClassTerms.Add(term);
        raw.SaveChanges();
        var love = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "사랑반", CreatedUtc = DateTime.UtcNow };
        raw.Classes.Add(love);
        var pk = new Person { TenantId = _kids.Id, Name = "김철수", UserId = _p["cheolsu"].UserId, CreatedUtc = DateTime.UtcNow };
        var pe = new Person { TenantId = _kids.Id, Name = "이은혜", UserId = _p["eunhye"].UserId, CreatedUtc = DateTime.UtcNow };
        raw.People.AddRange(pk, pe);
        raw.SaveChanges();
        raw.ClassMembers.AddRange(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = love.Id, PersonId = pk.Id, CreatedUtc = DateTime.UtcNow },
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = love.Id, PersonId = pe.Id, IsTeacher = true, CreatedUtc = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private void Add(Tenant t, string login, string name, params string[] roles)
    {
        var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name).UserName!;
        var m = InMemoryDb.SeedMembership(_db, t.Id, a, MembershipStatus.Approved, roles);
        using var raw = InMemoryDb.Raw(_db);
        raw.SaveChanges();
        _p[login] = (m, m.UserName, raw.Users.Single(u => u.UserName == a).Id);
    }

    private void Pts(string login, params (int Day, int Points)[] days)
    {
        using var raw = InMemoryDb.Raw(_db);
        foreach (var (day, pts) in days)
            raw.MissionActivities.Add(new MissionActivity { TenantId = _p[login].M.TenantId, UserName = _p[login].Pid, MissionDate = new DateOnly(2027, 1, day), MissionItem = "Word.Me", Points = pts, PerformedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private RosterService As(string login)
    {
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db))
            tc.Set(raw.Tenants.Single(t => t.Id == _kids.Id), raw.DeptRoles.Single(d => d.Id == _p[login].M.Id));
        var db = new InMemoryDb(_db, scope);
        return new RosterService(db, tc, new TenantClock(_time), new ClassService(db, new PeopleService(db, tc, _time), tc, _time));
    }

    [Fact]
    public async Task R214_점수가_1점_이상인_부서원만_나오고_부모님은_가족팀이_있을_때만_나온다()
    {
        var r = await As("cheolsu").ParticipantsAsync();
        Assert.Equal(["김철수", "이아빠", "이은혜"], r.People.Select(x => x.Name).OrderBy(n => n, StringComparer.Ordinal));   // 점수 없음 · 팀 없는 부모님 · 승인 전 · 다른 부서는 없다
        var kim = r.People.Single(x => x.Name == "김철수");
        Assert.Equal(("cheolsu", RankKind.Student, "사랑반", 13, true, true), (kim.UserName, kim.Kind, kim.Detail, kim.Points, kim.TodayDone, kim.Me));
        Assert.Equal(new DateOnly(2027, 1, 5), kim.LastDay);
        var dad = r.People.Single(x => x.Name == "이아빠");
        Assert.Equal((RankKind.Parent, "아빠", 50, false, new DateOnly(2027, 1, 4)), (dad.Kind, dad.Detail, dad.Points, dad.TodayDone, dad.LastDay));
        var eun = r.People.Single(x => x.Name == "이은혜");
        Assert.Equal((RankKind.Teacher, "사랑반 담당", 8), (eun.Kind, eun.Detail, eun.Points));   // 선생님은 팀 없이 혼자
        Assert.Equal(1, r.DoneToday);
        Assert.Equal(1, r.People.Count(x => x.Me));
    }

    [Fact]
    public async Task R214_순위를_꺼도_점수는_보인다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = 2, EffectiveFrom = Jan5,
                Values = RuleService.ToJson(RuleSet.Default with { Features = RuleSet.Default.Features with { Ranking = false } }), ChangedByUserName = "t" });
            raw.SaveChanges();
        }
        var r = await As("cheolsu").ParticipantsAsync();
        Assert.Equal([50, 13, 8], r.People.Select(x => x.Points).OrderByDescending(x => x));
    }

    // ---- 화면 ----

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string email, string name)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value,
            ["DisplayName"] = name, ["Email"] = email, ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        string id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Users.Single(a => a.Email == email).UserName!;
        InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, Roles.Participant);
        return c;
    }

    private static List<string> Names(string html) => Regex.Matches(html, "<div class=\"n\">([^<]+)").Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public async Task 화면_기본은_점수순_높은_점수_먼저이고_구분_숫자와_오늘_한_사람이_보인다()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연");
        var html = await c.GetStringAsync("/jamsil-kids/roster");
        Assert.Contains("<h1>참여자</h1>", html);
        Assert.Contains("오늘 미션을 한 사람 <b>1명</b> · 전체 3명", html);
        Assert.Equal(["이아빠", "김철수", "이은혜"], Names(html));
        Assert.Contains("전체 3</a>", html);
        Assert.Contains("학생 1</a>", html);
        Assert.Contains("부모님 1</a>", html);
        Assert.Contains("선생님 1</a>", html);
        Assert.Contains("<div class=\"i\">dad</div>", html);                    // 아이디(이메일이 아니다)
        Assert.DoesNotContain("dad@example.com", html);
        Assert.Contains("<b>부모님</b> · 아빠", html);
        Assert.Contains("<b>학생</b> · 사랑반", html);
        Assert.Contains("<b>선생님</b> · 사랑반 담당", html);
        Assert.Contains("마지막 오늘", html);
        Assert.Contains("마지막 어제", html);                                       // 1월 4일
        Assert.Contains("마지막 1월 2일", html);
        Assert.Contains(">오늘 완료<", html);
        Assert.Contains(">아직 안 함<", html);
        Assert.Contains("class=\"pt-pts num\">50점<", html);
        Assert.Contains("점수순 ↓", html);
        Assert.DoesNotContain("참여자 초대", html);                                 // 초대 버튼은 두지 않는다(2026-11-27 사용자)
        Assert.DoesNotContain("강하린", html);                                      // 점수 없음
        Assert.DoesNotContain("박엄마", html);                                      // 팀 없는 부모님
        Assert.DoesNotContain("윤지호", html);                                      // 다른 부서
    }

    [Fact]
    public async Task 화면_정렬은_수행일자순_이름순이_있고_같은_버튼을_다시_누르면_방향이_바뀐다()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연");
        var date = await c.GetStringAsync("/jamsil-kids/roster?s=date");
        Assert.Equal(["김철수", "이아빠", "이은혜"], Names(date));                  // 오늘 → 어제 → 이전
        Assert.Contains("수행일자순 ↓", date);
        var dateAsc = await c.GetStringAsync("/jamsil-kids/roster?s=date&o=asc");
        Assert.Equal(["이은혜", "이아빠", "김철수"], Names(dateAsc));
        Assert.Contains("수행일자순 ↑", dateAsc);
        var name = await c.GetStringAsync("/jamsil-kids/roster?s=name");
        Assert.Equal(["김철수", "이아빠", "이은혜"], Names(name));                  // 가나다 오름차순이 처음
        Assert.Contains("이름순 ↑", name);
        Assert.Equal(["이은혜", "이아빠", "김철수"], Names(await c.GetStringAsync("/jamsil-kids/roster?s=name&o=desc")));
        // 버튼 주소: 선택된 버튼은 방향 반전, 다른 버튼은 기본 방향으로
        var def = await c.GetStringAsync("/jamsil-kids/roster");
        Assert.Contains("href=\"/jamsil-kids/roster?o=asc\" aria-current=\"true\">점수순 ↓", def);
        Assert.Contains("href=\"/jamsil-kids/roster?s=date\" aria-current=\"false\">수행일자순", def);
        Assert.Contains("href=\"/jamsil-kids/roster?s=name\" aria-current=\"false\">이름순", def);
    }

    [Fact]
    public async Task 화면_구분_걸러보기와_이름_아이디_찾기는_정렬과_함께_동작한다()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연");
        Assert.Equal(["이아빠"], Names(await c.GetStringAsync("/jamsil-kids/roster?f=parent")));
        Assert.Equal(["이은혜"], Names(await c.GetStringAsync("/jamsil-kids/roster?f=teacher")));
        Assert.Equal(["이아빠"], Names(await c.GetStringAsync("/jamsil-kids/roster?q=dad")));                 // 아이디로 찾기
        Assert.Equal(["김철수"], Names(await c.GetStringAsync("/jamsil-kids/roster?q=%EC%B2%A0%EC%88%98")));   // 이름으로 찾기
        var both = await c.GetStringAsync("/jamsil-kids/roster?f=teacher&s=name");
        Assert.Contains("f=teacher&amp;s=name&amp;o=desc", both);                                              // 정렬 버튼 주소가 구분을 유지
        Assert.Contains("찾는 사람이 없어요", await c.GetStringAsync("/jamsil-kids/roster?q=%EC%97%86%EB%8A%94%EC%82%AC%EB%9E%8C"));
    }

    [Fact]
    public async Task 화면_임시_부서에서는_참여자_화면이_없다()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/welcome/roster")).StatusCode);
    }

    [Fact]
    public async Task 화면_순위_화면은_제목이_순위이고_숫자는_점수_있는_참여자라고_쓴다()
    {
        var c = await LoggedIn("seoyeon@example.com", "박서연");
        var html = await c.GetStringAsync("/jamsil-kids/ranking");
        Assert.Contains("<h1>순위</h1>", html);
        Assert.Contains("<small>점수 있는 참여자</small>", html);
    }
}
