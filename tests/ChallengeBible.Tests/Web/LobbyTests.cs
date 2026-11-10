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

    private string UserName(string email = "cheolsu") { using var raw = InMemoryDb.Raw(_db); return raw.Users.Single(a => a.Email == email + "@example.com").UserName!; }

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
        var me = UserName();
        using var raw = InMemoryDb.Raw(_db);
        var m = raw.DeptRoles.Single(x => x.TenantId == _lobby.Id && x.UserId == raw.Users.Single(u => u.UserName == me).Id);
        Assert.Equal(ChallengeBible.Web.Services.UserRole.StudentCode, m.RoleCode);
    }

    [Fact]
    public async Task R171_임시_부서에서_미션과_통독을_바로_한다()
    {
        var (c, _) = await SignUp();
        await PressWord(c, Jan5);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/welcome/reading/read/1/1")).StatusCode);
    }

    [Fact]
    public async Task R172_임시_부서_1단계는_윗부분만_있고_남의_기록은_숨긴다()
    {
        var (c, _) = await SignUp();
        var home = await c.GetStringAsync("/welcome");
        Assert.Contains("시작까지 3단계", home);                                          // 0068 1단계: 윗부분만
        Assert.Contains("href=\"/join\">내 부서 참여 <svg", home);                       // 큰 버튼 → 부서 고르기(이름은 "참여", 2026-10-30 고침)
        Assert.Contains("class=\"wl-btn wl-s\" href=\"/welcome/today\">먼저 미션 해 보기 <svg", home);   // 흰 큰 버튼 → 미션 화면
        Assert.Contains("<small>대한예수교장로회</small>", home);                          // 머리말 = 교단 · 교회(첫 화면과 같음)
        Assert.Contains("https://www.saenae.org/", home);
        Assert.DoesNotContain("내 부서를 찾는 동안", home);
        Assert.Contains("먼저 내가 참여할 부서를 신청해요.", home);
        Assert.DoesNotContain("교회 · 부서를", home);                                    // 교회를 찾는 것이 아님
        Assert.DoesNotContain("내 부서 찾기", home);
        Assert.Contains("<p class=\"copy\">© 2027 잠실새내 · 주일학교</p>", home);          // 맨 아래 © 줄(해는 오늘 날짜)
        Assert.DoesNotContain("미션 수행하러 가기", home);
        Assert.DoesNotContain("<section class=\"hj-card hj-pg m\"", home);                              // 아랫부분(미션 · 통독 카드)은 미션 화면으로
        Assert.DoesNotContain("aria-label=\"오늘의 말씀\"", home);
        Assert.DoesNotContain("aria-labelledby=\"dpT\"", home);                       // "우리 부서" 숫자 칸 없음
        Assert.DoesNotContain("/welcome/ranking", home);
        Assert.Contains("<a href=\"/join\">내 부서 참여</a>", home);                    // 계정 줄(로그아웃도 여기 있음)
        var today = await c.GetStringAsync("/welcome/today");                         // 미션 화면 = 지금 홈 아랫부분
        Assert.Contains("<section class=\"hj-card hj-pg m\"", today);
        Assert.Contains("<a href=\"/welcome\">← 처음 화면</a>", today);
        Assert.Contains("class=\"wl wl-wide\"", today);                               // 미션 화면 머리말은 아래 카드와 같은 폭(데스크톱 정렬)
        Assert.DoesNotContain("wl-wide", home);                                       // 1 · 2단계는 좁은 카드 폭 그대로
        Assert.DoesNotContain("승인 기다리는 중", today);                                // 신청 전이라 표시 없음
        Assert.DoesNotContain("시작까지 3단계", today);
        Assert.DoesNotContain("/welcome/ranking", today);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/welcome/ranking")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/welcome/ranking?t=team")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/welcome/reading/participants")).StatusCode);
        Assert.DoesNotContain("/welcome/reading/participants", await c.GetStringAsync("/welcome/reading"));
        Assert.DoesNotContain("/welcome/ranking", await c.GetStringAsync("/welcome/word-mission"));
    }

    [Fact]
    public async Task R172_신청해_두면_2단계_승인_기다리는_중으로_바뀐다()
    {
        var (c, _) = await SignUp();
        InMemoryDb.SeedMembership(_db, _kids.Id, UserName(), MembershipStatus.Requested, Roles.Participant);
        var home = await c.GetStringAsync("/welcome");
        Assert.Contains("<span>잠실새내교회 유년부</span>", home);                        // 참여 신청 ✓ + 신청한 부서
        Assert.Contains("관리자 승인 <span class=\"wl-tag wl-wait\">기다리는 중</span>", home);
        Assert.Contains("href=\"/welcome/today\">미션 수행하러 가기 <svg", home);          // 큰 버튼 → 미션 화면
        Assert.Contains("href=\"/join\">신청 보기 <svg", home);
        Assert.DoesNotContain("먼저 미션 해 보기", home);                                  // 1단계 전용 링크
        Assert.DoesNotContain("<section class=\"hj-card hj-pg m\"", home);
        var today = await c.GetStringAsync("/welcome/today");
        Assert.Contains("승인 기다리는 중</span>", today);                                  // 미션 화면 맨 위 표시
        Assert.Contains("<section class=\"hj-card hj-pg m\"", today);
    }

    [Fact]
    public async Task 미션_화면과_일반_부서_홈의_말씀_카드에는_암송_기도_칸이_없다()
    {
        ContentSeed.Verse(_db, 1);
        ContentSeed.Prayer(_db, Jan5);
        var (c, _) = await SignUp();
        var today = await c.GetStringAsync("/welcome/today");
        Assert.DoesNotContain("<small>오늘의 기도</small>", today);                        // 오른쪽 "이번 주 암송 · 오늘의 기도" 칸은 없다(2026-10-30)
        Assert.DoesNotContain("이번 주 암송", today);
        Assert.DoesNotContain("class=\"side\"", today);
        Assert.DoesNotContain("class=\"hj-rib\"", today);                                  // 말씀 한 줄 띠는 지움(2026-11-03)
        Assert.DoesNotContain("오늘 읽을 범위", today);
        InMemoryDb.SeedMembership(_db, _kids.Id, UserName(), MembershipStatus.Approved, Roles.Participant);
        var kids = await c.GetStringAsync("/jamsil-kids");                               // 일반 부서 홈도 같은 모양(2026-10-31)
        Assert.DoesNotContain("<small>오늘의 기도</small>", kids);
        Assert.DoesNotContain("class=\"hj-rib\"", kids);
    }

    [Fact]
    public async Task 일반_부서는_today_주소로_가도_원래_홈으로_보낸다()
    {
        var (c, _) = await SignUp();
        InMemoryDb.SeedMembership(_db, _kids.Id, UserName(), MembershipStatus.Approved, Roles.Participant);
        var r = await c.GetAsync("/jamsil-kids/today");                                  // 일반 부서 홈은 한 화면 그대로 — today 는 홈으로
        Assert.Equal("/jamsil-kids", r.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/jamsil-kids")).StatusCode);
        Assert.Contains("<section class=\"hj-card hj-pg m\"", await c.GetStringAsync("/jamsil-kids"));
        Assert.DoesNotContain("시작까지 3단계", await c.GetStringAsync("/jamsil-kids"));
        var kidsHome = await c.GetStringAsync("/jamsil-kids");                            // 맨 아래: 계정 줄 가운데 + © 줄(모든 부서 홈, 2026-10-30)
        Assert.Contains("<div class=\"acct\">", kidsHome);
        Assert.Contains("<p class=\"copy\">© 2027 잠실새내 · 주일학교</p>", kidsHome);
        Assert.DoesNotContain("부서 바꾸기", kidsHome);                                    // 일반 부서 홈 맨 아래에 "부서 바꾸기"는 없다(2026-11-05)
        Assert.Contains("<a href=\"/account\">내 계정</a>", kidsHome);
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
        Assert.Equal(JoinError.NotFound, (await join.ApplyAsync(a.UserName!, _lobby.Id, "WELCOM", SignupRoleKind.Student)).Error);
    }

    private MemberAdminService AdminIn(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        ctx.Set(t, null);
        return new MemberAdminService(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), ctx, TimeProvider.System);
    }

    [Fact]
    public async Task R173_승인되면_첫_화면은_진짜_부서로()
    {
        var (c, _) = await SignUp();
        var me = UserName();
        var admin = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
        var req = InMemoryDb.SeedMembership(_db, _kids.Id, me, MembershipStatus.Requested, Roles.Guardian);
        await AdminIn(_kids).ApproveAsync(admin.UserName!, req.Id, null);
        Assert.Equal("/jamsil-kids", (await c.GetAsync("/")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task R172_첫_화면_부서_수에_임시_부서는_세지_않는다()
    {
        var html = await _app.CreateClient().GetStringAsync("/");
        Assert.Contains("1<i>곳</i>", html);
    }
}
