using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>임시 부서 "처음 오신 분"(ADR 0064, R-171 ~ R-174, 확정 시안 임시부서-내부서찾기 ㄱ안). 오늘 = 2027-01-05 (서울).</summary>
public partial class LobbyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan4 = new(2027, 1, 4), Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _lobby;

    public LobbyTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _lobby = InMemoryDb.SeedTenant(_db, "welcome", "WELCOM");
        using (var raw = InMemoryDb.Raw(_db))
        {
            var t = raw.Tenants.Single(x => x.Id == _lobby.Id);
            (t.Name, t.IsLobby, t.ReadingBoardPublic) = ("처음 오신 분", true, false);
            raw.SaveChanges();
        }
        foreach (var d in new[] { Jan4, Jan5 }) { ContentSeed.Word(_db, d); ContentSeed.ChoiceQuiz(_db, d); }
        ContentSeed.Bible(_db);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<(HttpClient C, HttpResponseMessage Signup)> SignUp(string email = "cheolsu")
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        var r = await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김철수", ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        return (c, r);
    }

    private int AccountId(string email = "cheolsu") { using var raw = InMemoryDb.Raw(_db); return raw.Accounts.Single(a => a.Email == email + "@example.com").Id; }

    private async Task PressWord(HttpClient c, DateOnly d)
    {
        var token = TokenRx().Match(await c.GetStringAsync("/welcome/word")).Groups[1].Value;
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/welcome/missions/word") { Content = System.Net.Http.Json.JsonContent.Create(new { date = d, items = new[] { "Word.Me", "Word.Parent" } }) };
        req.Headers.Add("RequestVerificationToken", token);
        (await c.SendAsync(req)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task R171_가입하면_임시_부서_참여자가_되어_바로_홈으로_간다()
    {
        var (c, signup) = await SignUp();
        Assert.Equal("/", signup.Headers.Location!.OriginalString);
        var home = await c.GetAsync("/");
        Assert.Equal("/welcome", home.Headers.Location!.OriginalString);
        var me = AccountId();
        using var raw = InMemoryDb.Raw(_db);
        var m = raw.Memberships.Include(x => x.Roles).Single(x => x.TenantId == _lobby.Id && x.AccountId == me);
        Assert.Equal(MembershipStatus.Approved, m.Status);
        Assert.Equal([Roles.Participant], m.Roles.Select(r => r.Role));
        Assert.True(raw.Participants.Single(p => p.MembershipId == m.Id).IsActive);
    }

    [Fact]
    public async Task R171_임시_부서에서_미션과_통독을_바로_한다()
    {
        var (c, _) = await SignUp();
        await PressWord(c, Jan5);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/welcome/reading/read/1/1")).StatusCode);
    }

    [Fact]
    public async Task R172_임시_부서_홈은_내_부서_찾기_띠만_있고_남의_기록은_숨긴다()
    {
        var (c, _) = await SignUp();
        var home = await c.GetStringAsync("/welcome");
        Assert.Contains("아직 우리 부서가 없어요", home);
        Assert.Contains("<a class=\"go\" href=\"/join\">내 부서 찾기 →</a>", home);
        Assert.DoesNotContain("aria-labelledby=\"dpT\"", home);                       // "우리 부서" 숫자 칸 없음
        Assert.DoesNotContain("/welcome/ranking", home);
        Assert.Contains("<a href=\"/join\">내 부서 찾기</a>", home);                    // 계정 줄
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/welcome/ranking")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/welcome/ranking?t=team")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/welcome/reading/participants")).StatusCode);
        Assert.DoesNotContain("/welcome/reading/participants", await c.GetStringAsync("/welcome/reading"));
        Assert.DoesNotContain("/welcome/ranking", await c.GetStringAsync("/welcome/word-mission"));
    }

    [Fact]
    public async Task R172_신청해_두면_승인_기다리는_중으로_바뀐다()
    {
        var (c, _) = await SignUp();
        InMemoryDb.SeedMembership(_db, _kids.Id, AccountId(), MembershipStatus.Requested, Roles.Participant);
        var home = await c.GetStringAsync("/welcome");
        Assert.Contains("잠실새내교회 유년부 승인을 기다리는 중이에요", home);
        Assert.Contains("신청 보기", home);
    }

    [Fact]
    public async Task R171_임시_부서는_참여_신청_대상이_아니다()
    {
        var scope = new RequestScope();
        var join = new JoinService(new InMemoryDb(_db, scope), scope, TimeProvider.System);
        Assert.Null(await join.LookupAsync("WELCOM"));
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(t => t.Id == _lobby.Id).JoinListed = true; raw.SaveChanges(); }
        Assert.DoesNotContain(await join.ListedAsync(), t => t.TenantId == _lobby.Id);
        var a = InMemoryDb.SeedAccount(_db, "minji", "김민지");
        Assert.Equal(JoinError.NotFound, (await join.ApplyAsync(a.Id, _lobby.Id, "WELCOM", [Roles.Participant])).Error);
    }

    private MemberAdminService AdminIn(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        ctx.Set(t, null);
        return new MemberAdminService(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), ctx, TimeProvider.System, new LobbyService(new InMemorySystemDb(_db), TimeProvider.System));
    }

    [Fact]
    public async Task R173_R174_진짜_부서에_처음_승인되면_기록을_날짜별로_옮기고_임시_소속은_멈춘다()
    {
        var (c, _) = await SignUp();
        await PressWord(c, Jan4);
        await PressWord(c, Jan5);
        var me = AccountId();
        var admin = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.Id, MembershipStatus.Approved, Roles.TenantAdmin);
        var req = InMemoryDb.SeedMembership(_db, _kids.Id, me, MembershipStatus.Requested, Roles.Participant);

        Assert.Equal(MemberError.None, await AdminIn(_kids).ApproveAsync(admin.Id, req.Id, [Roles.Participant], null));

        using var raw = InMemoryDb.Raw(_db);
        var kp = raw.Participants.Single(p => p.TenantId == _kids.Id && p.MembershipId == req.Id).Id;
        var acts = raw.Activities.Where(a => a.TenantId == _kids.Id && a.ParticipantId == kp).OrderBy(a => a.MissionDate).ThenBy(a => a.MissionItem).ToList();
        Assert.Equal(4, acts.Count);                                                   // 4일 · 5일 × 말씀 두 항목
        Assert.Equal([Jan4, Jan4, Jan5, Jan5], acts.Select(a => a.MissionDate));
        var ledger = raw.PointLedgers.Where(l => l.TenantId == _kids.Id && l.ParticipantId == kp).ToList();
        Assert.Equal(acts.Sum(a => a.Points), ledger.Sum(l => l.Points));
        Assert.All(ledger, l => Assert.Contains(acts, a => a.Id == l.ActivityId));    // 원장은 옮긴 새 기록을 가리킴
        var lm = raw.Memberships.Single(m => m.TenantId == _lobby.Id && m.AccountId == me);
        Assert.Equal(MembershipStatus.Suspended, lm.Status);                          // 지우지 않고 멈춤
        Assert.False(raw.Participants.Single(p => p.MembershipId == lm.Id).IsActive);
        Assert.Equal(4, raw.Activities.Count(a => a.TenantId == _lobby.Id));          // 임시 부서 쪽 기록은 남음
        Assert.Contains(raw.AuditLogs, l => l.Action == LobbyService.MovedAction && l.TenantId == _kids.Id && l.Detail!.Contains("activities=4"));
    }

    [Fact]
    public async Task R174_같은_날_같은_미션은_진짜_부서_것을_남기고_두번째_승인에는_옮기지_않는다()
    {
        var (c, _) = await SignUp();
        await PressWord(c, Jan5);
        var me = AccountId();
        var admin = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.Id, MembershipStatus.Approved, Roles.TenantAdmin);
        var req = InMemoryDb.SeedMembership(_db, _kids.Id, me, MembershipStatus.Requested, Roles.Participant);
        int kp;
        using (var raw = InMemoryDb.Raw(_db))   // 진짜 부서에 이미 5일 "Word.Me" 가 있다고 하자(참여자 행을 미리 만듦)
        {
            var p = new Participant { TenantId = _kids.Id, MembershipId = req.Id };
            raw.Participants.Add(p);
            raw.SaveChanges();
            kp = p.Id;
            var wid = raw.DailyWords.Single(w => w.ExposeDate == Jan5).Id;
            raw.Activities.Add(new Activity { TenantId = _kids.Id, ParticipantId = kp, MissionItem = "Word.Me", MissionDate = Jan5, DailyWordId = wid, Points = 7, PerformedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        await AdminIn(_kids).ApproveAsync(admin.Id, req.Id, [Roles.Participant], null);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var acts = raw.Activities.Where(a => a.TenantId == _kids.Id && a.ParticipantId == kp).ToList();
            Assert.Equal(2, acts.Count);
            Assert.Equal(7, acts.Single(a => a.MissionItem == "Word.Me").Points);       // 진짜 부서 것을 남김
            Assert.Contains(raw.AuditLogs, l => l.Action == LobbyService.MovedAction && l.Detail!.Contains("skipped=1"));
        }

        // 두 번째 진짜 부서 승인: 임시 소속이 이미 멈춰 있어 옮기지 않는다
        var elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        InMemoryDb.SeedMembership(_db, elem.Id, admin.Id, MembershipStatus.Approved, Roles.TenantAdmin);
        var req2 = InMemoryDb.SeedMembership(_db, elem.Id, me, MembershipStatus.Requested, Roles.Participant);
        await AdminIn(elem).ApproveAsync(admin.Id, req2.Id, [Roles.Participant], null);
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal(0, raw.Activities.Count(a => a.TenantId == elem.Id));
            Assert.Single(raw.AuditLogs, l => l.Action == LobbyService.MovedAction);
        }
    }

    [Fact]
    public async Task R173_승인되면_첫_화면은_진짜_부서로()
    {
        var (c, _) = await SignUp();
        var me = AccountId();
        var admin = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.Id, MembershipStatus.Approved, Roles.TenantAdmin);
        var req = InMemoryDb.SeedMembership(_db, _kids.Id, me, MembershipStatus.Requested, Roles.Guardian);
        await AdminIn(_kids).ApproveAsync(admin.Id, req.Id, [Roles.Guardian], null);   // 참여자가 아니어도 임시 소속은 멈춤
        Assert.Equal("/jamsil-kids", (await c.GetAsync("/")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task R172_첫_화면_부서_수에_임시_부서는_세지_않는다()
    {
        var html = await _app.CreateClient().GetStringAsync("/");
        Assert.Contains("1<i>곳</i>", html);
    }
}
