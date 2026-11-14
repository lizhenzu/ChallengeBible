using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 개인 선물 고르기(확정 시안 2판 말씀-선물고르기, ADR 0094 B · 정한 것 1 · 2 · 8): R-276 보이는 선물 · R-280 학생만 · R-281 정산 ·
/// R-284 본인 취소 · R-285 점수만 보임. 오늘 = 2027-01-05(서울).
/// </summary>
public partial class PersonalGiftTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;

    public PersonalGiftTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        _elem = InMemoryDb.SeedTenant(_db, "elem", "ELEM01");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    /// <summary>학생(T1001) 또는 교사로 소속 · 로그인. 올해 점수를 넣는다(1월 1일 이전 점수는 세지 않음).</summary>
    private async Task<(HttpClient Client, string User)> Login(string who, int points, bool student = true, int lastYear = 0)
    {
        var a = InMemoryDb.SeedAccount(_db, who + "@example.com", who);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var u = raw.Users.Single(x => x.Id == a.Id);
            u.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>().HashPassword(u, "1234");
            raw.SaveChanges();
        }
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, a.UserName!, MembershipStatus.Approved, student ? [Roles.Participant] : [Roles.Participant, Roles.Teacher]);
        using (var raw = InMemoryDb.Raw(_db))
        {
            if (points > 0) raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = m.UserName, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = points, PerformedAt = DateTime.UtcNow });
            if (lastYear > 0) raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = m.UserName, MissionDate = new DateOnly(2026, 12, 30), MissionItem = "Word.Me", Points = lastYear, PerformedAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value,
            ["Email"] = a.UserName!, ["Password"] = "1234",
        }));
        return (c, m.UserName);
    }

    private int Item(string name, int price, int? tenantId = null, string status = GiftItemStatus.Active)
    {
        using var raw = InMemoryDb.Raw(_db);
        var i = new GiftItem { Name = name, Price = price, CoupangUrl = "https://www.coupang.com/vp/products/1", TenantId = tenantId, Status = status };
        raw.GiftItems.Add(i);
        raw.SaveChanges();
        return i.Id;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string handler, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/kids/gifts?all=true")).Groups[1].Value;
        return await c.PostAsync("/kids/gifts?handler=" + handler, new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task R285_미션_홈_카드와_고르기_화면에는_점수만_보이고_가격은_숨긴다()
    {
        Item("곰돌이 열쇠고리", 5000);
        Item("24색 색연필 세트", 6000);
        Item("캐릭터 책가방", 15000);
        var (c, _) = await Login("siwoo", 340, lastYear: 500);   // 지난해 점수는 세지 않는다(정한 것 1)
        var home = await c.GetStringAsync("/kids/word-mission");
        Assert.Contains("gk-card gk-can", home);
        Assert.Contains("340점", home);
        Assert.Contains(">선물 고르기<", home);

        var page = await c.GetStringAsync("/kids/gifts");
        Assert.Contains("곰돌이 열쇠고리", page);
        Assert.Contains("100점", page);
        Assert.Contains("200점", page);
        Assert.DoesNotContain("캐릭터 책가방", page);   // 기본은 고를 수 있는 것만
        Assert.DoesNotContain("5,000", page);
        Assert.DoesNotContain("원", Regex.Replace(page, "<[^>]+>", " ").Replace("원하", ""));   // 가격(원) 없음
        var all = await c.GetStringAsync("/kids/gifts?all=true");
        Assert.Contains("캐릭터 책가방", all);
        Assert.Contains("<b class=\"num\">760점</b> 더 모으면", all);   // 15,000 → 1,100점, 340점이 있으니 760점 더
    }

    [Fact]
    public async Task R285_100점이_안_되면_몇_점_더_모으면_되는지()
    {
        var (c, _) = await Login("haneul", 60);
        var home = await c.GetStringAsync("/kids/word-mission");
        Assert.Contains("100점이 되면 선물을 골라요", home);
        Assert.Contains("<span class=\"num\">40</span>점 더 모으면 돼요", home);
    }

    [Fact]
    public async Task R281_고르면_필요한_점수를_쓰고_100점_미만_자투리는_지우며_미션_기록은_그대로()
    {
        var pens = Item("24색 색연필 세트", 6000);
        var (c, user) = await Login("siwoo", 340);
        var res = await Post(c, "Pick", new() { ["item"] = pens.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var g = raw.Gifts.Single();
            Assert.Equal((GiftKinds.Personal, GiftStates.Picked, "24색 색연필 세트", 6000, 200, 40), (g.Kind, g.State, g.ItemName, g.ItemPrice!.Value, g.PointsUsed!.Value, g.PointsBurned!.Value));
            var use = raw.GiftPointUses.Single();
            Assert.Equal((200, 40, true), (use.PointsUsed, use.PointsBurned, use.FirstBonus));
            Assert.Equal(340, raw.MissionActivities.Where(a => a.UserName == user).Sum(a => a.Points));   // 미션 기록 · 순위는 그대로
            Assert.Equal(GiftActions.Pick, raw.GiftLogs.Single().Action);
        }
        var page = await c.GetStringAsync("/kids/gifts");
        Assert.Contains("준비 중이에요", page);
        Assert.Contains("100점", page);   // 남은 점수
        Assert.Contains("gk-card gk-got", await c.GetStringAsync("/kids/word-mission"));
    }

    [Fact]
    public async Task 정한것8_받기_전에는_다음_선물을_못_고르고_받은_뒤에는_고른다()
    {
        var a = Item("곰돌이 열쇠고리", 5000);
        var b = Item("스프링 노트", 1000);
        var (c, _) = await Login("siwoo", 500);
        await Post(c, "Pick", new() { ["item"] = a.ToString() });
        await c.PostAsync("/kids/gifts?handler=Pick", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/kids/gifts")).Groups[1].Value, ["item"] = b.ToString(),
        }));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Single(raw.Gifts);

        using (var raw = InMemoryDb.Raw(_db)) { raw.Gifts.Single().State = GiftStates.Delivered; raw.SaveChanges(); }
        var page = await c.GetStringAsync("/kids/gifts");
        Assert.Contains("400", page);   // 500 − 100 = 400점 남음
        await Post(c, "Pick", new() { ["item"] = b.ToString() });
        using (var raw = InMemoryDb.Raw(_db))
        {
            var second = raw.Gifts.OrderBy(g => g.Id).Last();
            Assert.Equal((GiftStates.Picked, 100), (second.State, second.PointsUsed!.Value));   // 처음 보너스를 썼으니 1,000 크레딧 = 100점
            Assert.False(raw.GiftPointUses.OrderBy(u => u.Id).Last().FirstBonus);
        }
    }

    [Fact]
    public async Task R281_점수가_모자라면_고르지_못한다()
    {
        var bag = Item("캐릭터 책가방", 15000);
        var (c, _) = await Login("siwoo", 340);
        await Post(c, "Pick", new() { ["item"] = bag.ToString() });
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.Gifts);
    }

    [Fact]
    public async Task R284_준비_전에는_취소하면_점수가_돌아오고_준비한_뒤에는_못_한다()
    {
        var pens = Item("24색 색연필 세트", 6000);
        var (c, _) = await Login("siwoo", 340);
        await Post(c, "Pick", new() { ["item"] = pens.ToString() });
        long id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Gifts.Single().Id;
        Assert.Contains("선물 취소하기", await c.GetStringAsync("/kids/gifts"));
        await c.PostAsync("/kids/gifts?handler=Cancel", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/kids/gifts")).Groups[1].Value, ["id"] = id.ToString(),
        }));
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal(GiftStates.Cancelled, raw.Gifts.Single().State);
            Assert.Empty(raw.GiftPointUses);
        }
        Assert.Contains("340", await c.GetStringAsync("/kids/gifts"));

        await Post(c, "Pick", new() { ["item"] = pens.ToString() });
        using (var raw = InMemoryDb.Raw(_db)) { raw.Gifts.Single(g => g.State == GiftStates.Picked).State = GiftStates.Purchased; raw.SaveChanges(); }
        var page = await c.GetStringAsync("/kids/gifts");
        Assert.DoesNotContain("선물 취소하기", page);
        Assert.Contains("선생님이 준비했어요", page);
        long id2;
        using (var raw = InMemoryDb.Raw(_db)) id2 = raw.Gifts.Single(g => g.State == GiftStates.Purchased).Id;
        await c.PostAsync("/kids/gifts?handler=Cancel", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["id"] = id2.ToString(),
        }));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(GiftStates.Purchased, raw.Gifts.Single(g => g.Id == id2).State);
    }

    [Fact]
    public async Task R280_선생님은_선물_카드가_없고_고를_수_없다()
    {
        var a = Item("곰돌이 열쇠고리", 5000);
        var (c, _) = await Login("teacher", 400, student: false);
        var home = await c.GetStringAsync("/kids/word-mission");
        Assert.DoesNotContain("gk-card", home);
        Assert.Contains("data-kind=\"clap\"", home);   // 100점마다 축하 창만
        Assert.Contains("선물은 학생만 골라요", await c.GetStringAsync("/kids/gifts"));
        await c.PostAsync("/kids/gifts?handler=Pick", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/kids/word-mission")).Groups[1].Value, ["item"] = a.ToString(),
        }));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.Gifts);
    }

    [Fact]
    public async Task R276_숨긴_공통_상품_다른_부서_상품_삭제_대상은_안_보이고_우리_부서_상품은_보인다()
    {
        Item("공통 노트", 5000);
        var hidden = Item("숨긴 공통", 5000);
        Item("초등부 상품", 5000, _elem.Id);
        Item("유년부 상품", 5000, _kids.Id);
        Item("삭제 대상", 5000, status: GiftItemStatus.Review);
        using (var raw = InMemoryDb.Raw(_db)) { raw.GiftItemsHidden.Add(new GiftItemHidden { TenantId = _kids.Id, GiftItemId = hidden }); raw.SaveChanges(); }
        var (c, _) = await Login("siwoo", 340);
        var page = await c.GetStringAsync("/kids/gifts?all=true");
        Assert.Contains("공통 노트", page);
        Assert.Contains("유년부 상품", page);
        Assert.DoesNotContain("숨긴 공통", page);
        Assert.DoesNotContain("초등부 상품", page);
        Assert.DoesNotContain("삭제 대상", page);
    }

    [Fact]
    public async Task R283_부서_크레딧_표로_필요한_점수가_바뀐다()
    {
        Item("축구공", 12000);
        using (var raw = InMemoryDb.Raw(_db)) { raw.GiftCreditRates.Add(new GiftCreditRate { TenantId = _kids.Id, Credits = 10000, Amount = 12000 }); raw.SaveChanges(); }
        var (c, _) = await Login("siwoo", 1000);
        Assert.Contains("600점", await c.GetStringAsync("/kids/gifts"));   // 10,000 크레딧 = 600점(표가 없으면 12,000 → 800점)
    }
}
