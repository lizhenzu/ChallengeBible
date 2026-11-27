using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 완전 초기화(ADR 0102, R-314): 플랫폼 관리자가 지울 것을 체크하고(기본 모두) 확인 글자 RESET 을 쓰고 누르면 모든 부서에서 지운다.
/// 체크를 뺀 것은 남기고, 관리자 · 부서 · 반은 절대 안 지운다. 사람을 지우면 기록은 모두 함께. 오늘 = 2027-01-05(서울).
/// </summary>
public partial class ResetTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids;
    private readonly Dictionary<string, string> _p = [];

    public ResetTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(_time);
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        foreach (var (login, name) in new[] { ("cheolsu", "김철수"), ("younghee", "김영희") })
            _p[login] = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, login + "@fake-seed.test", name).UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private void Pts(string login, int points, DateTime performedUtc)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _p[login], MissionDate = new DateOnly(2027, 1, 5), MissionItem = "Word.Me", Points = points, PerformedAt = performedUtc });
        raw.SaveChanges();
    }

    private void FamilyTeam()
    {
        using var raw = InMemoryDb.Raw(_db);
        var t = new FamilyTeam { TenantId = _kids.Id, Name = "철수네", AwardKey = "철수네", TeamType = TeamTypes.Family, InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
        t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p["cheolsu"], SortOrder = 0, Relation = FamilyRelation.Student });
        t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p["younghee"], SortOrder = 1, Relation = FamilyRelation.Mother });
        raw.FamilyTeams.Add(t);
        raw.SaveChanges();
    }

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private static async Task<HttpResponseMessage> Reset(HttpClient c, string confirm, IEnumerable<string>? pick = null)
    {
        var token = TokenRx().Match(await c.GetStringAsync("/platform/reset")).Groups[1].Value;
        var form = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", token), new("confirm", confirm) };
        form.AddRange((pick ?? ResetService.AllKeys.Where(k => !ResetService.OffByDefault(k))).Select(k => new KeyValuePair<string, string>("pick", k)));
        return await c.PostAsync("/platform/reset", new FormUrlEncodedContent(form));
    }

    private void SeedEverything()
    {
        using var raw = InMemoryDb.Raw(_db);
        foreach (var state in new[] { GiftStates.Open, GiftStates.Picked, GiftStates.Purchased, GiftStates.Expired, GiftStates.Delivered, GiftStates.Cancelled })
            raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Personal, State = state, Year = 2027, UserName = _p["cheolsu"], SourceKey = "P-" + state, MaxAmount = 5000, CreatedUtc = DateTime.UtcNow });
        var reading = new Gift { TenantId = _kids.Id, Kind = GiftKinds.Reading, State = GiftStates.Delivered, Year = 2026, UserName = _p["cheolsu"], SourceKey = "R1", MaxAmount = 10000, ItemPrice = 9000, CreatedUtc = DateTime.UtcNow };
        raw.Gifts.Add(reading);
        raw.SaveChanges();
        foreach (var g in raw.Gifts.ToList()) raw.GiftLogs.Add(new GiftLog { TenantId = _kids.Id, GiftId = g.Id, Action = GiftActions.Open, AtUtc = DateTime.UtcNow });
        raw.GiftPointUses.Add(new GiftPointUse { TenantId = _kids.Id, UserName = _p["cheolsu"], Year = 2027, GiftId = raw.Gifts.First(g => g.SourceKey == "P-" + GiftStates.Picked).Id, PointsUsed = 100, CreatedUtc = DateTime.UtcNow });
        raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = "철수네", Threshold = 1000, AwardedAt = new DateTime(2026, 12, 20) });
        raw.TeamAwardArchives.Add(new TeamAwardArchive { TenantId = _kids.Id, Year = 2025, Kind = TeamTypes.Family, AwardKey = "철수네", Threshold = 1000, AwardedAt = new DateTime(2025, 12, 20) });
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _p["cheolsu"], MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = 300, PerformedAt = DateTime.UtcNow });
        var kid = new Person { TenantId = _kids.Id, Name = "김철수", RoleCode = "T1001", CreatedUtc = DateTime.UtcNow };
        var nc = new Person { TenantId = _kids.Id, Name = "새친구", RoleCode = "T1020", NewcomerFirstVisit = new DateOnly(2026, 12, 6), NewcomerVoluntary = false, NewcomerEvangelist = "김철수", CreatedUtc = DateTime.UtcNow };
        raw.People.AddRange(kid, nc);
        raw.SaveChanges();
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = kid.Id, Sunday = new DateOnly(2027, 1, 3), CreatedUtc = DateTime.UtcNow });
        foreach (var d in new[] { 6, 13, 20, 27 }) raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = nc.Id, Sunday = new DateOnly(2026, 12, d), CreatedUtc = DateTime.UtcNow });
        raw.AuditLogs.Add(new AuditLog { OccurredUtc = DateTime.UtcNow, ActorUserName = "operator", Action = "ImpersonateStart" });
        raw.ReadingRounds.Add(new ReadingRound { UserName = _p["cheolsu"], RoundNo = 1, StartedAt = new DateTime(2026, 1, 1), CompletedAt = new DateTime(2026, 12, 1) });
        raw.SaveChanges();
    }

    private (int ClassId, int AdminPersonId) SeedClass()
    {
        using var raw = InMemoryDb.Raw(_db);
        var term = new ClassTerm { TenantId = _kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 1), CreatedUtc = DateTime.UtcNow };
        raw.ClassTerms.Add(term); raw.SaveChanges();
        var cls = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1학년 1반", CreatedUtc = DateTime.UtcNow };
        var nc = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "새친구반", IsNewcomer = true, CreatedUtc = DateTime.UtcNow };
        raw.Classes.AddRange(cls, nc); raw.SaveChanges();
        var adminUser = raw.Users.Single(u => u.Email == "admin@example.com");
        var ap = new Person { TenantId = _kids.Id, Name = "윤동호", UserId = adminUser.Id, CreatedUtc = DateTime.UtcNow };
        raw.People.Add(ap); raw.SaveChanges();
        var kid = raw.People.Single(p => p.Name == "김철수");
        kid.UserId = raw.Users.Single(u => u.Email == "cheolsu@fake-seed.test").Id;
        raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = cls.Id, PersonId = kid.Id, CreatedUtc = DateTime.UtcNow });
        raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = cls.Id, PersonId = ap.Id, IsTeacher = true, CreatedUtc = DateTime.UtcNow });
        raw.GuardianLinks.Add(new GuardianLink { TenantId = _kids.Id, GuardianUserName = _p["younghee"], StudentUserName = _p["cheolsu"], CreatedUtc = DateTime.UtcNow, CreatedByUserName = "x" });
        raw.GiftYearCloses.Add(new GiftYearClose { Year = 2027, ClosedUtc = DateTime.UtcNow.AddDays(-1), ClosedByUserName = "operator" });
        raw.MemberTags.Add(new MemberTag { TenantId = _kids.Id, UserId = kid.UserId!, Tag = "gift", CreatedUtc = DateTime.UtcNow });
        raw.SaveChanges();
        return (cls.Id, ap.Id);
    }

    [Fact]
    public async Task R314_모두_체크하고_RESET_을_쓰면_관리자_부서_반만_남기고_모두_지운다()
    {
        SeedEverything();
        FamilyTeam();
        var (classId, adminPersonId) = SeedClass();
        var op = await Login("operator@example.com");
        var page = await op.GetStringAsync("/platform/reset");
        Assert.Contains("되돌릴 수 없어요", page);
        Assert.Contains("절대 안 지워요", page);
        Assert.Equal(ResetService.AllKeys.Count(k => !ResetService.OffByDefault(k)), System.Text.RegularExpressions.Regex.Matches(page, "name=\"pick\" value=\"[a-z]+\" data-rs-group=\"[^\"]+\" checked").Count);   // 처음엔 모두 체크

        var body = await (await Reset(op, "RESET")).Content.ReadAsStringAsync();
        Assert.Contains("초기화했어요", body);
        using var after = InMemoryDb.Raw(_db);
        Assert.Empty(after.MissionActivities);
        Assert.Empty(after.AttendanceChecks);   // 새친구 출석도
        Assert.Empty(after.Gifts);   // 완독 선물도
        Assert.Empty(after.GiftLogs);
        Assert.Empty(after.GiftPointUses);
        Assert.Empty(after.TeamAwards);
        Assert.Empty(after.TeamAwardArchives);
        Assert.Empty(after.FamilyTeams);
        Assert.Empty(after.FamilyMembers);
        Assert.Empty(after.GuardianLinks);
        Assert.Empty(after.ReadingRounds);
        Assert.Empty(after.MemberTags);
        Assert.Empty(after.GiftYearCloses);   // 초기화 시각도 남기지 않음
        // 관리자(부서 관리자 · 플랫폼 관리자)만 남고, 그 명부 줄 · 반 배정은 그대로. 반은 새친구반까지 모두 그대로
        Assert.Equal(new[] { "admin@example.com", "operator@example.com" }, after.Users.Select(u => u.Email!).OrderBy(e => e).ToArray());
        Assert.Equal(adminPersonId, after.People.Single().Id);
        Assert.Equal(adminPersonId, after.ClassMembers.Single().PersonId);
        Assert.Equal(2, after.Classes.Count());
        Assert.Contains(after.Classes, c => c.Id == classId);
        Assert.Single(after.DeptRoles);
        var audit = after.AuditLogs.Single();   // 감사 로그는 이번 초기화 한 줄만
        Assert.Equal(ResetService.Action, audit.Action);
        Assert.Equal("missions=1;attend=5;gifts=7;awards=2;teams=1;reading=1;copy=0;prayer=0;requests=0;logs=1;accounts=2;roster=2", audit.Detail);
    }

    [Fact]
    public async Task R315_완전_초기화를_모두_해도_플랫폼_관리자와_부서_어드민_계정은_그대로_남는다()
    {
        SeedEverything();
        SeedClass();
        (string Id, string? Hash, string? Role)[] Admins()
        {
            using var raw = InMemoryDb.Raw(_db);
            return [.. raw.Users.Where(u => u.Email == "admin@example.com" || u.Email == "operator@example.com").OrderBy(u => u.Email)
                .Select(u => new { u.Id, u.PasswordHash }).ToList()
                .Select(u => (u.Id, (string?)u.PasswordHash, raw.DeptRoles.Where(d => d.UserId == u.Id).Select(d => d.RoleCode).FirstOrDefault()))];
        }
        var before = Admins();
        int roles;
        using (var raw = InMemoryDb.Raw(_db)) roles = raw.UserRoles.Count(r => before.Select(b => b.Id).Contains(r.UserId));
        await Reset(await Login("operator@example.com"), "RESET");
        Assert.Equal(before, Admins());   // 계정 · 비밀번호 · 부서 역할 그대로
        using var after = InMemoryDb.Raw(_db);
        Assert.Equal(roles, after.UserRoles.Count(r => before.Select(b => b.Id).Contains(r.UserId)));   // 관리자 권한 그대로
        Assert.Single(after.People);        // 부서 어드민 명부 줄
        Assert.Single(after.ClassMembers);  // 부서 어드민 반 배정
    }

    [Fact]
    public async Task R314_체크를_뺀_기록은_남긴다()
    {
        SeedEverything();
        FamilyTeam();
        var op = await Login("operator@example.com");
        await Reset(op, "RESET", ["missions", "gifts"]);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.MissionActivities);
        Assert.Empty(raw.Gifts);
        Assert.Equal(5, raw.AttendanceChecks.Count());   // 체크 안 함 → 남음
        Assert.Single(raw.TeamAwards);
        Assert.Single(raw.FamilyTeams);
        Assert.Single(raw.ReadingRounds);
        Assert.Equal(4, raw.Users.Count());   // 사람은 그대로
        Assert.Equal(2, raw.People.Count());
    }

    [Fact]
    public async Task R314_사람을_지우면_기록은_체크와_상관없이_모두_함께_지운다()
    {
        SeedEverything();
        var op = await Login("operator@example.com");
        await Reset(op, "RESET", ["roster"]);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.MissionActivities);
        Assert.Empty(raw.AttendanceChecks);
        Assert.Empty(raw.Gifts);
        Assert.Empty(raw.People);
        Assert.Equal(4, raw.Users.Count());   // 계정은 체크 안 함 → 남음
        Assert.Equal(new[] { "missions", "attend", "gifts", "awards", "teams", "reading", "copy", "prayer", "requests", "logs", "roster" }, ResetService.Effective(["roster"]).OrderBy(k => ResetService.AllKeys.ToList().IndexOf(k)));
    }

    [Fact]
    public async Task R314_계정만_지우고_명부를_남기면_명부_줄의_계정_연결만_끊는다()
    {
        SeedEverything();
        SeedClass();
        var op = await Login("operator@example.com");
        await Reset(op, "RESET", ["accounts"]);
        using var raw = InMemoryDb.Raw(_db);
        var kid = raw.People.Single(p => p.Name == "김철수");
        Assert.Null(kid.UserId);
        Assert.Equal(2, raw.ClassMembers.Count());   // 반 배정도 남음
        Assert.DoesNotContain(raw.Users, u => u.Email == "cheolsu@fake-seed.test");
        Assert.Empty(raw.GuardianLinks);
    }

    [Fact]
    public async Task R314_RESET_을_쓰지_않거나_아무것도_체크하지_않으면_아무것도_바뀌지_않는다()
    {
        SeedEverything();
        var op = await Login("operator@example.com");
        Assert.Contains("RESET 를 써야 초기화해요", await (await Reset(op, "reset")).Content.ReadAsStringAsync());
        Assert.Contains("지울 것을 하나 이상 체크해 주세요", await (await Reset(op, "RESET", [])).Content.ReadAsStringAsync());
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(7, raw.Gifts.Count());
        Assert.Single(raw.MissionActivities);
        Assert.Single(raw.TeamAwards);
        Assert.Empty(raw.AuditLogs.Where(a => a.Action == ResetService.Action));
    }

    [Fact]
    public async Task R314_플랫폼_관리자만_들어오고_옛_주소는_완전_초기화로_간다()
    {
        var admin = await Login("admin@example.com");   // 부서 관리자
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync("/platform/reset")).StatusCode);
        var student = await Login("cheolsu@fake-seed.test");
        Assert.NotEqual(HttpStatusCode.OK, (await student.GetAsync("/platform/reset")).StatusCode);
        var op = await Login("operator@example.com");
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync("/platform/reset")).StatusCode);
        foreach (var old in new[] { "/platform/year-reset", "/platform/people-reset" })
        {
            var r = await op.GetAsync(old);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Equal("/platform/reset", r.Headers.Location!.OriginalString);
        }
        await op.GetAsync("/platform");   // 로그인 직후 한 번은 오늘 할 일로 갈 수 있다(R-309)
        var menu = await op.GetStringAsync("/platform");
        Assert.Contains("href=\"/platform/reset\"", menu);
        Assert.DoesNotContain("href=\"/platform/year-reset\"", menu);
        Assert.DoesNotContain("href=\"/platform/people-reset\"", menu);
    }

    [Fact]
    public async Task R314_초기화_뒤에는_처음_상태처럼_다시_넣은_기록을_모두_센다()
    {
        Pts("cheolsu", 340, _time.Now.UtcDateTime.AddHours(-1));
        await Reset(await Login("operator@example.com"), "RESET");
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.Users.Where(u => u.Email == "cheolsu@fake-seed.test"));   // 학생 계정도 지움

        var again = InMemoryDb.SeedAccount(_db, "cheolsu2@example.com", "김철수");
        _p["cheolsu"] = InMemoryDb.SeedMembership(_db, _kids.Id, again.UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        using (var raw = InMemoryDb.Raw(_db)) { foreach (var u in raw.Users.Where(u => u.Email == "cheolsu2@example.com")) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234"); raw.SaveChanges(); }
        Pts("cheolsu", 340, _time.Now.UtcDateTime.AddDays(-30));
        var kid = await Login("cheolsu2@example.com");
        Assert.Contains("340점", await kid.GetStringAsync("/kids/word-mission"));
    }

    /// <summary>2026-11-20 사용자(1번 · A): 완전 초기화는 가짜 데이터(@fake-seed.test)만 지운다 — 직접 가입한 회원의 계정 · 소속 · 명부 줄 · 기록 · 가족팀은 모두 남긴다.</summary>
    [Fact]
    public async Task R314_직접_가입한_회원은_계정과_기록을_모두_남기고_가짜만_지운다()
    {
        var real = InMemoryDb.SeedAccount(_db, "real@gmail.com", "박진짜");
        InMemoryDb.SeedMembership(_db, _kids.Id, real.UserName!, MembershipStatus.Approved, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = new Person { TenantId = _kids.Id, Name = "박진짜", UserId = real.Id, CreatedUtc = DateTime.UtcNow, Phone1 = "010-1111-2222" };
            raw.People.Add(p); raw.SaveChanges();
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = p.Id, Sunday = new DateOnly(2027, 1, 3), CreatedUtc = DateTime.UtcNow });
            raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = real.UserName!, MissionDate = new DateOnly(2027, 1, 5), MissionItem = "Word.Me", Points = 50, PerformedAt = DateTime.UtcNow });
            raw.ReadingRounds.Add(new ReadingRound { UserName = real.UserName!, RoundNo = 1, StartedAt = new DateTime(2026, 1, 1) });
            raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Personal, State = GiftStates.Open, Year = 2027, UserName = real.UserName, SourceKey = "real", MaxAmount = 5000, CreatedUtc = DateTime.UtcNow });
            var team = new FamilyTeam { TenantId = _kids.Id, Name = "진짜네", AwardKey = "진짜네", TeamType = TeamTypes.Family, InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
            team.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = real.UserName!, SortOrder = 0, Relation = FamilyRelation.Student });
            team.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p["cheolsu"], SortOrder = 1, Relation = FamilyRelation.Student });   // 가짜가 섞인 팀
            raw.FamilyTeams.Add(team);
            raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = "진짜네", Threshold = 1000, AwardedAt = new DateTime(2026, 12, 20) });
            raw.Users.Single(u => u.Id == real.Id).PasswordHash = new PasswordHasher<AppUser>().HashPassword(real, "1234");
            raw.SaveChanges();
        }
        SeedEverything();
        FamilyTeam();

        var op = await Login("operator@example.com");
        Assert.Contains("가짜 계정(@fake-seed.test)", await op.GetStringAsync("/platform/reset"));
        Assert.Contains("직접 가입한 회원 계정(관리자 빼고 모두)", await op.GetStringAsync("/platform/reset"));   // 2026-11-21: 고를 때만 지운다(기본 꺼짐)
        await Reset(op, "RESET");

        using var after = InMemoryDb.Raw(_db);
        var un = real.UserName!;
        Assert.Contains(after.Users, u => u.Id == real.Id);   // 계정
        Assert.Contains(after.DeptRoles, d => d.UserId == real.Id && d.TenantId == _kids.Id);   // 소속
        var row = after.People.Single(p => p.UserId == real.Id);   // 명부 줄 그대로
        Assert.Equal("010-1111-2222", row.Phone1);
        Assert.Single(after.AttendanceChecks.Where(a => a.PersonId == row.Id));   // 기록
        Assert.Single(after.MissionActivities.Where(m => m.UserName == un));
        Assert.Single(after.ReadingRounds.Where(r => r.UserName == un));
        Assert.Single(after.Gifts.Where(g => g.UserName == un));
        Assert.Equal(new[] { un }, after.FamilyMembers.Select(m => m.UserName).ToArray());   // 팀은 남고 가짜만 빠진다
        Assert.Equal("진짜네", after.FamilyTeams.Single().Name);
        Assert.Equal("진짜네", after.TeamAwards.Single().AwardKey);

        Assert.DoesNotContain(after.Users, u => u.Email!.EndsWith("@fake-seed.test"));   // 가짜는 모두
        Assert.DoesNotContain(after.MissionActivities, m => m.UserName == _p["cheolsu"]);
        Assert.DoesNotContain(after.Gifts, g => g.UserName == _p["cheolsu"]);
        Assert.Contains(after.Users, u => u.Email == "admin@example.com");   // 관리자도 그대로
    }

    private string SeedReal(string email, string name)
    {
        var real = InMemoryDb.SeedAccount(_db, email, name);
        InMemoryDb.SeedMembership(_db, _kids.Id, real.UserName!, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        var p = new Person { TenantId = _kids.Id, Name = name, UserId = real.Id, CreatedUtc = DateTime.UtcNow, Phone1 = "010-1111-2222" };
        raw.People.Add(p); raw.SaveChanges();
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = real.UserName!, MissionDate = new DateOnly(2027, 1, 5), MissionItem = "Word.Me", Points = 50, PerformedAt = new DateTime(2027, 1, 4, 23, 0, 0, DateTimeKind.Utc) });
        raw.SaveChanges();
        return real.UserName!;
    }

    [Fact]
    public async Task R314_직접_가입한_회원_계정을_고르면_관리자만_남기고_모두_지운다()
    {
        var un = SeedReal("real@gmail.com", "박진짜");
        var op = await Login("operator@example.com");
        Assert.DoesNotContain("value=\"members\" data-rs-group=\"사람\" checked", await op.GetStringAsync("/platform/reset"));   // 기본 꺼짐
        await Reset(op, "RESET", ["members"]);

        using var after = InMemoryDb.Raw(_db);
        Assert.DoesNotContain(after.Users, u => u.UserName == un);                               // 진짜 회원도 지움
        Assert.DoesNotContain(after.People, p => p.Name == "박진짜");
        Assert.DoesNotContain(after.MissionActivities, m => m.UserName == un);
        Assert.DoesNotContain(after.Users, u => u.Email != null && u.Email.EndsWith("@fake-seed.test"));
        Assert.Contains(after.Users, u => u.Email == "operator@example.com");                     // 플랫폼 관리자(R-315)
        var adm = after.Users.Single(u => u.Email == "admin@example.com");                       // 부서 관리자 · 소속 그대로
        Assert.Contains(after.DeptRoles, d => d.UserId == adm.Id);
        Assert.Contains(after.Tenants, t => t.Id == _kids.Id);
    }

    [Fact]
    public async Task 회원_내보내기는_고른_회원만_복원용_SQL로_만들고_관리자는_빼고_내용은_남기지_않는다()
    {
        var un = SeedReal("real@gmail.com", "박진짜");
        var op = await Login("operator@example.com");
        var page = await op.GetStringAsync("/platform/reset");
        Assert.Contains("회원 내보내기", page);
        Assert.Contains($"value=\"{un}\"", page);
        using (var raw = InMemoryDb.Raw(_db))
            Assert.DoesNotContain($"value=\"{raw.Users.Single(u => u.Email == "admin@example.com").UserName}\"", page);   // 관리자는 목록에 없음

        var adminName = InMemoryDb.Raw(_db).Users.Single(u => u.Email == "admin@example.com").UserName!;
        var tok = TokenRx().Match(page).Groups[1].Value;
        var res = await op.PostAsync("/platform/reset?handler=Export", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", tok),
            new KeyValuePair<string, string>("member", un),
            new KeyValuePair<string, string>("member", adminName),   // 관리자를 보내도 빠진다
        }));
        var body = WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());
        Assert.Contains("복원용 SQL 준비됐어요", body);
        Assert.Contains("INSERT INTO [Identity].[User]", body);
        Assert.Contains("N'박진짜'", body);                                       // 한글은 N'…'
        Assert.Contains("INSERT INTO [app].[Person]", body);
        Assert.Contains("010-1111-2222", body);
        Assert.Contains("SET IDENTITY_INSERT [app].[Person] ON;", body);
        Assert.Contains("(1명)", body);
        Assert.DoesNotContain("N'윤동호'", body);
        using var after = InMemoryDb.Raw(_db);
        var log = after.AuditLogs.Single(a => a.Action == MemberExportService.Action);    // 누가 · 몇 명만, SQL 내용은 없음
        Assert.Equal("1명 · " + log.Detail!.Split(" · ")[1], log.Detail);
        Assert.DoesNotContain("INSERT", log.Detail);
    }

    [Fact]
    public void 회원_내보내기_값_글자()
    {
        Assert.Equal("NULL", MemberExportService.Literal(null));
        Assert.Equal("N'O''Neil'", MemberExportService.Literal("O'Neil"));
        Assert.Equal("1", MemberExportService.Literal(true));
        Assert.Equal("CAST('2027-01-05 09:30:00.0000000' AS datetime2)", MemberExportService.Literal(new DateTime(2027, 1, 5, 9, 30, 0)));   // datetime 열에도 들어가게
        Assert.Equal("'2027-01-05'", MemberExportService.Literal(new DateOnly(2027, 1, 5)));
        Assert.Equal("0x0AFF", MemberExportService.Literal(new byte[] { 10, 255 }));
        Assert.Equal("12.5", MemberExportService.Literal(12.5m));
    }

    [Fact]
    public async Task 오늘_현황_계정_명단은_앞에_번호를_붙인다()
    {
        var op = await Login("operator@example.com");
        var page = await op.GetStringAsync("/platform/today?k=acct&f=all");
        Assert.Contains("<th class=\"no\">번호</th>", page);
        Assert.Contains("<td class=\"no num\">1</td>", page);
        Assert.Contains("<td class=\"no num\">2</td>", page);
    }
}
