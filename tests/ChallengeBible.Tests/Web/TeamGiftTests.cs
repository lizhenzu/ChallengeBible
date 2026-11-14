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
/// 가족팀 · 친구팀 선물(확정 시안 가족팀-선물고르기, ADR 0094 F · 정한 것 11): R-290 금액(가족 1,000점 1만, 친구는 절반씩) · R-291 고르기 · 바꾸기 · 기한 · 다시 열기 ·
/// R-292 줬어요 때 시상 기록 + 잠금. 오늘 = 2027-01-05(서울).
/// </summary>
public partial class TeamGiftTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly Dictionary<string, string> _p = [];

    public TeamGiftTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        foreach (var (login, name) in new[] { ("cheolsu", "김철수"), ("younghee", "김영희"), ("haneul", "문하늘"), ("minji", "박민지"), ("other", "남다른") })
        {
            var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name);
            using (var raw = InMemoryDb.Raw(_db)) { var u = raw.Users.Single(x => x.Id == a.Id); u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234"); raw.SaveChanges(); }
            _p[login] = InMemoryDb.SeedMembership(_db, _kids.Id, a.UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        }
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        using (var raw = InMemoryDb.Raw(_db)) { var u = raw.Users.Single(x => x.Id == adm.Id); u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234"); raw.SaveChanges(); }
        InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private void Pts(string login, int points)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _p[login], MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = points, PerformedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private int Team(string name, bool friend, params string[] logins)
    {
        using var raw = InMemoryDb.Raw(_db);
        var t = new FamilyTeam { TenantId = _kids.Id, Name = name, AwardKey = name, TeamType = friend ? TeamTypes.Friend : TeamTypes.Family, InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
        var order = 0;
        foreach (var l in logins) t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p[l], SortOrder = order++, Relation = l == "younghee" ? FamilyRelation.Mother : FamilyRelation.Student });
        raw.FamilyTeams.Add(t);
        raw.SaveChanges();
        return t.Id;
    }

    private int Item(string name, int price)
    {
        using var raw = InMemoryDb.Raw(_db);
        var i = new GiftItem { Name = name, Price = price, CoupangUrl = "https://www.coupang.com/vp/products/9", Status = GiftItemStatus.Active };
        raw.GiftItems.Add(i);
        raw.SaveChanges();
        return i.Id;
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

    private static async Task<HttpResponseMessage> Post(HttpClient c, string tokenPage, string url, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync(tokenPage)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private Gift Single() { using var raw = InMemoryDb.Raw(_db); return raw.Gifts.Single(); }

    [Fact]
    public async Task R290_가족팀이_1000점을_넘으면_1만원_선물이_생기고_구성원_미션_홈에_카드()
    {
        Team("철수네", false, "cheolsu", "younghee");
        Pts("cheolsu", 700); Pts("younghee", 400);
        var c = await Login("cheolsu@example.com");
        var home = await c.GetStringAsync("/kids/word-mission");
        Assert.Contains("가족팀 1,000점 달성", home);
        Assert.Contains("가족 선물을 골라요", home);
        var g = Single();
        Assert.Equal((GiftKinds.Family, GiftStates.Open, 10000, new DateOnly(2027, 2, 4)), (g.Kind, g.State, g.MaxAmount, g.OpenUntil!.Value));   // 기한 30일
        Assert.Null(g.UserName);   // 팀에 하나
        Assert.Contains("가족팀 1,000점 달성", await (await Login("younghee@example.com")).GetStringAsync("/kids/word-mission"));
        Assert.DoesNotContain("가족 선물을 골라요", await (await Login("other@example.com")).GetStringAsync("/kids/word-mission"));
    }

    [Fact]
    public async Task R291_금액_안의_상품만_가격_없이_보이고_구성원_누구나_고르고_바꾼다()
    {
        Team("철수네", false, "cheolsu", "younghee");
        Pts("cheolsu", 1100);
        var cheap = Item("젠가 클래식", 8000);
        var other = Item("가족 퍼즐", 9500);
        Item("비싼 텐트", 15000);
        var c = await Login("cheolsu@example.com");
        await c.GetStringAsync("/kids/word-mission");
        var id = Single().Id;
        var page = await c.GetStringAsync($"/kids/gifts/team/{id}");
        Assert.Contains("젠가 클래식", page);
        Assert.DoesNotContain("비싼 텐트", page);
        Assert.DoesNotContain("8,000", page);   // 금액 · 가격 숨김(질문 1 → ③)
        Assert.DoesNotContain("10,000", page);
        Assert.Contains("김영희(엄마)", page);

        var mom = await Login("younghee@example.com");
        await Post(mom, $"/kids/gifts/team/{id}", $"/kids/gifts/team/{id}", new() { ["item"] = cheap.ToString() });
        Assert.Equal((GiftStates.Picked, "젠가 클래식"), (Single().State, Single().ItemName));
        Assert.Contains("김영희님이 골랐어요", await c.GetStringAsync($"/kids/gifts/team/{id}"));
        await Post(c, $"/kids/gifts/team/{id}?change=true", $"/kids/gifts/team/{id}", new() { ["item"] = other.ToString() });
        Assert.Equal("가족 퍼즐", Single().ItemName);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(["Open", "Pick", "Change"], raw.GiftLogs.OrderBy(l => l.Id).Select(l => l.Action).ToArray());

        var stranger = await Login("other@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/kids/gifts/team/{id}")).StatusCode);
    }

    [Fact]
    public async Task R292_줬어요_때_시상_기록과_잠금_되돌리면_기록도_지운다_산_뒤에는_못_바꾼다()
    {
        var team = Team("철수네", false, "cheolsu", "younghee");
        Pts("cheolsu", 1100);
        var cheap = Item("젠가 클래식", 8000);
        var c = await Login("cheolsu@example.com");
        await c.GetStringAsync("/kids/word-mission");
        var id = Single().Id;
        await Post(c, $"/kids/gifts/team/{id}", $"/kids/gifts/team/{id}", new() { ["item"] = cheap.ToString() });

        var admin = await Login("admin@example.com");
        var buy = await admin.GetStringAsync("/kids/admin/gifts");
        Assert.Contains("철수네 가족", buy);   // 팀 이름 + 가족(이미 "가족"으로 끝나면 그대로)
        Assert.Contains("가족팀 1,000점", buy);
        Assert.Contains("한도 10,000원", buy);
        var det = $"/kids/admin/gifts/{id}";
        await Post(admin, det, $"{det}?handler=Move", new() { ["act"] = "Purchase" });
        await Post(c, $"/kids/gifts/team/{id}", $"/kids/gifts/team/{id}", new() { ["item"] = cheap.ToString() });   // 산 뒤 바꾸기 → 안 됨
        Assert.Equal(GiftStates.Purchased, Single().State);
        Assert.DoesNotContain("다른 선물로 바꾸기", await c.GetStringAsync($"/kids/gifts/team/{id}"));

        await Post(admin, det, $"{det}?handler=Move", new() { ["act"] = "Deliver" });
        using (var raw = InMemoryDb.Raw(_db))
        {
            var a = raw.TeamAwards.Single();
            Assert.Equal((TeamTypes.Family, "철수네", 1000), (a.Kind, a.AwardKey, a.Threshold));
        }
        Assert.DoesNotContain("가족 선물을 골라요", await c.GetStringAsync("/kids/word-mission"));   // 받았으니 카드 없음 · 다시 생기지 않음
        using (var raw = InMemoryDb.Raw(_db)) Assert.Single(raw.Gifts);

        await Post(admin, det, $"{det}?handler=Move", new() { ["act"] = "Undo" });
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.TeamAwards);
    }

    [Fact]
    public async Task R290_친구팀은_절반씩_각자_고른다()
    {
        Team("하늘이네 친구팀", true, "haneul", "minji");
        Pts("haneul", 600); Pts("minji", 500);
        Item("스티커북", 4500);
        Item("필통", 6000);
        var c = await Login("haneul@example.com");
        Assert.Contains("내 친구팀 선물을 골라요", await c.GetStringAsync("/kids/word-mission"));
        using var raw = InMemoryDb.Raw(_db);
        var gifts = raw.Gifts.OrderBy(g => g.UserName).ToList();
        Assert.Equal(2, gifts.Count);
        Assert.All(gifts, g => Assert.Equal((GiftKinds.Friend, 5000), (g.Kind, g.MaxAmount)));
        var mine = gifts.Single(g => g.UserName == _p["haneul"]).Id;
        var page = await c.GetStringAsync($"/kids/gifts/team/{mine}");
        Assert.Contains("스티커북", page);
        Assert.DoesNotContain("필통", page);
        var friendGift = gifts.Single(g => g.UserName == _p["minji"]).Id;
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/kids/gifts/team/{friendGift}")).StatusCode);   // 친구 몫은 친구만
    }

    [Fact]
    public async Task R291_기한이_지나면_기한_지남_관리자가_다시_열면_7일()
    {
        Team("철수네", false, "cheolsu", "younghee");
        Pts("cheolsu", 1100);
        var c = await Login("cheolsu@example.com");
        await c.GetStringAsync("/kids/word-mission");
        using (var raw = InMemoryDb.Raw(_db)) { raw.Gifts.Single().OpenUntil = new DateOnly(2027, 1, 1); raw.SaveChanges(); }
        Assert.Contains("가족 선물을 고르지 못했어요", await c.GetStringAsync("/kids/word-mission"));
        Assert.Equal(GiftStates.Expired, Single().State);

        var admin = await Login("admin@example.com");
        var pick = await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Contains("기한 지남", pick);
        Assert.Contains("다시 열기(7일)", pick);
        await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Reopen", new() { ["id"] = Single().Id.ToString() });
        Assert.Equal((GiftStates.Open, new DateOnly(2027, 1, 12)), (Single().State, Single().OpenUntil!.Value));
        Assert.Contains("가족 선물을 골라요", await c.GetStringAsync("/kids/word-mission"));
    }
}
