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

/// <summary>
/// 선물 구매 · 전달 처리(확정 시안 관리-선물처리, ADR 0094 G): R-293 관리자만 샀어요 · 줬어요 · 되돌리기, R-284 관리자 취소(점수 돌려줌),
/// R-295 이력, R-297 부서 카드(받은 지 30일까지, 가격 없음). 오늘 = 2027-01-05(서울).
/// </summary>
public partial class GiftOrderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public GiftOrderTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        using (var rawIntro = InMemoryDb.Raw(_db)) { rawIntro.TenantIntros.Add(new TenantIntro { TenantId = _kids.Id, Title = "환영", Body = "소개", IsActive = true, UpdatedUtc = DateTime.UtcNow }); rawIntro.SaveChanges(); }   // 선물 알림은 웰컴(소개 화면) 하단에 있다
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<(HttpClient C, string User)> Login(string who, string display, int points = 0, bool admin = false)
    {
        var a = InMemoryDb.SeedAccount(_db, who + "@example.com", display);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var u = raw.Users.Single(x => x.Id == a.Id);
            u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
            raw.SaveChanges();
        }
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, a.UserName!, MembershipStatus.Approved, admin ? [Roles.Teacher, Roles.TenantAdmin] : [Roles.Participant]);
        if (points > 0)
        {
            using var raw = InMemoryDb.Raw(_db);
            raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = m.UserName, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = points, PerformedAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = a.UserName!, ["Password"] = "1234",
        }));
        return (c, m.UserName);
    }

    private int Item(string name, int price)
    {
        using var raw = InMemoryDb.Raw(_db);
        var i = new GiftItem { Name = name, Price = price, CoupangUrl = "https://www.coupang.com/vp/products/77", Status = GiftItemStatus.Active };
        raw.GiftItems.Add(i);
        raw.SaveChanges();
        return i.Id;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string tokenPage, string url, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync(tokenPage)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    /// <summary>학생 안예빛(700점)이 색연필(6,000원 = 400점, ADR 0104)을 고른 상태.</summary>
    private async Task<(HttpClient Kid, HttpClient Admin, long Gift)> Picked()
    {
        var pens = Item("24색 색연필 세트", 6000);
        var (kid, _) = await Login("yebit", "안예빛", 700);
        await Post(kid, "/kids/gifts?all=true", "/kids/gifts?handler=Pick", new() { ["item"] = pens.ToString() });
        var (admin, _) = await Login("kidsadmin", "박지은", admin: true);
        using var raw = InMemoryDb.Raw(_db);
        return (kid, admin, raw.Gifts.Single().Id);
    }

    private static string GiftState(string db, long id) { using var raw = InMemoryDb.Raw(db); return raw.Gifts.Single(g => g.Id == id).State; }

    [Fact]
    public async Task R293_살_것에서_샀어요_줄_것에서_줬어요_아이_화면도_바뀐다()
    {
        var (kid, admin, id) = await Picked();
        var buy = await admin.GetStringAsync("/kids/admin/gifts");
        Assert.Contains("24색 색연필 세트", buy);
        Assert.Contains("안예빛", buy);
        Assert.Contains("6,000원", buy);   // 관리자에게는 가격이 보인다
        Assert.Contains("살 것 1건 합계", buy);
        Assert.Contains("https://www.coupang.com/vp/products/77", buy);

        var r = await Post(admin, "/kids/admin/gifts", "/kids/admin/gifts?handler=Move", new() { ["id"] = id.ToString(), ["act"] = "Purchase", ["s"] = "buy" });
        Assert.Equal("/kids/admin/gifts?s=buy&ok=Purchase", r.Headers.Location!.OriginalString);
        Assert.Equal(GiftStates.Purchased, GiftState(_db, id));
        Assert.Contains("24색 색연필 세트", await admin.GetStringAsync("/kids/admin/gifts?s=give"));
        var kidPage = await kid.GetStringAsync("/kids/gifts");
        Assert.Contains("선생님이 준비했어요", kidPage);
        Assert.DoesNotContain("선물 취소하기", kidPage);   // 산 뒤에는 아이가 취소하지 못한다(R-284)

        await Post(admin, "/kids/admin/gifts?s=give", "/kids/admin/gifts?handler=Move", new() { ["id"] = id.ToString(), ["act"] = "Deliver", ["s"] = "give" });
        Assert.Equal(GiftStates.Delivered, GiftState(_db, id));
        var home = await kid.GetStringAsync("/kids/word-mission");   // 받았으니 다음 선물을 고를 차례(정한 것 8)
        Assert.DoesNotContain("gk-card gk-got", home);
        Assert.Contains("100점 더 모으면 선물을 골라요", home);   // 700 − 400 = 300점, 가장 싼 선물 400점까지 100점(ADR 0104)
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(["Pick", "Purchase", "Deliver"], raw.GiftLogs.OrderBy(l => l.Id).Select(l => l.Action).ToArray());
    }

    [Fact]
    public async Task R298_되돌리기는_한_단계_앞으로_순서가_틀리면_바꾸지_않는다()
    {
        var (_, admin, id) = await Picked();
        var page = $"/kids/admin/gifts/{id}";
        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Deliver" });   // 사기 전에 줬어요 → 안 됨
        Assert.Equal(GiftStates.Picked, GiftState(_db, id));
        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Purchase" });
        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Deliver" });
        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Undo" });
        Assert.Equal(GiftStates.Purchased, GiftState(_db, id));
        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Undo" });
        Assert.Equal(GiftStates.Picked, GiftState(_db, id));
    }

    [Fact]
    public async Task R284_관리자는_산_뒤에도_취소하고_점수를_돌려준다()
    {
        var (kid, admin, id) = await Picked();
        var page = $"/kids/admin/gifts/{id}";
        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Purchase" });
        var r = await Post(admin, page, $"{page}?handler=Cancel", []);
        Assert.Equal($"{page}?ok=cancel", r.Headers.Location!.OriginalString);
        Assert.Equal(GiftStates.Cancelled, GiftState(_db, id));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.GiftPointUses);
        Assert.Contains("700", await kid.GetStringAsync("/kids/gifts"));
    }

    [Fact]
    public async Task R295_자세히에_받는_사람_가격_쓴_점수_이력()
    {
        var (_, admin, id) = await Picked();
        var page = await admin.GetStringAsync($"/kids/admin/gifts/{id}");
        Assert.Contains("안예빛", page);
        Assert.Contains("6,000원 · 400점", page);
        Assert.Contains("<b>안예빛</b> · 골랐어요", page);
        Assert.Contains("쿠팡에서 보기", page);
    }

    [Fact]
    public async Task R293_관리자가_아니면_선물_처리를_못_한다()
    {
        var (kid, _, id) = await Picked();
        Assert.NotEqual(HttpStatusCode.OK, (await kid.GetAsync("/kids/admin/gifts")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await kid.GetAsync($"/kids/admin/gifts/{id}")).StatusCode);
    }

    [Fact]
    public async Task R297_부서_카드는_다른_아이에게도_보이고_가격은_없으며_받은_지_30일이_지나면_빠진다()
    {
        var (_, admin, id) = await Picked();
        var (other, _) = await Login("haneul", "문하늘", 50);
        var home = await other.GetStringAsync("/kids?go=1");
        Assert.DoesNotContain("누가 선물 받나요", home);                                  // 홈 카드는 2026-11-22 확정으로 뺐다(ADR 0127)
        var board = await other.GetStringAsync("/kids/gift-board");
        Assert.Contains("누가 선물 받나요", board);
        Assert.Contains("안예빛", board);
        Assert.Contains("24색 색연필 세트 · 개인 선물", board);
        Assert.Contains(">골랐어요<", board);
        Assert.DoesNotContain("6,000", board);

        var page = $"/kids/admin/gifts/{id}";
        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Purchase" });
        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Deliver" });
        Assert.Contains(">받았어요<", await other.GetStringAsync("/kids/gift-board"));
        Assert.Contains("안예빛", await other.GetStringAsync("/kids/gift-board"));

        using (var raw = InMemoryDb.Raw(_db)) { raw.Gifts.Single().DeliveredUtc = DateTime.UtcNow.AddYears(-1); raw.SaveChanges(); }
        Assert.DoesNotContain("안예빛", await other.GetStringAsync("/kids/gift-board"));
    }

    [Fact]   // ADR 0138 · 2026-11-26 — 선물 알림은 웰컴 하단 한 줄(홈에서는 뺐다), 자세한 카드는 새 화면
    public async Task 웰컴_선물_알림은_한_줄이고_누르면_상세_화면으로_간다()
    {
        var (_, admin, id) = await Picked();
        var (other, _) = await Login("haneul", "문하늘", 50);

        Assert.DoesNotContain("vb-galert", await other.GetStringAsync("/kids?go=1"));   // 부서 홈에서는 뺐다
        var home = await other.GetStringAsync("/kids/welcome");
        Assert.Contains("class=\"wl-gift\"", home);                          // 챙길 선물 카드(2026-11-26 새 디자인)
        Assert.Contains("href=\"/kids/gift-status\"", home);                 // 누르면 새 화면
        Assert.Contains("신청자</em><strong><b>1</b>명", home);                // 신청자 → 준비중 → 구매완료
        Assert.Contains("준비중</em><strong><b>1</b>명", home);
        Assert.Contains("구매완료</em><strong><b>0</b>명", home);
        Assert.DoesNotContain("안예빛", home);                               // 홈에서는 이름을 꺼내지 않는다
        Assert.DoesNotContain("골랐어요", home);                             // 3단계 눈금은 상세에서
        Assert.DoesNotContain("6,000", home);                                // 가격은 없다(R-297)

        var detail = await other.GetStringAsync("/kids/gift-status");
        Assert.Contains("선물 대상자와 상태", detail);
        Assert.Contains("class=\"vb-gs-row\"", detail);                     // 사람마다 한 칸
        Assert.Contains("안예빛", detail);
        Assert.Contains("24색 색연필 세트", detail);
        Assert.Contains("골랐어요", detail);
        Assert.Contains("샀어요", detail);
        Assert.Contains("줬어요", detail);
        Assert.DoesNotContain("6,000", detail);                             // 가격은 없다(R-297)

        var page = $"/kids/admin/gifts/{id}";
        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Purchase" });
        var bought = await other.GetStringAsync("/kids/gift-status");
        Assert.Contains("class=\"st s2\"", bought);                          // 샀어요 = 옅은 초록

        await Post(admin, page, $"{page}?handler=Move", new() { ["act"] = "Deliver" });
        var given = await other.GetStringAsync("/kids/gift-status");
        Assert.Contains("class=\"st s3\"", given);                           // 받았어요 = 초록 바탕 · 흰 글자
        Assert.Contains("class=\"vb-gs-row done\"", given);
        Assert.Contains("role=\"list\"", given);
    }

    [Fact]   // ADR 0138 — 대상이 없으면 홈에 한 줄이 아예 없고, 손님에게는 로그인 안내
    public async Task 웰컴_선물_알림은_대상이_없으면_그리지_않고_손님에게는_안_보인다()
    {
        var (other, _) = await Login("haneul", "문하늘", 50);
        var home = await other.GetStringAsync("/kids/welcome");
        Assert.DoesNotContain("wl-gift", home);                              // 대상이 없으면 회원에게는 카드가 없다
        Assert.DoesNotContain("선물 대상자와 상태", home);

        var detail = await other.GetStringAsync("/kids/gift-status");
        Assert.Contains("아직 선물 대상자가 없어요", detail);                  // 상세는 빈 안내

        var anon = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var guest = await anon.GetStringAsync("/kids/welcome");
        Assert.Contains("wl-gift wl-is-locked", guest);                      // 손님에게는 자물쇠 카드(누르면 로그인, 2026-11-26 새 디자인)
        Assert.Contains("챙길 선물 (로그인 필요)", guest);
    }
}
