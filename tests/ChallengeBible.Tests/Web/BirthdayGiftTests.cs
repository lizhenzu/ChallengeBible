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
/// 생일 선물(확정 시안 생일-선물, ADR 0094 D · 정한 것 14, R-288) + 로그인 없는 선물 링크(R-287). 오늘 = 2027-01-05(서울).
/// 김하준 = 계정 있는 학생(2월 1일 생일 → 1월 2일 ~ 25일), 박서윤 = 계정 없는 새친구(2월 3일), 윤동호 = 부서 관리자(교사, 2월 1일 — 안 받음).
/// </summary>
public partial class BirthdayGiftTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids;
    private readonly string _hajun;

    public BirthdayGiftTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(_time);
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var kid = InMemoryDb.SeedAccount(_db, "hajun@example.com", "김하준");
        _hajun = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "김하준", UserId = kid.Id, BirthMonth = 2, BirthDay = 1, CreatedUtc = DateTime.UtcNow });
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "박서윤", RoleCode = "T1020", BirthMonth = 2, BirthDay = 3, CreatedUtc = DateTime.UtcNow });
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "윤동호", UserId = adm.Id, BirthMonth = 2, BirthDay = 1, CreatedUtc = DateTime.UtcNow });
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "이도윤", RoleCode = "T1001", BirthMonth = 3, BirthDay = 20, CreatedUtc = DateTime.UtcNow });   // 아직 멀다
        foreach (var (n, p) in new[] { ("싼 연필", 500), ("곰돌이 열쇠고리", 3000), ("스티커북", 5000), ("비싼 레고", 6000) })
            raw.GiftItems.Add(new GiftItem { Name = n, Price = p, CoupangUrl = "https://www.coupang.com/vp/products/1", Status = GiftItemStatus.Active });
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();
    [GeneratedRegex("/gift/([A-Za-z0-9]{32})")]
    private static partial Regex LinkRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string tokenPage, string url, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync(tokenPage)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private Gift GiftOf(string name)
    {
        using var raw = InMemoryDb.Raw(_db);
        var pid = raw.People.Single(p => p.Name == name).Id;
        return raw.Gifts.Single(g => g.PersonId == pid);
    }

    private int ItemId(string name) { using var raw = InMemoryDb.Raw(_db); return raw.GiftItems.Single(i => i.Name == name).Id; }

    [Fact]
    public void R288_고르는_기간은_생일_30일_전부터_7일_전까지_2월29일생은_평년_28일()
    {
        var today = new DateOnly(2027, 1, 5);
        Assert.Equal(new DateOnly(2027, 2, 1), BirthdayGiftService.WindowBirthday(2, 1, today, 30, 7));
        Assert.Null(BirthdayGiftService.WindowBirthday(1, 10, today, 30, 7));   // 1월 3일에 끝남
        Assert.Null(BirthdayGiftService.WindowBirthday(3, 20, today, 30, 7));   // 아직 멀다
        Assert.Equal(new DateOnly(2027, 1, 20), BirthdayGiftService.WindowBirthday(1, 20, new DateOnly(2026, 12, 25), 30, 7));   // 1월 생일은 전해 12월부터
        Assert.Equal(new DateOnly(2027, 2, 28), BirthdayGiftService.WindowBirthday(2, 29, new DateOnly(2027, 2, 1), 30, 7));
        Assert.Equal("B2027-0201", BirthdayGiftService.SourceKey(new DateOnly(2027, 2, 1)));
        Assert.Equal(new DateOnly(2027, 2, 1), BirthdayGiftService.BirthdayOf("B2027-0201-r270105000000"));
    }

    [Fact]
    public async Task R288_생일_30일_전이면_학생_새친구_본인에게_생기고_교사는_안_받는다_미션_홈_카드()
    {
        var c = await Login("hajun@example.com");
        var home = await c.GetStringAsync("/kids/word-mission");
        Assert.Contains("생일 선물을 골라요", home);
        Assert.Contains("2월 1일 생일", home);
        Assert.Contains("1월 25일까지", home);   // 생일 7일 전

        using var raw = InMemoryDb.Raw(_db);
        var gifts = raw.Gifts.Where(g => g.Kind == GiftKinds.Birthday).ToList();
        Assert.Equal(2, gifts.Count);   // 김하준 · 박서윤(새친구, 계정 없음) — 윤동호(교사) · 이도윤(아직 멀다) 없음
        var hajun = GiftOf("김하준");
        Assert.Equal((_hajun, GiftStates.Open, 1000, 5000, new DateOnly(2027, 1, 2), new DateOnly(2027, 1, 25), "B2027-0201"),
            (hajun.UserName, hajun.State, hajun.MinAmount, hajun.MaxAmount, hajun.OpenFrom!.Value, hajun.OpenUntil!.Value, hajun.SourceKey));
        Assert.Null(GiftOf("박서윤").UserName);

        await c.GetStringAsync("/kids/word-mission");   // 다시 열어도 한 해 한 번
        using var raw2 = InMemoryDb.Raw(_db);
        Assert.Equal(2, raw2.Gifts.Count(g => g.Kind == GiftKinds.Birthday));
    }

    [Fact]
    public async Task R288_금액_안의_상품만_가격_없이_고르고_사기_전까지_바꾼다()
    {
        var c = await Login("hajun@example.com");
        await c.GetStringAsync("/kids/word-mission");
        var id = GiftOf("김하준").Id;
        var page = await c.GetStringAsync($"/kids/gifts/birthday/{id}");
        Assert.Contains("곰돌이 열쇠고리", page);
        Assert.Contains("스티커북", page);
        Assert.DoesNotContain("싼 연필", page);   // 1,000원 아래
        Assert.DoesNotContain("비싼 레고", page);   // 5,000원 위
        Assert.DoesNotContain("3,000", page);

        await Post(c, $"/kids/gifts/birthday/{id}", $"/kids/gifts/birthday/{id}", new() { ["item"] = ItemId("곰돌이 열쇠고리").ToString() });
        Assert.Equal((GiftStates.Picked, "곰돌이 열쇠고리"), (GiftOf("김하준").State, GiftOf("김하준").ItemName));
        await Post(c, $"/kids/gifts/birthday/{id}?change=true", $"/kids/gifts/birthday/{id}", new() { ["item"] = ItemId("스티커북").ToString() });
        Assert.Equal("스티커북", GiftOf("김하준").ItemName);
        Assert.Contains("준비 중", await c.GetStringAsync("/kids/word-mission"));

        Assert.Equal(HttpStatusCode.NotFound, (await (await Login("admin@example.com")).GetAsync($"/kids/gifts/birthday/{id}")).StatusCode);   // 남의 생일 선물
    }

    [Fact]
    public async Task R287_관리자가_링크를_만들면_로그인_없이_고르고_새_링크면_옛_링크는_막힌다()
    {
        var admin = await Login("admin@example.com");
        var pick = await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Contains("생일 2.3", pick);
        Assert.Contains("계정 없음", pick);
        var id = GiftOf("박서윤").Id;
        var res = await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Link", new() { ["id"] = id.ToString(), ["s"] = "pick" });
        var box = await admin.GetStringAsync(res.Headers.Location!.OriginalString);
        Assert.Contains("박서윤 생일 선물 링크", box);
        var token = LinkRx().Match(box).Groups[1].Value;
        Assert.Equal(token, GiftOf("박서윤").LinkToken);

        var parent = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });   // 로그인 없음
        var link = await parent.GetStringAsync($"/gift/{token}");
        Assert.Contains("박서윤 어린이 생일 선물", link);
        Assert.Contains("스티커북", link);
        Assert.DoesNotContain("비싼 레고", link);
        await Post(parent, $"/gift/{token}", $"/gift/{token}", new() { ["item"] = ItemId("스티커북").ToString() });
        var g = GiftOf("박서윤");
        Assert.Equal((GiftStates.Picked, "스티커북"), (g.State, g.ItemName));
        Assert.Contains("준비 중이에요", await parent.GetStringAsync($"/gift/{token}"));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(BirthdayGiftService.LinkActor, raw.GiftLogs.Where(l => l.GiftId == id && l.Action == GiftActions.Pick).Single().ActorUserName);

        await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Link", new() { ["id"] = id.ToString(), ["fresh"] = "true", ["s"] = "buy" });
        Assert.NotEqual(token, GiftOf("박서윤").LinkToken);
        Assert.Contains("열 수 없는 링크예요", await parent.GetStringAsync($"/gift/{token}"));
        Assert.Contains("열 수 없는 링크예요", await parent.GetStringAsync("/gift/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

        await Post(admin, "/kids/admin/gifts?s=buy", "/kids/admin/gifts?handler=Move", new() { ["id"] = id.ToString(), ["act"] = "Purchase", ["s"] = "buy" });
        var fresh = GiftOf("박서윤").LinkToken!;
        var after = await parent.GetStringAsync($"/gift/{fresh}");
        Assert.Contains("선생님이 준비했어요", after);
        Assert.DoesNotContain("다른 선물로 바꾸기", after);   // 산 뒤에는 바꿀 수 없다
    }

    [Fact]
    public async Task R288_건너뛰기는_취소에서_되돌리고_기한이_지나면_다시_열기_7일()
    {
        var admin = await Login("admin@example.com");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        var id = GiftOf("김하준").Id;
        await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Skip", new() { ["id"] = id.ToString(), ["skip"] = "true" });
        Assert.Equal(GiftStates.Skipped, GiftOf("김하준").State);
        Assert.Contains("건너뜀", await admin.GetStringAsync("/kids/admin/gifts?s=cancel"));
        Assert.DoesNotContain("생일 선물을 골라요", await (await Login("hajun@example.com")).GetStringAsync("/kids/word-mission"));
        await Post(admin, "/kids/admin/gifts?s=cancel", "/kids/admin/gifts?handler=Skip", new() { ["id"] = id.ToString(), ["skip"] = "false" });
        Assert.Equal(GiftStates.Open, GiftOf("김하준").State);

        _time.Now = new DateTimeOffset(2027, 1, 26, 0, 0, 0, TimeSpan.Zero);   // 기한(1월 25일) 지남
        Assert.Contains("기한 지남", await admin.GetStringAsync("/kids/admin/gifts?s=pick"));
        Assert.Equal(GiftStates.Expired, GiftOf("김하준").State);
        await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Reopen", new() { ["id"] = id.ToString() });
        Assert.Equal((GiftStates.Open, new DateOnly(2027, 2, 2)), (GiftOf("김하준").State, GiftOf("김하준").OpenUntil!.Value));
    }

    [Fact]
    public async Task R297_부서_카드에는_생일_선물을_생일_주에만_보인다()
    {
        var c = await Login("hajun@example.com");
        await c.GetStringAsync("/kids/word-mission");
        var id = GiftOf("김하준").Id;
        await Post(c, $"/kids/gifts/birthday/{id}", $"/kids/gifts/birthday/{id}", new() { ["item"] = ItemId("스티커북").ToString() });
        Assert.DoesNotContain("스티커북", await c.GetStringAsync("/kids/gift-board"));   // 1월 5일: 생일(2월 1일) 주가 아님

        _time.Now = new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero);   // 1월 31일(일) ~ 2월 6일(토) 주
        Assert.Contains("스티커북", await c.GetStringAsync("/kids/gift-board"));
    }
}
