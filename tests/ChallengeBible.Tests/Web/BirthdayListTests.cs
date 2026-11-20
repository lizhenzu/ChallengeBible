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
/// R-344 생일(ADR 0121, 확정 시안 생일-시안): 교사 · 관리자만. 다가오는(기본 30일, 7 · 14 · 30 · 60) · 월별(달력 · 명단) · 연간 · 생일 없음(바로 넣기).
/// 오늘은 부서 날짜(시험 시각 2027-01-05 서울), 2월 29일생은 평년에 2월 28일. 담임은 같은 날 안에서 내 반 먼저. 선물은 상태만.
/// </summary>
public partial class BirthdayListTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public BirthdayListTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());   // 2027-01-05(화) 서울
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var t = InMemoryDb.SeedAccount(_db, "teacher@example.com", "박선생");
        InMemoryDb.SeedMembership(_db, _kids.Id, t.UserName!, MembershipStatus.Approved, Roles.Teacher);
        var kid = InMemoryDb.SeedAccount(_db, "kid@example.com", "김하늘");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        var now = DateTime.UtcNow;
        var tp = new Person { TenantId = _kids.Id, Name = "박선생", UserId = t.Id, CreatedUtc = now };
        var p1 = new Person { TenantId = _kids.Id, Name = "김하늘", UserId = kid.Id, BirthMonth = 1, BirthDay = 5, CreatedUtc = now };      // 오늘
        var p2 = new Person { TenantId = _kids.Id, Name = "이바다", RoleCode = "T1001", BirthMonth = 1, BirthDay = 7, CreatedUtc = now };   // D-2 (내 반)
        var p3 = new Person { TenantId = _kids.Id, Name = "가나다", RoleCode = "T1001", BirthMonth = 1, BirthDay = 7, CreatedUtc = now };   // D-2 (다른 반)
        var p4 = new Person { TenantId = _kids.Id, Name = "최별", RoleCode = "T1001", BirthMonth = 2, BirthDay = 20, CreatedUtc = now };    // D-46
        var p5 = new Person { TenantId = _kids.Id, Name = "윤달", RoleCode = "T1001", BirthMonth = 2, BirthDay = 29, CreatedUtc = now };    // 2027 평년 → 2월 28일
        var p6 = new Person { TenantId = _kids.Id, Name = "오하린", RoleCode = "T1001", CreatedUtc = now };                                // 생일 없음
        var p7 = new Person { TenantId = _kids.Id, Name = "지난해", RoleCode = "T1001", BirthMonth = 1, BirthDay = 4, CreatedUtc = now };   // 어제 → 내년(D-364)
        var p8 = new Person { TenantId = _kids.Id, Name = "탈퇴함", RoleCode = "T1001", BirthMonth = 1, BirthDay = 6, LeftOn = new DateOnly(2026, 12, 1), CreatedUtc = now };
        raw.People.AddRange(tp, p1, p2, p3, p4, p5, p6, p7, p8);
        raw.SaveChanges();
        var term = new ClassTerm { TenantId = _kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 1), CreatedUtc = now };
        raw.ClassTerms.Add(term); raw.SaveChanges();
        var c1 = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1반", CreatedUtc = now };
        var c2 = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "2반", CreatedUtc = now };
        raw.Classes.AddRange(c1, c2); raw.SaveChanges();
        raw.ClassMembers.AddRange(
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c1.Id, PersonId = tp.Id, IsTeacher = true, CreatedUtc = now },
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c1.Id, PersonId = p2.Id, CreatedUtc = now },
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c2.Id, PersonId = p3.Id, CreatedUtc = now });
        raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Birthday, State = GiftStates.Picked, Year = 2027, PersonId = p2.Id, SourceKey = "B2027-0107", MaxAmount = 5000, CreatedUtc = now });
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234" }));
        return c;
    }

    private static string Text(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));

    [Fact]
    public void R344_2월_29일생은_평년에_2월_28일_어제는_내년()
    {
        var today = new DateOnly(2027, 1, 5);
        Assert.Equal(new DateOnly(2027, 2, 28), BirthdayListService.NextFrom(today, 2, 29));
        Assert.Equal(new DateOnly(2028, 2, 29), BirthdayListService.NextFrom(new DateOnly(2028, 1, 1), 2, 29));
        Assert.Equal(new DateOnly(2027, 1, 5), BirthdayListService.NextFrom(today, 1, 5));
        Assert.Equal(new DateOnly(2028, 1, 4), BirthdayListService.NextFrom(today, 1, 4));
    }

    [Fact]
    public async Task R344_교사는_다가오는_생일을_날짜순으로_보고_같은_날은_내_반이_먼저()
    {
        var t = await Login("teacher@example.com");
        var html = await t.GetStringAsync("/kids/birthdays");
        var text = Text(html);
        Assert.Contains("aria-current=\"page\">30일", html);   // 기본 30일
        Assert.Contains("🎂 오늘", text);
        Assert.True(text.IndexOf("김하늘", StringComparison.Ordinal) < text.IndexOf("이바다", StringComparison.Ordinal));
        Assert.True(text.IndexOf("이바다", StringComparison.Ordinal) < text.IndexOf("가나다", StringComparison.Ordinal));   // 같은 D-2 — 내 반 먼저
        Assert.Contains("내 반", text);
        Assert.Contains("골랐음", text);   // 선물 상태
        Assert.DoesNotContain("최별", text);   // D-46 → 30일 밖
        Assert.DoesNotContain("탈퇴함", text);
        Assert.Contains("최별", Text(await t.GetStringAsync("/kids/birthdays?Days=60")));
        Assert.Contains("/kids/admin/gifts", html);   // 선물 처리로만
    }

    [Fact]
    public async Task R344_월별_달력_명단_연간_생일없음()
    {
        var t = await Login("teacher@example.com");
        var feb = Text(await t.GetStringAsync("/kids/birthdays?Tab=month&M=2&V=list"));
        Assert.Contains("최별", feb);
        Assert.Contains("윤달", feb);
        var cal = await t.GetStringAsync("/kids/birthdays?Tab=month&M=2&D=28");
        Assert.Contains("2월 28일", Text(cal));
        Assert.Contains("<u>윤달</u>", cal);   // 평년 2월 28일 칸
        var year = Text(await t.GetStringAsync("/kids/birthdays?Tab=year"));
        Assert.Contains("1월 4명", year.Replace("  ", " "));   // 김하늘 · 이바다 · 가나다 · 지난해
        Assert.Contains("인쇄", year);

        var none = await t.GetStringAsync("/kids/birthdays?Tab=none");
        Assert.Contains("오하린", Text(none));
        int pid; using (var raw = InMemoryDb.Raw(_db)) pid = raw.People.Single(p => p.Name == "오하린").Id;
        var r = await t.PostAsync("/kids/birthdays?handler=Birthday", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = TokenRx().Match(none).Groups[1].Value, ["pid"] = pid.ToString(), ["month"] = "3", ["day"] = "9" }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        using var after = InMemoryDb.Raw(_db);
        Assert.Equal(((byte?)3, (byte?)9), (after.People.Single(p => p.Id == pid).BirthMonth, after.People.Single(p => p.Id == pid).BirthDay));
    }

    [Fact]
    public async Task R344_학생은_못_보고_메뉴도_없다_교사_메뉴와_관리_머리말에는_있다()
    {
        var kid = await Login("kid@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await kid.GetAsync("/kids/birthdays")).StatusCode);
        Assert.DoesNotContain("/kids/birthdays", await kid.GetStringAsync("/kids"));
        var t = await Login("teacher@example.com");
        Assert.Contains("href=\"/kids/birthdays\"", await t.GetStringAsync("/kids"));
        Assert.Contains("<b>생일</b>", await t.GetStringAsync("/kids/birthdays"));   // 머리말 "도전!!!성경말씀 > 생일"
    }
}
