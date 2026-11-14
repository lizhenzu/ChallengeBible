using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 선물 총비용(확정 시안 선물-총비용, ADR 0094 G-2 · 정한 것 13, R-294): [줬어요]를 누른 날 기준 받은 선물만, 고를 때 가격 합.
/// 부서 관리자 = 자기 부서, 플랫폼 관리자 = 모든 부서 · 부서별, 엑셀(.xlsx). 오늘 = 2027-01-05(서울).
/// </summary>
public partial class GiftCostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;
    private readonly string _kid;

    public GiftCostTests(WebApplicationFactory<Program> app)
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
        _kid = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "kid@example.com", "안예빛").UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호").UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _elem.Id, InMemoryDb.SeedAccount(_db, "elemadmin@example.com", "김초등").UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
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

    private void Gift(int tenantId, string kind, string state, int price, DateTime? deliveredUtc, string item = "젠가 클래식")
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.Gifts.Add(new Gift
        {
            TenantId = tenantId, Kind = kind, State = state, Year = 2027, UserName = _kid, SourceKey = Guid.NewGuid().ToString("N"), MaxAmount = price,
            ItemName = item, ItemPrice = price, CreatedUtc = new DateTime(2026, 12, 1), PurchasedUtc = new DateTime(2026, 12, 1), DeliveredUtc = deliveredUtc,
        });
        raw.SaveChanges();
    }

    /// <summary>유년부: 받은 선물 4건(개인 2 · 새친구 · 초대) + 줄 것 · 취소 · 지난해 받은 것, 초등부 받은 것 1건.</summary>
    private void Seed()
    {
        Gift(_kids.Id, GiftKinds.Personal, GiftStates.Delivered, 4500, new DateTime(2027, 1, 3, 3, 0, 0), "모나미 153 볼펜 12자루");
        Gift(_kids.Id, GiftKinds.Personal, GiftStates.Delivered, 6000, new DateTime(2026, 12, 31, 15, 30, 0));   // 서울 1월 1일 00:30 → 올해
        Gift(_kids.Id, GiftKinds.Newcomer, GiftStates.Delivered, 3000, new DateTime(2027, 1, 2));
        Gift(_kids.Id, GiftKinds.Invite, GiftStates.Delivered, 2000, new DateTime(2027, 1, 2));
        Gift(_kids.Id, GiftKinds.Personal, GiftStates.Purchased, 9000, null);   // 줄 것 → 빠짐
        Gift(_kids.Id, GiftKinds.Personal, GiftStates.Cancelled, 7000, null);   // 취소 → 빠짐
        Gift(_kids.Id, GiftKinds.Family, GiftStates.Delivered, 10000, new DateTime(2026, 12, 20));   // 지난달
        Gift(_elem.Id, GiftKinds.Personal, GiftStates.Delivered, 5000, new DateTime(2027, 1, 4));
    }

    [Fact]
    public void R294_기간_올해_이번달_지난달_직접()
    {
        var today = new DateOnly(2027, 1, 5);
        Assert.Equal(new GiftCostPeriod("year", new(2027, 1, 1), today), GiftCostService.Period(null, null, null, today));
        Assert.Equal(new GiftCostPeriod("month", new(2027, 1, 1), today), GiftCostService.Period("month", null, null, today));
        Assert.Equal(new GiftCostPeriod("last", new(2026, 12, 1), new(2026, 12, 31)), GiftCostService.Period("last", null, null, today));
        Assert.Equal(new GiftCostPeriod("custom", new(2026, 11, 1), new(2026, 12, 15)), GiftCostService.Period("custom", "2026-12-15", "2026-11-01", today));   // 거꾸로 써도
        Assert.Equal("year", GiftCostService.Period("custom", "abc", "2026-11-01", today).Key);   // 날짜가 틀리면 올해
    }

    [Fact]
    public async Task R294_부서_총비용은_줬어요를_누른_날_기준_받은_선물만_우리_부서만()
    {
        Seed();
        var admin = await Login("admin@example.com");
        var page = await admin.GetStringAsync("/kids/admin/gifts?t=cost");
        Assert.Contains("15,500<u>원</u>", page);   // 4,500 + 6,000 + 3,000 + 2,000 (줄 것 · 취소 · 지난달 · 초등부는 빠짐)
        Assert.Contains("받은 선물 <b class=\"num\">4건</b>", page);
        Assert.Contains("새친구 · 초대", page);
        Assert.Contains("5,000원", page);   // 새친구 3,000 + 초대 2,000 은 한 줄
        Assert.Contains("모나미 153 볼펜 12자루", page);
        Assert.Contains("1.3 받음", page);
        Assert.Contains(">엑셀로 받기 (.xlsx)<", page);

        var last = await admin.GetStringAsync("/kids/admin/gifts?t=cost&per=last");
        Assert.Contains("10,000<u>원</u>", last);   // 지난달: 가족팀 1건

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/kids/admin/gifts?t=cost&per=custom&from=2026-12-01&to=2027-01-05")).StatusCode);
        Assert.Contains("25,500<u>원</u>", await admin.GetStringAsync("/kids/admin/gifts?t=cost&per=custom&from=2026-12-01&to=2027-01-05"));
    }

    [Fact]
    public async Task R294_부서_엑셀은_합계와_받은_선물_한_줄씩()
    {
        Seed();
        var admin = await Login("admin@example.com");
        var res = await admin.GetAsync("/kids/admin/gifts?handler=Excel&per=year");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", res.Content.Headers.ContentType!.MediaType);
        using var wb = new XLWorkbook(await res.Content.ReadAsStreamAsync());
        Assert.Equal(["합계", "받은 선물"], wb.Worksheets.Select(w => w.Name).ToArray());
        var lines = wb.Worksheet("받은 선물");
        Assert.Equal(["받은 날", "받는 사람", "반", "종류", "상품", "금액"], Enumerable.Range(1, 6).Select(i => lines.Cell(1, i).GetString()).ToArray());
        Assert.Equal("모나미 153 볼펜 12자루", lines.Cell(2, 5).GetString());   // 최근 순
        Assert.Equal("안예빛", lines.Cell(2, 2).GetString());
        Assert.Equal(15500, lines.Cell(6, 6).GetValue<long>());   // 4줄 + 합계
        var sum = wb.Worksheet("합계");
        Assert.Contains(Enumerable.Range(1, 20).Select(r => (sum.Cell(r, 1).GetString(), sum.Cell(r, 3).GetString())), x => x == ("합계", "15500"));
    }

    [Fact]
    public async Task R294_총비용은_부서_관리자만()
    {
        var kid = await Login("kid@example.com");
        Assert.NotEqual(HttpStatusCode.OK, (await kid.GetAsync("/kids/admin/gifts?t=cost")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await kid.GetAsync("/kids/admin/gifts?handler=Excel")).StatusCode);
        var admin = await Login("admin@example.com");
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync("/platform/gift-cost")).StatusCode);   // 모든 부서는 플랫폼 관리자만
    }

    [Fact]
    public async Task R294_플랫폼은_모든_부서와_부서별_부서_종류로_거르고_엑셀에_부서_칸()
    {
        Seed();
        var op = await Login("operator@example.com");
        var page = await op.GetStringAsync("/platform/gift-cost");
        Assert.Contains("20,500<u>원</u>", page);   // 유년부 15,500 + 초등부 5,000
        Assert.Contains("모든 부서 · 받은 선물 금액 합계", page);
        Assert.Contains("부서별", page);
        Assert.DoesNotContain("한 명당", page);   // 질문 4 → 1

        Assert.Contains("5,000<u>원</u>", await op.GetStringAsync($"/platform/gift-cost?d={_elem.Id}"));
        Assert.Contains("15,500<u>원</u>", await op.GetStringAsync("/platform/gift-cost?k=personal"));   // 개인: 4,500 + 6,000 + 초등부 5,000(줄 것 · 취소 빠짐)

        var res = await op.GetAsync("/platform/gift-cost?handler=Excel");
        using var wb = new XLWorkbook(await res.Content.ReadAsStreamAsync());
        var lines = wb.Worksheet("받은 선물");
        Assert.Equal("부서", lines.Cell(1, 1).GetString());
        Assert.Equal(new[] { _kids.Name, _elem.Name }.Order().ToArray(), Enumerable.Range(2, 5).Select(r => lines.Cell(r, 1).GetString()).Distinct().Order().ToArray());
    }
}
