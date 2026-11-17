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
/// 새친구 · 전도 선물(확정 시안 전도-선물, ADR 0094 C · 정한 것 15, R-286 고침): 새친구가 언제든 출석을 모두 4번 채우면 새친구 본인과 데려온 아이에게 함께.
/// 데려온 아이는 적힌 이름과 같은 학생이 명부에 한 명이면 저절로, 아니면 선생님이 고른다. 오늘 = 2027-01-05(서울).
/// 고성윤 = 권동석이 데려온 새친구(1월 2번 · 10월 1번 · 1월 1번 = 4번), 김하늘 = 3번, 박하나 = "김민준"(같은 이름 2명)이 데려옴, 이스스 = 스스로 옴.
/// </summary>
public partial class NewcomerGiftTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly Dictionary<string, int> _p = [];

    public NewcomerGiftTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var kid = InMemoryDb.SeedAccount(_db, "dongseok@example.com", "권동석");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        Person P(string name, string? role = "T1001", string? userId = null, DateOnly? first = null, bool? voluntary = null, string? ev = null) =>
            new() { TenantId = _kids.Id, Name = name, RoleCode = role, UserId = userId, NewcomerFirstVisit = first, NewcomerVoluntary = voluntary, NewcomerEvangelist = ev, CreatedUtc = DateTime.UtcNow };
        var people = new[]
        {
            P("권동석", null, kid.Id), P("김민준"), P("김민준"),
            P("고성윤", "T1020", first: new DateOnly(2026, 1, 4), voluntary: false, ev: " 권동석 "),
            P("김하늘", "T1020", first: new DateOnly(2026, 1, 4), voluntary: false, ev: "권동석"),
            P("박하나", "T1020", first: new DateOnly(2026, 1, 4), voluntary: false, ev: "김민준"),
            P("이스스", "T1020", first: new DateOnly(2026, 1, 4), voluntary: true),
        };
        raw.People.AddRange(people);
        raw.SaveChanges();
        foreach (var p in people) _p.TryAdd(p.Name, p.Id);
        var four = new[] { new DateOnly(2026, 1, 4), new DateOnly(2026, 1, 11), new DateOnly(2026, 10, 4), new DateOnly(2027, 1, 3) };
        void Visits(string name, IEnumerable<DateOnly> days) { foreach (var d in days) raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _p[name], Sunday = d, CreatedUtc = DateTime.UtcNow }); }
        Visits("고성윤", four); Visits("김하늘", four.Take(3)); Visits("박하나", four); Visits("이스스", four);
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _p["김하늘"], Sunday = new DateOnly(2025, 12, 28), CreatedUtc = DateTime.UtcNow });   // 첫 방문 전 출석은 세지 않는다
        raw.GiftItems.Add(new GiftItem { Name = "보드게임", Price = 9500, CoupangUrl = "https://www.coupang.com/vp/products/1", Status = GiftItemStatus.Active });
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

    private List<Gift> Gifts() { using var raw = InMemoryDb.Raw(_db); return raw.Gifts.Where(g => g.Kind == GiftKinds.Newcomer || g.Kind == GiftKinds.Invite).ToList(); }

    [Fact]
    public async Task R286_출석을_모두_4번_채우면_새친구와_데려온_아이에게_함께_생기고_스스로_왔으면_새친구만()
    {
        var admin = await Login("admin@example.com");
        var pick = await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        var gifts = Gifts();
        string Key(string n) => NewcomerGiftService.SourceKey(_p[n]);
        Assert.Equal(3, gifts.Count(g => g.Kind == GiftKinds.Newcomer));   // 고성윤 · 박하나 · 이스스 (김하늘은 3번)
        Assert.DoesNotContain(gifts, g => g.SourceKey == Key("김하늘"));
        var nc = gifts.Single(g => g.Kind == GiftKinds.Newcomer && g.SourceKey == Key("고성윤"));
        Assert.Equal((_p["고성윤"], (string?)null, 1000, 15000, (DateOnly?)null), (nc.PersonId!.Value, nc.UserName, nc.MinAmount, nc.MaxAmount, nc.OpenUntil));   // 기한 없음
        var inv = gifts.Single(g => g.Kind == GiftKinds.Invite);   // 고성윤 → 권동석(이름이 한 명과 맞음, 앞뒤 빈칸 무시). 박하나 → 김민준 2명이라 아직, 이스스는 스스로
        Assert.Equal((_p["권동석"], Key("고성윤")), (inv.PersonId!.Value, inv.SourceKey));
        Assert.NotNull(inv.UserName);

        Assert.Contains("고성윤을 데려옴", pick);
        Assert.Contains("전도한 아이를 골라 주세요", pick);
        Assert.Contains("명부에 같은 이름이 2명이에요", pick);

        await admin.GetStringAsync("/kids/admin/gifts?s=pick");   // 다시 열어도 한 번만
        Assert.Equal(4, Gifts().Count);
    }

    [Fact]
    public async Task R286_전도한_아이는_계정이_있으면_미션_홈_카드로_고른다()
    {
        var c = await Login("dongseok@example.com");
        var home = await c.GetStringAsync("/kids/word-mission");
        Assert.Contains("친구 선물을 골라요", home);
        Assert.Contains("고성윤을 데려와 줘서 고마워요!", home);
        var id = Gifts().Single(g => g.Kind == GiftKinds.Invite).Id;
        var page = await c.GetStringAsync($"/kids/gifts/birthday/{id}");
        Assert.Contains("전도 선물 고르기", page);
        Assert.DoesNotContain("9,500", page);
        await Post(c, $"/kids/gifts/birthday/{id}", $"/kids/gifts/birthday/{id}", new() { ["item"] = ItemId().ToString() });
        Assert.Equal(GiftStates.Picked, Gifts().Single(g => g.Id == id).State);
    }

    private int ItemId() { using var raw = InMemoryDb.Raw(_db); return raw.GiftItems.Single().Id; }

    [Fact]
    public async Task R286_새친구는_링크로_고른다()
    {
        var admin = await Login("admin@example.com");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        var id = Gifts().Single(g => g.Kind == GiftKinds.Newcomer && g.PersonId == _p["고성윤"]).Id;
        var res = await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Link", new() { ["id"] = id.ToString(), ["s"] = "pick" });
        var box = await admin.GetStringAsync(res.Headers.Location!.OriginalString);
        Assert.Contains("고성윤 새친구 선물 링크", box);
        var token = LinkRx().Match(box).Groups[1].Value;
        var parent = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var link = await parent.GetStringAsync($"/gift/{token}");
        Assert.Contains("고성윤 어린이<br />새친구 환영 선물이에요", link);   // 확정 시안 생일링크-부모안내 ③
        Assert.Contains("새친구 선물", link);
        await Post(parent, $"/gift/{token}", $"/gift/{token}", new() { ["item"] = ItemId().ToString() });
        Assert.Equal((GiftStates.Picked, "보드게임"), (Gifts().Single(g => g.Id == id).State, Gifts().Single(g => g.Id == id).ItemName));
    }

    [Fact]
    public async Task R286_이름이_애매하면_선생님이_고르고_건너뛰면_취소에서_되돌린다()
    {
        var admin = await Login("admin@example.com");
        var modal = await admin.GetStringAsync($"/kids/admin/gifts?s=pick&pick={_p["박하나"]}");
        Assert.Contains("박하나를 데려온 아이", modal);
        Assert.Equal(2, Regex.Matches(modal, "name=\"person\"").Count);   // 같은 이름 김민준 2명
        Assert.Contains("권동석", await admin.GetStringAsync($"/kids/admin/gifts?s=pick&pick={_p["박하나"]}&q2=권"));   // 이름으로 찾기

        await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=SkipPending", new() { ["newcomer"] = _p["박하나"].ToString() });
        Assert.DoesNotContain("전도한 아이를 골라 주세요", await admin.GetStringAsync("/kids/admin/gifts?s=pick"));
        var skipped = Gifts().Single(g => g.Kind == GiftKinds.Invite && g.State == GiftStates.Skipped);
        Assert.Contains("박하나(새친구)를 데려온 아이", await admin.GetStringAsync("/kids/admin/gifts?s=cancel"));
        await Post(admin, "/kids/admin/gifts?s=cancel", "/kids/admin/gifts?handler=Skip", new() { ["id"] = skipped.Id.ToString(), ["skip"] = "false" });
        Assert.Contains("전도한 아이를 골라 주세요", await admin.GetStringAsync("/kids/admin/gifts?s=pick"));   // 되돌리면 다시 고르기로

        var target = (await admin.GetStringAsync($"/kids/admin/gifts?s=pick&pick={_p["박하나"]}"));
        var minjun = Regex.Match(target, "name=\"person\" value=\"(\\d+)\"").Groups[1].Value;
        await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Assign", new() { ["newcomer"] = _p["박하나"].ToString(), ["person"] = minjun });
        var g = Gifts().Single(x => x.Kind == GiftKinds.Invite && x.SourceKey == NewcomerGiftService.SourceKey(_p["박하나"]));
        Assert.Equal((GiftStates.Open, int.Parse(minjun)), (g.State, g.PersonId!.Value));
        Assert.Contains("박하나를 데려옴", await admin.GetStringAsync("/kids/admin/gifts?s=pick"));
        Assert.DoesNotContain("전도한 아이를 골라 주세요", await admin.GetStringAsync("/kids/admin/gifts?s=pick"));
    }
}
