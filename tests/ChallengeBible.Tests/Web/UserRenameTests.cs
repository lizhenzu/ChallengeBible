using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>아이디 변경(ADR 0090): 플랫폼 관리자만, 그 사람의 기록도 함께 고친다.</summary>
public partial class UserRenameTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly string _op, _kid, _other;

    public UserRenameTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자").UserName!;
        _kid = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").UserName!;
        _other = InMemoryDb.SeedAccount(_db, "younghee@example.com", "김영희").UserName!;
        InMemoryDb.SeedMembership(_db, _kids.Id, _kid, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        InMemoryDb.AddRole(_db, raw.Users.Single(u => u.UserName == _op).Id, IdentityRoles.SuperAdmin);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        raw.SaveChanges();
    }

    private PlatformAccountService Service() => new(new InMemorySystemDb(_db), TimeProvider.System);

    private void SeedRecords()
    {
        using var raw = InMemoryDb.Raw(_db);
        var at = new DateTime(2027, 1, 4, 23, 0, 0, DateTimeKind.Utc);
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _kid, MissionDate = Jan5, MissionItem = MissionItems.WordMe, Points = 1, PerformedAt = at });
        raw.QuizAttempts.Add(new QuizAttempt { TenantId = _kids.Id, UserName = _kid, MissionDate = Jan5, QuestionId = 3, AttemptNo = 1, AnswerText = "답", IsCorrect = true, AttemptedAt = at });
        raw.ReadingRounds.Add(new ReadingRound { UserName = _kid, RoundNo = 1, StartedAt = at });
        raw.ReadingVerses.Add(new ReadingVerse { UserName = _kid, RoundNo = 1, BookId = 1, Chapter = 1, Verse = 1, ReadAt = at });
        raw.ReadingChapterReads.Add(new ReadingChapterRead { UserName = _kid, RoundNo = 1, BookId = 1, Chapter = 1, ReadAt = at });
        raw.ReadingChapterProgresses.Add(new ReadingChapterProgress { UserName = _kid, RoundNo = 1, BookId = 1, Chapter = 1, LastViewedVerse = 2, UpdatedAt = at });
        raw.ReadingBookmarks.Add(new ReadingBookmark { UserName = _kid, BookId = 1, Chapter = 1, Verse = 3, CreatedAt = at });
        raw.UserSettings.Add(new UserSetting { UserName = _kid, ReadingTheme = "paper" });
        var team = new FamilyTeam { TenantId = _kids.Id, Name = "웃음팀", CreatorUserName = _kid };
        team.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _kid, Relation = FamilyRelation.Student });
        raw.FamilyTeams.Add(team);
        raw.SaveChanges();
        raw.FamilyRequests.Add(new FamilyRequest { TenantId = _kids.Id, FamilyTeamId = team.Id, Kind = FamilyRequest.Invite, TargetUserName = _other, FromUserName = _kid, RelationKey = "mom" });
        raw.FamilyNotices.Add(new FamilyNotice { TenantId = _kids.Id, UserName = _kid, FamilyTeamId = team.Id, TeamName = "웃음팀" });
        raw.GuardianLinks.Add(new GuardianLink { TenantId = _kids.Id, GuardianUserName = _other, StudentUserName = _kid, CreatedByUserName = _op });
        raw.PromotionRequests.Add(new PromotionRequest { UserName = _kid, SourceTenantId = _kids.Id, TargetTenantId = _kids.Id, RequestedByUserName = _op });
        raw.SignupApplications.Add(new SignupApplication { TenantId = _kids.Id, UserId = "id-" + _kid, UserName = _kid, FullName = "김철수", RoleKind = SignupRoleKind.Student, Status = SignupStatus.Approved, DecidedBy = _op });
        raw.AuditLogs.Add(new AuditLog { ActorUserName = _op, Action = "x", Detail = $"account={_kid} 김철수", OccurredUtc = at });
        raw.AuditLogs.Add(new AuditLog { ActorUserName = _op, Action = "y", Detail = $"account={_kid}2 비슷한 아이디", OccurredUtc = at });
        raw.SaveChanges();
    }

    [Fact]
    public async Task 아이디를_바꾸면_회원과_기록의_아이디가_모두_바뀌고_감사_기록이_남는다()
    {
        SeedRecords();
        Assert.Null(await Service().RenameAsync(_op, _kid, "cheolsu2027", "cheolsu@example.com"));

        const string n = "cheolsu2027";
        using var raw = InMemoryDb.Raw(_db);
        var u = raw.Users.Single(x => x.Id == "id-" + _kid);
        Assert.Equal((n, "CHEOLSU2027"), (u.UserName, u.NormalizedUserName));
        Assert.Equal(n, raw.MissionActivities.Single().UserName);
        Assert.Equal(n, raw.QuizAttempts.Single().UserName);
        Assert.Equal(n, raw.ReadingRounds.Single().UserName);
        Assert.Equal(n, raw.ReadingVerses.Single().UserName);
        Assert.Equal(n, raw.ReadingChapterReads.Single().UserName);
        Assert.Equal((n, (short)2), (raw.ReadingChapterProgresses.Single().UserName, raw.ReadingChapterProgresses.Single().LastViewedVerse));
        Assert.Equal(n, raw.ReadingBookmarks.Single().UserName);
        Assert.Equal((n, "paper"), (raw.UserSettings.Single().UserName, raw.UserSettings.Single().ReadingTheme));
        Assert.Equal(n, raw.FamilyMembers.Single().UserName);
        Assert.Equal(n, raw.FamilyTeams.Single().CreatorUserName);
        Assert.Equal((n, _other), (raw.FamilyRequests.Single().FromUserName, raw.FamilyRequests.Single().TargetUserName));
        Assert.Equal(n, raw.FamilyNotices.Single().UserName);
        Assert.Equal(n, raw.GuardianLinks.Single().StudentUserName);
        Assert.Equal(n, raw.PromotionRequests.Single().UserName);
        Assert.Equal(n, raw.SignupApplications.Single().UserName);

        Assert.Contains(raw.AuditLogs.ToList(), l => l.Detail == $"account={n} 김철수");
        Assert.Contains(raw.AuditLogs.ToList(), l => l.Detail == "account=cheolsu2 비슷한 아이디");   // 앞부분만 같은 아이디는 그대로
        var log = raw.AuditLogs.Single(l => l.Action == PlatformAccountService.RenameAction);
        Assert.Equal((_op, $"account={n} 이전 아이디 {_kid}"), (log.ActorUserName, log.Detail));
    }

    [Fact]
    public async Task 관리자_아이디를_바꾸면_관리자가_남긴_기록의_이름도_바뀐다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.AuditLogs.Add(new AuditLog { ActorUserName = _op, ImpersonatorUserName = _op, Action = "x", OccurredUtc = DateTime.UtcNow });
            raw.GuardianLinks.Add(new GuardianLink { TenantId = _kids.Id, GuardianUserName = _other, StudentUserName = _kid, CreatedByUserName = _op });
            raw.TenantSeasons.Add(new TenantSeason { TenantId = _kids.Id, Year = 2027, StartDate = Jan5, ChangedByUserName = _op });
            raw.SaveChanges();
        }
        ContentSeed.Word(_db, Jan5);
        using (var raw = InMemoryDb.Raw(_db)) { raw.DailyWords.Single().CreatedByUserName = _op; raw.SaveChanges(); }

        Assert.Null(await Service().RenameAsync(_op, _op, "boss", "operator@example.com"));

        using var after = InMemoryDb.Raw(_db);
        var old = after.AuditLogs.Single(l => l.Action == "x");
        Assert.Equal(("boss", "boss"), (old.ActorUserName, old.ImpersonatorUserName));
        Assert.Equal("boss", after.GuardianLinks.Single().CreatedByUserName);
        Assert.Equal("boss", after.TenantSeasons.Single().ChangedByUserName);
        Assert.Equal("boss", after.DailyWords.Single().CreatedByUserName);
    }

    [Theory]
    [InlineData("younghee", "cheolsu@example.com")]     // 이미 쓰는 아이디
    [InlineData("YoungHee", "cheolsu@example.com")]     // 대소문자만 다른 아이디도 같다
    [InlineData("cheolsu", "cheolsu@example.com")]      // 지금과 같음
    [InlineData("a", "cheolsu@example.com")]            // 너무 짧음
    [InlineData("a@b.kr", "cheolsu@example.com")]       // 이메일 모양은 아이디가 될 수 없다
    [InlineData("김 철수", "cheolsu@example.com")]        // 공백 · 한글
    [InlineData("cheolsu2027", "other@example.com")]    // 이메일 확인이 틀림
    public async Task 잘못된_아이디나_틀린_확인이면_아무것도_바꾸지_않는다(string name, string confirm)
    {
        SeedRecords();
        Assert.NotNull(await Service().RenameAsync(_op, _kid, name, confirm));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(_kid, raw.Users.Single(u => u.Id == "id-" + _kid).UserName);
        Assert.Equal(_kid, raw.MissionActivities.Single().UserName);
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_바꿀_수_없다()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().RenameAsync(_other, _kid, "cheolsu2027", "cheolsu@example.com"));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string id)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/login");
        var r = await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value, ["Email"] = id, ["Password"] = "1234",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    [Fact]
    public async Task 화면에서_바꾸면_옛_쿠키는_다시_로그인하고_새_아이디로_들어간다()
    {
        var kid = await Login(_kid);
        Assert.Equal(HttpStatusCode.OK, (await kid.GetAsync("/jamsil-kids")).StatusCode);

        var op = await Login(_op);
        var page = await op.GetStringAsync($"/platform/accounts?q=cheolsu&id={_kid}&askRename=true");
        Assert.Contains("새 아이디", page);
        Assert.Contains($"아이디 {_kid}", page);
        var r = await op.PostAsync($"/platform/accounts?handler=Rename&q=cheolsu&id={_kid}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["newName"] = "cheolsu2027", ["confirm"] = "cheolsu@example.com",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("Id=cheolsu2027", r.Headers.Location!.OriginalString);
        Assert.Contains("아이디를 바꿨어요", await op.GetStringAsync(r.Headers.Location.OriginalString));

        var kicked = await kid.GetAsync("/jamsil-kids");
        Assert.Equal(HttpStatusCode.Redirect, kicked.StatusCode);
        Assert.Equal("/account/login", kicked.Headers.Location!.OriginalString);

        var back = await Login("cheolsu2027");
        Assert.Equal(HttpStatusCode.OK, (await back.GetAsync("/jamsil-kids")).StatusCode);
    }

    [Fact]
    public async Task 화면에서_틀리면_입력_칸이_열린_채_이유를_보인다()
    {
        var op = await Login(_op);
        var page = await op.GetStringAsync($"/platform/accounts?q=cheolsu&id={_kid}&askRename=true");
        var r = await op.PostAsync($"/platform/accounts?handler=Rename&q=cheolsu&id={_kid}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["newName"] = "younghee", ["confirm"] = "cheolsu@example.com",
        }));
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("이미 쓰는 아이디예요", html);
        Assert.Contains("name=\"newName\"", html);
    }
}
