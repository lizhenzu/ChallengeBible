using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>로그인 전 첫 화면(2026-11-22 사용자 확정, ADR 0128) — 부서 홈과 같은 dodan 네이비 테마. 오늘 = 2027-01-05 (서울).</summary>
public class LandingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public LandingTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        ContentSeed.Bible(_db);
    }

    private Task<string> Page() => _app.CreateClient().GetStringAsync("/");

    [Fact]
    public async Task 공용_말씀이_있으면_오늘의_말씀_카드와_암송_기도_칩이_보인다()
    {
        ContentSeed.Word(_db, Jan5);
        ContentSeed.Verse(_db, 1);
        ContentSeed.Prayer(_db, Jan5);
        var html = await Page();
        Assert.Contains("aria-label=\"오늘의 말씀\"", html);
        Assert.Contains("창세기 24:1", html);
        Assert.Contains("이번 주 암송", html);
        Assert.Contains("(1/3 ~ 1/9)", html);                       // 날짜 범위는 한 덩어리
        Assert.Contains("오늘의 기도 <b>신뢰</b>", html);
        Assert.Contains("© 2027 잠실새내 · 주일학교", html);
    }

    [Fact]
    public async Task 머리말은_교회_날짜_로그인이고_햄버거_메뉴가_있다()
    {
        var html = await Page();
        Assert.Contains("<small>대한예수교장로회</small>", html);
        Assert.Contains("href=\"https://www.saenae.org/\" target=\"_blank\" rel=\"noopener\"", html);
        Assert.Contains("<time class=\"vb-today\" datetime=\"2027-01-05\">", html);   // 날짜는 머리줄이 아니라 본문 제목 위(기준표 5판, 2026-11-24)
        Assert.DoesNotContain("class=\"vb-date\"", html);
        Assert.Contains("1월 5일", html);
        Assert.Contains("<a class=\"person-btn\" href=\"/account/login\" aria-label=\"로그인\" title=\"로그인\">", html);   // 로그인은 우측 사람 아이콘 하나
        Assert.Contains("aria-label=\"메뉴 열기\" aria-controls=\"pnav\" aria-expanded=\"false\" data-pnav-open", html);     // ☰ 메뉴(2026-11-23 사용자)
        Assert.Contains("<div class=\"pdrawer vb-dr\" id=\"pnav\" hidden>", html);
    }

    [Fact]
    public async Task 부서_덮어쓰기_말씀은_보이지_않고_공용이_없으면_카드를_숨긴다()
    {
        var kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        ContentSeed.Word(_db, Jan5, kids.Id, title: "부서만의 말씀");
        var html = await Page();
        Assert.DoesNotContain("aria-label=\"오늘의 말씀\"", html);
        Assert.DoesNotContain("부서만의 말씀", html);
    }

    [Fact]
    public async Task 숫자는_모든_부서_합계만_보이고_부서_이름은_없다()
    {
        var a = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var b = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        string Acc(string e) => InMemoryDb.SeedAccount(_db, e + "@example.com", e).UserName!;
        var k1 = Acc("k1"); var k2 = Acc("k2"); var k3 = Acc("k3");
        InMemoryDb.SeedMembership(_db, a.Id, k1, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, b.Id, k1, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, b.Id, k2, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, b.Id, k3, MembershipStatus.Requested, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.MissionActivities.Add(new MissionActivity { TenantId = a.Id, UserName = k1, MissionItem = "Word.Me", MissionDate = Jan5, Points = 1, PerformedAt = new DateTime(2027, 1, 4, 23, 0, 0, DateTimeKind.Utc) });
            var at = new DateTime(2027, 1, 4, 23, 30, 0, DateTimeKind.Utc);
            raw.ReadingChapterReads.Add(new ReadingChapterRead { UserName = k1, RoundNo = 1, BookId = 1, Chapter = 1, ReadAt = at });
            raw.ReadingChapterReads.Add(new ReadingChapterRead { UserName = k2, RoundNo = 1, BookId = 1, Chapter = 1, ReadAt = at });
            raw.ReadingChapterReads.Add(new ReadingChapterRead { UserName = k2, RoundNo = 1, BookId = 1, Chapter = 2, ReadAt = new DateTime(2026, 12, 30, 0, 0, 0, DateTimeKind.Utc) });
            raw.SaveChanges();
        }
        var html = await Page();
        Assert.Contains(">2<small>곳</small></b><span>함께하는 부서</span>", html);
        Assert.Contains(">2<small>명</small></b><span>참여하는 사람</span>", html);
        Assert.Contains(">2<small>명</small></b><span>오늘 함께한 사람</span>", html);
        Assert.Contains("class=\"vb-big num\">2<small>장</small>", html);
        Assert.Contains("최근 7일 참여", html);
        Assert.DoesNotContain("잠실새내교회 유년부", html);
        Assert.DoesNotMatch(@"\bk1\b", html);
    }

    [Fact]
    public async Task 네_프로그램과_로그인_가입이_보이고_관리자_로그인은_로그인_화면에만_있다()
    {
        var html = await Page();
        Assert.Contains("<b>성경미션</b>", html);
        Assert.Contains("<b>성경통독</b>", html);
        Assert.Contains("<b>성경필사</b>", html);
        Assert.Contains("<b>기도365</b>", html);
        Assert.Contains("<a class=\"person-btn\" href=\"/account/login\"", html);   // 로그인은 사람 아이콘 하나
        Assert.DoesNotContain("class=\"lp-login\"", html);                          // 큰 로그인 카드는 없앴다
        Assert.Contains("<a class=\"vb-item\" href=\"/account/login\"", html);      // 미션 칸도 로그인
        Assert.DoesNotContain("관리자 로그인", html);                    // 2026-11-24 사용자: 첫 화면 맨 아래 「관리자 · 부서 대표 학생 로그인」 상자를 지우고
        Assert.DoesNotContain("href=\"/account/admin-login\"", html);   // 플랫폼 관리자는 로그인 화면(/account/login)에만
    }
}
