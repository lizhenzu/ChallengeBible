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
/// 성경통독 완독 선물(확정 시안 완독-선물, ADR 0094 E · 정한 것 16, R-289): 학생 · 새친구가 회차를 완독하면 그 회차에 하나, 완독 뒤 30일 안에 고른다.
/// 30일이 지난 완독에는 생기지 않고(질문 1 → 1), 어른은 박수만. 금액 1독 10,000 · 2독 15,000 · 3독부터 20,000(부서 표가 먼저). 오늘 = 2027-01-05(서울).
/// </summary>
public partial class ReadingGiftTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids;
    private readonly string _kid, _old, _teacher;

    public ReadingGiftTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(_time);
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        _kid = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "dongseok@example.com", "권동석").UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        _old = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "yebit@example.com", "안예빛").UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        _teacher = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호").UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin).UserName;
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        void Round(string user, int no, DateTime? done) => raw.ReadingRounds.Add(new ReadingRound { UserName = user, RoundNo = no, StartedAt = new DateTime(2026, 1, 1), CompletedAt = done });
        Round(_kid, 1, new DateTime(2026, 12, 20, 3, 0, 0));   // 서울 12월 20일 → 1월 19일까지
        Round(_kid, 2, new DateTime(2027, 1, 4, 16, 0, 0));    // 서울 1월 5일 01:00 → 2월 4일까지
        Round(_kid, 3, null);
        Round(_old, 1, new DateTime(2026, 11, 1));             // 30일이 지남 → 안 생김
        Round(_teacher, 1, new DateTime(2027, 1, 1));          // 어른은 박수만
        raw.GiftItems.Add(new GiftItem { Name = "성경 필사 노트", Price = 9000, CoupangUrl = "https://www.coupang.com/vp/products/1", Status = GiftItemStatus.Active });
        raw.GiftItems.Add(new GiftItem { Name = "비싼 레고", Price = 18000, CoupangUrl = "https://www.coupang.com/vp/products/2", Status = GiftItemStatus.Active });
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private List<Gift> Gifts() { using var raw = InMemoryDb.Raw(_db); return raw.Gifts.Where(g => g.Kind == GiftKinds.Reading).OrderBy(g => g.SourceKey).ToList(); }

    [Fact]
    public void R289_회차_금액은_표에_없으면_마지막_값()
    {
        int[] a = [10000, 15000, 20000];
        Assert.Equal([10000, 15000, 20000, 20000], new[] { 1, 2, 3, 7 }.Select(n => ReadingGiftService.AmountFor(n, a)).ToArray());
    }

    [Fact]
    public async Task R289_학생이_완독하면_회차마다_생기고_30일_지난_완독과_어른은_없다_통독_홈_카드()
    {
        var c = await Login("dongseok@example.com");
        var home = await c.GetStringAsync("/kids/reading");
        Assert.Contains("완독 선물을 골라요", home);
        Assert.Contains("1독 완독", home);
        Assert.Contains("2독 완독", home);
        var g = Gifts();
        Assert.Equal(2, g.Count);   // 권동석 1 · 2회차만(안예빛 30일 지남, 윤동호 교사)
        Assert.Equal((_kid, "R1", 10000, new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 19)), (g[0].UserName, g[0].SourceKey, g[0].MaxAmount, g[0].OpenFrom!.Value, g[0].OpenUntil!.Value));
        Assert.Equal(("R2", 15000, new DateOnly(2027, 2, 4)), (g[1].SourceKey, g[1].MaxAmount, g[1].OpenUntil!.Value));
        Assert.Contains("완독 선물을 골라요", await c.GetStringAsync("/kids/word-mission"));   // 미션 홈에도
        await c.GetStringAsync("/kids/reading");
        Assert.Equal(2, Gifts().Count);   // 다시 열어도 한 번만
    }

    [Fact]
    public async Task R289_부서_금액표가_먼저_고르기는_금액_안의_상품만()
    {
        using (var raw = InMemoryDb.Raw(_db)) { raw.GiftReadingAmounts.Add(new GiftReadingAmount { TenantId = _kids.Id, RoundNo = 1, Amount = 12000 }); raw.SaveChanges(); }
        var c = await Login("dongseok@example.com");
        await c.GetStringAsync("/kids/reading");
        var g = Gifts()[0];
        Assert.Equal(12000, g.MaxAmount);
        var page = await c.GetStringAsync($"/kids/gifts/birthday/{g.Id}");
        Assert.Contains("완독 선물 고르기", page);
        Assert.Contains("1독 완독", page);
        Assert.Contains("← 통독 홈", page);
        Assert.Contains("성경 필사 노트", page);
        Assert.DoesNotContain("비싼 레고", page);
        Assert.DoesNotContain("9,000", page);
    }

    [Fact]
    public async Task R289_기한이_지나면_기한_지남_관리자가_다시_열면_7일()
    {
        var admin = await Login("admin@example.com");
        var pick = await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Contains("완독 1독", pick);
        Assert.Contains("12월 20일 완독", pick);
        _time.Now = new DateTimeOffset(2027, 1, 20, 0, 0, 0, TimeSpan.Zero);
        Assert.Contains("기한 지남", await admin.GetStringAsync("/kids/admin/gifts?s=pick"));
        var id = Gifts()[0].Id;
        Assert.Equal(GiftStates.Expired, Gifts()[0].State);
        var token = TokenRx().Match(await admin.GetStringAsync("/kids/admin/gifts?s=pick")).Groups[1].Value;
        await admin.PostAsync("/kids/admin/gifts?handler=Reopen", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["id"] = id.ToString() }));
        Assert.Equal((GiftStates.Open, new DateOnly(2027, 1, 27)), (Gifts()[0].State, Gifts()[0].OpenUntil!.Value));
    }
}
