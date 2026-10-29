using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>로그인 전 첫 화면(확정 시안 로그인전-첫화면-v2, 0061). 오늘 = 2027-01-05 (서울).</summary>
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
    public async Task 공용_말씀이_있으면_오늘의_말씀_카드가_보인다()
    {
        ContentSeed.Word(_db, Jan5);
        ContentSeed.Verse(_db, 1);
        ContentSeed.Prayer(_db, Jan5);
        var html = await Page();
        Assert.Contains("<small>오늘의 기도</small><b>신뢰 · 잠언 3장 5~6절</b>", html);    // 성품 + 본문 범위(2026-10-25 결정)
        Assert.Contains("aria-label=\"오늘의 말씀\"", html);
        Assert.Contains("창세기 24:1 · 오늘 읽을 범위 창세기 24장 1~9절", html);
        Assert.Contains("이번 주 암송 (1/3 ~ 1/9)", html);
        Assert.Contains("© 2027 잠실새내 · 주일학교", html);                       // 올해 = 시스템 날짜
    }

    [Fact]
    public async Task 부서_덮어쓰기_말씀은_보이지_않고_공용이_없으면_카드를_숨긴다()
    {
        var kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        ContentSeed.Word(_db, Jan5, kids.Id, title: "부서만의 말씀");
        var html = await Page();
        Assert.DoesNotContain("aria-label=\"오늘의 말씀\"", html);                // 0061 결정 1: 공용만
        Assert.DoesNotContain("부서만의 말씀", html);
    }

    [Fact]
    public async Task 숫자는_모든_부서_합계만_보이고_부서_이름은_없다()
    {
        var a = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var b = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        int Acc(string e) { using var raw = InMemoryDb.Raw(_db); var x = new Account { Email = e + "@example.com", DisplayName = e, PasswordHash = "-" }; raw.Accounts.Add(x); raw.SaveChanges(); return x.Id; }
        var k1 = Acc("k1"); var k2 = Acc("k2"); var k3 = Acc("k3");
        var m1 = InMemoryDb.SeedMembership(_db, a.Id, k1, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, b.Id, k1, MembershipStatus.Approved, Roles.Participant);   // 두 부서 소속도 한 사람
        InMemoryDb.SeedMembership(_db, b.Id, k2, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, b.Id, k3, MembershipStatus.Requested, Roles.Participant);  // 승인 전은 세지 않음
        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = new Participant { TenantId = a.Id, MembershipId = m1.Id };
            raw.Participants.Add(p);
            raw.SaveChanges();
            raw.Activities.Add(new Activity { TenantId = a.Id, ParticipantId = p.Id, MissionItem = "Word.Me", MissionDate = Jan5, Points = 1, PerformedUtc = new DateTime(2027, 1, 4, 23, 0, 0, DateTimeKind.Utc) });
            var at = new DateTime(2027, 1, 4, 23, 30, 0, DateTimeKind.Utc);   // 서울 1월 5일 아침
            raw.ChapterReads.Add(new ChapterRead { AccountId = k1, RoundNo = 1, BookId = 1, Chapter = 1, ReadUtc = at });   // 미션도 한 k1 — 오늘 한 명으로
            raw.ChapterReads.Add(new ChapterRead { AccountId = k2, RoundNo = 1, BookId = 1, Chapter = 1, ReadUtc = at });
            raw.ChapterReads.Add(new ChapterRead { AccountId = k2, RoundNo = 1, BookId = 1, Chapter = 2, ReadUtc = new DateTime(2026, 12, 30, 0, 0, 0, DateTimeKind.Utc) });   // 지난해
            raw.SaveChanges();
        }
        var html = await Page();
        Assert.Contains("2<i>곳</i>", html);          // 운영 중 부서
        Assert.Contains("2<i>명</i></b><small>참여하는 사람", html);
        Assert.Contains("2<i>명</i></b><small>오늘 말씀과 함께한 사람", html);
        Assert.Contains("2<i>장</i></b><small>올해 함께 읽은 성경", html);
        Assert.Contains("<span class=\"num\">2</span><i style=\"height:94px\"></i>오늘", html);
        Assert.DoesNotContain("잠실새내교회", html);   // 부서 이름은 내보내지 않음
        Assert.DoesNotContain("k1", html);
    }

    [Fact]
    public async Task 미션_점수와_퀴즈_횟수는_규칙에서_읽는다()
    {
        var html = await Page();
        Assert.Contains("내가 읽었어요<b>+1점</b>", html);
        Assert.Contains("기회는 하루 2번", html);
        Assert.Contains("3,000점이 되면", html);        // 첫 축하 단계
        Assert.DoesNotContain("가족 선물", html);                  // 선물 · 보상은 부서마다 다르고 확정 전이라 첫 화면에 쓰지 않음
        Assert.DoesNotContain("class=\"cheer\"", html);
        Assert.Contains("href=\"/account/signup\"", html);
        Assert.Contains("href=\"/account/login\"", html);
    }
}
