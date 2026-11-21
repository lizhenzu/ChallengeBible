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
        raw.DeptRoles.Single(d => d.UserId == kid.Id).RoleCode = "T1001";   // R-351: 목록에는 반에 든 학생 · 새친구만
        raw.ClassMembers.AddRange(
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c2.Id, PersonId = p1.Id, CreatedUtc = now },
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c2.Id, PersonId = p4.Id, CreatedUtc = now },
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c2.Id, PersonId = p5.Id, CreatedUtc = now },
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c2.Id, PersonId = p6.Id, CreatedUtc = now },
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c2.Id, PersonId = p7.Id, CreatedUtc = now },
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
        Assert.DoesNotContain("선물 사기 · 전하기는 선물 처리", html);   // 선물 관리 바로가기는 관리자만(R-352) — 이 사람은 교사
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
    public async Task R352_생일자_보기는_모두_보지만_학생에게는_이름_날짜_반만_선물_관리는_관리자만()
    {
        using (var raw = InMemoryDb.Raw(_db)) { raw.People.Single(p => p.Name == "이바다").Phone1 = "010-1111-2222"; raw.SaveChanges(); }
        var kid = await Login("kid@example.com");
        var page = await kid.GetStringAsync("/kids/birthdays");
        var text = Text(page);
        Assert.Contains("이바다", text);
        Assert.Contains("1월 7일", text);
        Assert.Contains("1반", text);
        Assert.DoesNotContain("010-1111-2222", text);   // 전화 없음
        Assert.DoesNotContain("골랐음", text);   // 선물 상태 없음
        Assert.DoesNotContain("담임", text);
        Assert.DoesNotContain("Tab=none", page);   // 생일 없음 탭 없음
        Assert.Contains("<b>생일자 보기</b>", page);   // 머리말 "도전!!!성경말씀 > 생일자 보기"
        Assert.Contains("data-pnav-open", page);   // 일반 ☰
        Assert.DoesNotContain("오하린", Text(await kid.GetStringAsync("/kids/birthdays?Tab=none")));   // 생일 없음은 교사 · 관리자만
        var home = await kid.GetStringAsync("/kids");
        Assert.Contains("href=\"/kids/birthdays\"", home);   // 메뉴판 "생일" 묶음: 생일자 보기
        Assert.DoesNotContain("href=\"/kids/admin/gifts\"", home);   // 선물 관리는 관리자만

        var t = await Login("teacher@example.com");
        var tp = Text(await t.GetStringAsync("/kids/birthdays"));
        Assert.Contains("010-1111-2222", tp);   // 교사는 상세
        Assert.Contains("골랐음", tp);
        Assert.DoesNotContain("href=\"/kids/admin/gifts\"", await t.GetStringAsync("/kids"));   // 교사(관리자 아님)
    }

    private (long Gift, int Item) SeedOpenGift()
    {
        using var raw = InMemoryDb.Raw(_db);
        var item = new GiftItem { Name = "키캡 키링", Price = 4900, CoupangUrl = "https://www.coupang.com/vp/products/1", Status = GiftItemStatus.Active };
        raw.GiftItems.Add(item);
        var p1 = raw.People.Single(p => p.Name == "김하늘");
        var g = new Gift { TenantId = _kids.Id, Kind = GiftKinds.Birthday, State = GiftStates.Open, Year = 2027, PersonId = p1.Id, UserName = raw.Users.Single(u => u.Email == "kid@example.com").UserName,
            SourceKey = "B2027-0105", MinAmount = 0, MaxAmount = 5000, CreatedUtc = DateTime.UtcNow };
        raw.Gifts.Add(g);
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = p1.Id, Sunday = new DateOnly(2027, 1, 3), CreatedUtc = DateTime.UtcNow });
        raw.SaveChanges();
        return (g.Id, item.Id);
    }

    /// <summary>R-345(ADR 0122, 확정 시안 생일카드-선물): 카드에 전화 · 출석(올해) · 담임 · 선물 상태 · 링크 칩, 선생님이 대신 고르면 "골랐음" · 아이 화면 "선생님이 골라 주셨어요".</summary>
    [Fact]
    public async Task R345_카드_상세와_선생님이_대신_고르기()
    {
        var (gid, item) = SeedOpenGift();
        using (var raw = InMemoryDb.Raw(_db)) { raw.People.Single(p => p.Name == "이바다").Phone1 = "010-1111-2222"; raw.People.Single(p => p.Name == "박선생").Phone1 = "010-3333-4444"; raw.SaveChanges(); }
        var t = await Login("teacher@example.com");
        var html = await t.GetStringAsync("/kids/birthdays");
        var text = Text(html);
        Assert.Contains("010-1111-2222", text);   // 전화
        Assert.Contains("박선생 선생님", text);      // 담임(이바다 1반)
        Assert.Contains("1/1 (100%)", text);        // 출석: 올해 주일 1번(1/3) 중 1번(김하늘)
        Assert.Contains("선택 전", text);   // 2026-11-21 "고르는 중" → "선택 전"
        Assert.Contains("링크 없음", text);
        Assert.Contains("선물 선택", text);
        Assert.Contains("링크 복사", text);
        Assert.Contains("카톡으로 보내기", text);   // 2026-11-21 사용자
        Assert.DoesNotContain("선물 안 주기", text);   // [선물 안 주기] · [새 링크] · [다시 주기]는 없앴다
        Assert.DoesNotContain("새 링크", text);
        Assert.DoesNotContain("건너뛰기", text);

        var sheet = await t.GetStringAsync($"/kids/birthdays?pick={gid}");
        Assert.Contains("키캡 키링", Text(sheet));
        var r = await t.PostAsync("/kids/birthdays?handler=Pick&Tab=up", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = TokenRx().Match(sheet).Groups[1].Value, ["gift"] = gid.ToString(), ["item"] = item.ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var g = raw.Gifts.Single(x => x.Id == gid);
            Assert.Equal((GiftStates.Picked, (int?)item), (g.State, g.GiftItemId));
            Assert.StartsWith(BirthdayGiftService.StaffPrefix, raw.GiftLogs.Where(l => l.GiftId == gid && l.Action == GiftActions.Pick).Single().Detail);
        }
        var after = Text(await t.GetStringAsync("/kids/birthdays"));
        Assert.Contains("선생님(박선생)이 골라 주셨어요", after);
        Assert.Contains("쿠팡에서 보기", after);
        Assert.Contains("선물 바꾸기", after);

        // 링크 → 부모님이 열면 "부모님이 열어 봄" · 링크 화면에 "선생님이 골라 주셨어요"
        var page = await t.GetStringAsync("/kids/birthdays");
        await t.PostAsync("/kids/birthdays?handler=Link&Tab=up", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["gift"] = gid.ToString() }));
        string token; using (var raw = InMemoryDb.Raw(_db)) token = raw.Gifts.Single(x => x.Id == gid).LinkToken!;
        Assert.Contains("선생님이 골라 주셨어요", Text(await _app.CreateClient().GetStringAsync($"/gift/{token}")));
        Assert.Contains("부모님이 열어 봄", Text(await t.GetStringAsync("/kids/birthdays")));
    }

    [Fact]
    public async Task R345_안_주기_다시_주기_새_링크는_없고_카톡으로_보내기_창이_열린다()
    {
        var (gid, _) = SeedOpenGift();
        var t = await Login("teacher@example.com");
        var page = await t.GetStringAsync("/kids/birthdays");
        Assert.DoesNotContain("handler=Skip", page);   // 2026-11-21 사용자: 없앴다
        Assert.DoesNotContain("다시 주기", Text(page));
        var r = await t.PostAsync("/kids/birthdays?handler=Skip&Tab=up", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["gift"] = gid.ToString(), ["skip"] = "true" }));
        Assert.NotEqual(HttpStatusCode.Redirect, r.StatusCode);   // 처리 길 자체가 없다
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(GiftStates.Open, raw.Gifts.Single(x => x.Id == gid).State);

        // [카톡으로 보내기]: 링크를 만들고 보내기 창을 연다 — 창에 휴대폰 공유 버튼
        var res = await t.PostAsync("/kids/birthdays?handler=Link&Tab=up", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["gift"] = gid.ToString() }));
        var box = await t.GetStringAsync(res.Headers.Location!.OriginalString);
        Assert.Contains("data-share=", box);
        Assert.Contains("카톡으로 보내기", box);
        Assert.Matches(@"/js/share\.[A-Za-z0-9_]+\.js", box);   // share.js(버전 붙은 이름)
        Assert.Contains("링크만 복사", box);
    }

    [Fact]
    public async Task R351_목록에는_반에_든_학생과_새친구만_보인다_교사_기타_학부모_반_없는_학생은_안_보인다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var now = DateTime.UtcNow;
            raw.People.AddRange(
                new Person { TenantId = _kids.Id, Name = "새친구가", RoleCode = "T1020", BirthMonth = 1, BirthDay = 8, CreatedUtc = now },    // 반 없어도 보임
                new Person { TenantId = _kids.Id, Name = "반없는학생", RoleCode = "T1001", BirthMonth = 1, BirthDay = 8, CreatedUtc = now },
                new Person { TenantId = _kids.Id, Name = "교사하나", RoleCode = "T1002", BirthMonth = 1, BirthDay = 8, CreatedUtc = now },
                new Person { TenantId = _kids.Id, Name = "기타사람", RoleCode = "T1099", BirthMonth = 1, BirthDay = 8, CreatedUtc = now },
                new Person { TenantId = _kids.Id, Name = "학부모님", BirthMonth = 1, BirthDay = 8, CreatedUtc = now });
            raw.SaveChanges();
        }
        var t = await Login("teacher@example.com");
        var text = Text(await t.GetStringAsync("/kids/birthdays"));
        Assert.Contains("새친구가", text);
        Assert.Contains("이바다", text);   // 반에 든 학생
        foreach (var no in new[] { "반없는학생", "교사하나", "기타사람", "학부모님" }) Assert.DoesNotContain(no, text);
        Assert.DoesNotContain("선물 대상 아님", text);
        var year = Text(await t.GetStringAsync("/kids/birthdays?Tab=year"));
        Assert.DoesNotContain("교사하나", year);
        Assert.DoesNotContain("학부모님", year);
    }
}
