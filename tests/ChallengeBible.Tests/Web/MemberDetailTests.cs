using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>관리 · 참여자 상세(확정 시안: 관리-참여자상세, 0035 임시 비밀번호, 0029 성경 읽음). 오늘 = 2027-01-05(서울).</summary>
public partial class MemberDetailTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;

    public MemberDetailTests(WebApplicationFactory<Program> app)
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
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<(HttpClient C, int Id)> SignUp(string login, string name)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value,
            ["DisplayName"] = name, ["Email"] = login + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        return (c, raw.Accounts.Single(a => a.Email == login + "@example.com").Id);
    }

    private async Task<(HttpClient Admin, Membership Kid, int KidAccount)> Setup()
    {
        var (admin, adminId) = await SignUp("eunhye", "이은혜");
        var (_, kidId) = await SignUp("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, adminId, MembershipStatus.Approved, Roles.TenantAdmin);
        var km = InMemoryDb.SeedMembership(_db, _kids.Id, kidId, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        raw.Memberships.Single(m => m.Id == km.Id).GroupLabel = "사랑반";
        var p = new Participant { TenantId = _kids.Id, MembershipId = km.Id };
        raw.Participants.Add(p);
        raw.SaveChanges();
        var at = new DateTime(2027, 1, 5, 0, 12, 0, DateTimeKind.Utc);   // 서울 9:12
        void Act(DateOnly d, string item, int pts, DateTime when)
        {
            var a = new Activity { TenantId = _kids.Id, ParticipantId = p.Id, MissionItem = item, MissionDate = d, Points = pts, PerformedUtc = when };
            raw.Activities.Add(a);
            raw.SaveChanges();
            raw.PointLedgers.Add(new PointLedger { TenantId = _kids.Id, ParticipantId = p.Id, MissionDate = d, MissionItem = item, Points = pts, ActivityId = a.Id, RecordedUtc = when });
        }
        Act(Jan5, "Word.Me", 1, at.AddMinutes(-30));
        Act(Jan5, "Prayer.Me", 1, at);
        Act(Jan5.AddDays(-1), "Word.Me", 1, at.AddDays(-1));
        raw.ReadingRounds.Add(new ReadingRound { AccountId = kidId, RoundNo = 1, StartedUtc = at.AddDays(-3) });
        raw.ChapterReads.Add(new ChapterRead { AccountId = kidId, RoundNo = 1, BookId = 1, Chapter = 1, ReadUtc = at });
        raw.VerseReadMarks.Add(new VerseReadMark { AccountId = kidId, BookId = 1, Chapter = 1, Verse = 1, ReadUtc = at });
        raw.SaveChanges();
        return (admin, km, kidId);
    }

    [Fact]
    public async Task 관리자는_소속_관리에서_자세히_보기로_점수_달력_활동_통독을_본다()
    {
        var (admin, kid, _) = await Setup();
        var list = await admin.GetStringAsync("/jamsil-kids/admin/members?Tab=Approved");
        Assert.Contains($"href=\"/jamsil-kids/admin/members/{kid.Id}\"", list);
        Assert.Contains("class=\"row-more\"", list);                                       // 펼치지 않아도 보이는 "자세히 →"
        Assert.Contains("href=\"/jamsil-kids/admin\">관리</a>", await admin.GetStringAsync("/jamsil-kids"));   // 부서 홈 → 관리 홈
        var html = await admin.GetStringAsync($"/jamsil-kids/admin/members/{kid.Id}");
        Assert.Contains("<h1>김철수</h1>", html);
        Assert.Contains("cheolsu@example.com · 사랑반", html);
        Assert.Matches(">3<u>점</u>", html);                                         // 올해 점수
        Assert.Matches(">2<u>일</u>", html);                                         // 연속(어제 · 오늘)
        Assert.Contains("1/5 9:12", html);                                           // 마지막 미션(부서 시간)
        Assert.Contains("2027년 1월", html);
        Assert.Contains("오늘 · 1월 5일", html);                                      // 최근 활동
        Assert.Contains("1 / 1,189장", html);                                        // 통독 진도
        Assert.Contains("임시 비밀번호 만들기", html);

        var day = await admin.GetStringAsync($"/jamsil-kids/admin/members/{kid.Id}?month=2027-01&date=2027-01-04");
        Assert.Contains("1월 4일(월) 활동", day);
    }

    [Fact]
    public async Task 다른_부서_소속이나_신청_중이면_404_교사는_못_본다()
    {
        var (admin, _, _) = await Setup();
        var (_, otherId) = await SignUp("jiho", "윤지호");
        var other = InMemoryDb.SeedMembership(_db, _elem.Id, otherId, MembershipStatus.Approved, Roles.Participant);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/jamsil-kids/admin/members/{other.Id}")).StatusCode);
        var (teacher, tid) = await SignUp("minsu", "김민수");
        var tm = InMemoryDb.SeedMembership(_db, _kids.Id, tid, MembershipStatus.Approved, Roles.Teacher);
        Assert.StartsWith("/account/denied", (await teacher.GetAsync($"/jamsil-kids/admin/members/{tm.Id}")).Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task R0035_부서_관리자가_임시_비밀번호를_만들면_한_번만_보이고_다음_로그인에_바꾼다()
    {
        var (admin, kid, kidId) = await Setup();
        var page = await admin.GetStringAsync($"/jamsil-kids/admin/members/{kid.Id}");
        var r = await admin.PostAsync($"/jamsil-kids/admin/members/{kid.Id}?handler=ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value,
        }));
        Assert.True(r.Headers.CacheControl!.NoStore);
        var temp = Regex.Match(await r.Content.ReadAsStringAsync(), "<code>([a-z]+-\\d{4})</code>").Groups[1].Value;
        Assert.NotEmpty(temp);
        using var raw = InMemoryDb.Raw(_db);
        var a = raw.Accounts.Single(x => x.Id == kidId);
        Assert.True(a.MustChangePassword);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<Account>().VerifyHashedPassword(a, a.PasswordHash, temp));
        var log = raw.AuditLogs.Single();
        Assert.Equal((_kids.Id, "PasswordReset"), (log.TenantId!.Value, log.Action));
    }

    [Fact]
    public async Task R0035_다른_부서의_관리자인_계정은_초기화할_수_없다()
    {
        var (admin, kid, kidId) = await Setup();
        InMemoryDb.SeedMembership(_db, _elem.Id, kidId, MembershipStatus.Approved, Roles.TenantAdmin);
        var page = await admin.GetStringAsync($"/jamsil-kids/admin/members/{kid.Id}");
        Assert.Contains("다른 부서의 관리자이기도 한 계정이에요", page);
        Assert.DoesNotContain("임시 비밀번호 만들기</button>", page);
        var r = await admin.PostAsync($"/jamsil-kids/admin/members/{kid.Id}?handler=ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value,
        }));
        Assert.DoesNotContain("<code>", await r.Content.ReadAsStringAsync());
        using var raw = InMemoryDb.Raw(_db);
        Assert.False(raw.Accounts.Single(x => x.Id == kidId).MustChangePassword);
    }

    [Fact]
    public async Task R0029_성경_읽음_지우기는_이름을_똑같이_써야_한다()
    {
        var (admin, kid, kidId) = await Setup();
        var page = await admin.GetStringAsync($"/jamsil-kids/admin/members/{kid.Id}?ask=marks");
        Assert.Contains("다른 부서에서 읽은 기록도 함께 지워지고", page);
        var token = TokenRx().Match(page).Groups[1].Value;
        Task<HttpResponseMessage> Post(string name) => admin.PostAsync($"/jamsil-kids/admin/members/{kid.Id}?handler=ResetMarks",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["confirm"] = name }));

        Assert.Contains("이름이 맞지 않아요", await (await Post("김철")).Content.ReadAsStringAsync());
        var ok = await Post(" 김철수 ");
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.VerseReadMarks.Where(m => m.AccountId == kidId));
    }
}
