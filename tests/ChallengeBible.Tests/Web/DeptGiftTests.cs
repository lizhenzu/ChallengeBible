using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>부서 선물 설정(확정 시안 관리-선물, ADR 0094): R-276 공통 숨기기 · R-277 부서 상품과 공통에 올리기 · 금액 · 점수 저장(R-280 · R-283 · R-286 · R-288 · R-289).</summary>
public partial class DeptGiftTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly string _images = Path.Combine(Path.GetTempPath(), "cb-img-" + Guid.NewGuid().ToString("N"));
    private readonly Tenant _kids, _elem;

    public DeptGiftTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("Images:Dir", _images);
            b.ConfigureServices(s =>
            {
                s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
                s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            });
        });
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        _elem = InMemoryDb.SeedTenant(_db, "elem", "ELEM01");
    }

    public void Dispose() { if (Directory.Exists(_images)) Directory.Delete(_images, true); }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Admin(string who, Tenant t, bool admin = true)
    {
        var u = InMemoryDb.SeedAccount(_db, who + "@example.com", who);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var row = raw.Users.Single(x => x.Id == u.Id);
            row.PasswordHash = new PasswordHasher<AppUser>().HashPassword(row, "1234");
            raw.SaveChanges();
        }
        InMemoryDb.SeedMembership(_db, t.Id, u.UserName!, MembershipStatus.Approved, admin ? [Roles.Teacher, Roles.TenantAdmin] : [Roles.Teacher]);
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var tok = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value;
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok, ["Email"] = u.UserName!, ["Password"] = "1234" }));
        return c;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string page, string url, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync(page)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private int Item(string name, int? tenantId = null, int? sharedBy = null)
    {
        using var raw = InMemoryDb.Raw(_db);
        var i = new GiftItem { Name = name, Price = 4500, CoupangUrl = "https://www.coupang.com/vp/products/1", TenantId = tenantId, SharedByTenantId = sharedBy };
        raw.GiftItems.Add(i);
        raw.SaveChanges();
        return i.Id;
    }

    [Fact]
    public async Task R276_공통_상품을_숨기면_우리_부서만_숨김이고_다른_부서는_그대로()
    {
        var pen = Item("볼펜");
        Item("노트");
        var c = await Admin("kidsadmin", _kids);
        Assert.Contains("href=\"/kids/admin/gifts\"", await c.GetStringAsync("/kids/admin"));   // 관리 메뉴 "선물"
        var tabs = await c.GetStringAsync("/kids/admin/gifts");
        Assert.Contains(">공통 상품</a>", tabs);
        Assert.Contains(">부서 상품</a>", tabs);
        Assert.Contains(">선물 처리</a>", tabs);   // 처음 탭은 선물 처리(확정 시안 관리-선물처리)
        var page = await c.GetStringAsync("/kids/admin/gifts?t=list");
        Assert.Contains("볼펜", page);
        Assert.Contains("aria-checked=\"true\"", page);

        var r = await Post(c, "/kids/admin/gifts?t=list", "/kids/admin/gifts?handler=Hide", new() { ["id"] = pen.ToString(), ["hide"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal((_kids.Id, pen), raw.GiftItemsHidden.Select(h => ValueTuple.Create(h.TenantId, h.GiftItemId)).Single());
        var hidden = await c.GetStringAsync("/kids/admin/gifts?t=list&v=hidden");
        Assert.Contains("볼펜", hidden);
        Assert.DoesNotContain("노트", hidden);
        Assert.DoesNotContain("볼펜", await c.GetStringAsync("/kids/admin/gifts?t=list&v=shown"));

        var e = await Admin("elemadmin", _elem);
        Assert.Contains("볼펜", await e.GetStringAsync("/elem/admin/gifts?t=list&v=shown"));   // 다른 부서는 그대로 보인다

        await Post(c, "/kids/admin/gifts?t=list", "/kids/admin/gifts?handler=Hide", new() { ["id"] = pen.ToString(), ["hide"] = "false" });
        using var raw2 = InMemoryDb.Raw(_db);
        Assert.Empty(raw2.GiftItemsHidden);
    }

    [Fact]
    public async Task R277_부서_상품은_우리_부서만_보이고_공통에_올리면_모든_부서에_보이며_올린_부서만_고친다()
    {
        var c = await Admin("kidsadmin", _kids);
        var e = await Admin("elemadmin", _elem);
        var add = await Post(c, "/kids/admin/gifts/edit", "/kids/admin/gifts/edit", new() { ["Input.Name"] = "찬양 DVD", ["Input.Price"] = "15,000", ["Input.CoupangUrl"] = "https://www.coupang.com/vp/products/5" });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        int id;
        using (var raw = InMemoryDb.Raw(_db)) { var i = raw.GiftItems.Single(); Assert.Equal((int?)_kids.Id, i.TenantId); id = i.Id; }
        Assert.Contains("찬양 DVD", await c.GetStringAsync("/kids/admin/gifts?t=ours"));
        Assert.DoesNotContain("찬양 DVD", await c.GetStringAsync("/kids/admin/gifts?t=list"));   // "공통 상품" 탭에는 공통만(2026-11-14 사용자: 탭 이름 공통 상품 · 부서 상품)
        Assert.DoesNotContain("찬양 DVD", await e.GetStringAsync("/elem/admin/gifts?t=list"));   // 다른 부서에는 안 보인다
        Assert.Equal(HttpStatusCode.NotFound, (await e.GetAsync($"/elem/admin/gifts/edit/{id}")).StatusCode);

        var share = await Post(c, "/kids/admin/gifts?t=ours", "/kids/admin/gifts?handler=Share", new() { ["id"] = id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, share.StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(((int?)null, (int?)_kids.Id), raw.GiftItems.Where(x => x.Id == id).Select(x => ValueTuple.Create(x.TenantId, x.SharedByTenantId)).Single());
        Assert.Contains("찬양 DVD", await e.GetStringAsync("/elem/admin/gifts?t=list"));        // 이제 모든 부서
        Assert.Equal(HttpStatusCode.NotFound, (await e.GetAsync($"/elem/admin/gifts/edit/{id}")).StatusCode);   // 고치는 건 올린 부서 · 플랫폼만

        // 올린 부서는 고칠 수 있지만, 내리기 · 지우기는 플랫폼만
        var edit = await c.GetStringAsync($"/kids/admin/gifts/edit/{id}");
        Assert.Contains("고치면 모든 부서에 반영돼요", edit);
        Assert.DoesNotContain("목록에서 내리기</button>", edit);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, $"/kids/admin/gifts/edit/{id}", $"/kids/admin/gifts/edit/{id}", new() { ["Input.Name"] = "찬양 DVD 2", ["Input.Price"] = "16,000", ["Input.CoupangUrl"] = "https://www.coupang.com/vp/products/5" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, $"/kids/admin/gifts/edit/{id}", $"/kids/admin/gifts/edit/{id}?handler=Retire", [])).StatusCode);
        using var raw2 = InMemoryDb.Raw(_db);
        Assert.Equal(("찬양 DVD 2", GiftItemStatus.Active), raw2.GiftItems.Where(x => x.Id == id).Select(x => ValueTuple.Create(x.Name, x.Status)).Single());
    }

    [Fact]
    public async Task R277_다른_부서의_상품은_공통에_올리거나_숨길_수_없다()
    {
        var theirs = Item("초등부 팔찌", _elem.Id);
        var c = await Admin("kidsadmin", _kids);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, "/kids/admin/gifts?t=money", "/kids/admin/gifts?handler=Share", new() { ["id"] = theirs.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, "/kids/admin/gifts?t=money", "/kids/admin/gifts?handler=Hide", new() { ["id"] = theirs.ToString(), ["hide"] = "true" })).StatusCode);
    }

    [Fact]
    public async Task 금액_점수는_빈_칸이면_공통_기본값이고_틀린_칸이_있으면_아무것도_저장하지_않는다()
    {
        var c = await Admin("kidsadmin", _kids);
        var page = await c.GetStringAsync("/kids/admin/gifts?t=money");
        Assert.Contains("placeholder=\"30,000\"", page);   // 상한 기본값(R-280)
        Assert.Contains("placeholder=\"15,000\"", page);   // 새친구 최대(R-286)
        Assert.Contains("placeholder=\"20,000\"", page);   // 3독부터(R-289)

        var bad = await Post(c, "/kids/admin/gifts?t=money", "/kids/admin/gifts?t=money&handler=Money", new()
        {
            ["Money.PersonalCap"] = "20,000", ["Money.BirthdayMin"] = "9,000",             // 최대(기본 5,000)보다 큼
            ["Money.RateCredits"] = "1500", ["Money.RateAmounts"] = "2000",                // 1,000 단위 아님
        });
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        var html = await bad.Content.ReadAsStringAsync();
        Assert.Contains("최대 금액이 최소 금액보다 작아요", html);
        Assert.Contains("크레딧은 1,000 단위로 써 주세요", html);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.GiftSettings);

        var form = new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", TokenRx().Match(page).Groups[1].Value),
            new("Money.PersonalCap", "20,000"), new("Money.InviteMax", "10,000원"),
            new("Money.RateCredits", "10,000"), new("Money.RateAmounts", "12,000"), new("Money.RateCredits", ""), new("Money.RateAmounts", ""),
            new("Money.Reading", ""), new("Money.Reading", "18,000"), new("Money.Reading", ""),
        ]);
        var ok = await c.PostAsync("/kids/admin/gifts?t=money&handler=Money", form);
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        using var raw2 = InMemoryDb.Raw(_db);
        var s = raw2.GiftSettings.Single();
        Assert.Equal((_kids.Id, (int?)20000, (int?)10000, (int?)null), (s.TenantId, s.PersonalCap, s.InviteMax, s.NewcomerMax));
        Assert.Equal((10000, 12000), raw2.GiftCreditRates.Select(r => ValueTuple.Create(r.Credits, r.Amount)).Single());
        Assert.Equal((2, 18000), raw2.GiftReadingAmounts.Select(r => ValueTuple.Create(r.RoundNo, r.Amount)).Single());
        Assert.Contains("value=\"20,000\"", await c.GetStringAsync("/kids/admin/gifts?t=money"));
    }

    [Fact]
    public async Task 부서_관리자가_전체_공통으로_등록하면_2000번대_공통_상품이_되어_모든_부서에_보인다()
    {
        Item("유년부 연필");   // 옮겨 온 유년부 상품(번호는 2000 미만)
        var c = await Admin("kidsadmin", _kids);
        var e = await Admin("elemadmin", _elem);
        Assert.Contains("전체 공통으로 등록", await c.GetStringAsync("/kids/admin/gifts/edit"));
        var form = new Dictionary<string, string> { ["Input.Name"] = "성경 팔찌", ["Input.Price"] = "3,500", ["Input.CoupangUrl"] = "https://www.coupang.com/vp/products/7", ["common"] = "true" };
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/kids/admin/gifts/edit", "/kids/admin/gifts/edit", form)).StatusCode);
        form["Input.Name"] = "성경 책갈피";
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/kids/admin/gifts/edit", "/kids/admin/gifts/edit", form)).StatusCode);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var made = raw.GiftItems.Where(x => x.Name.StartsWith("성경")).OrderBy(x => x.Id).ToList();
            Assert.Equal([2000, 2001], made.Select(x => x.Id));
            Assert.All(made, x => Assert.Equal(((int?)null, (int?)_kids.Id), (x.TenantId, x.SharedByTenantId)));
        }
        Assert.Contains("성경 팔찌", await e.GetStringAsync("/elem/admin/gifts?t=list"));   // 다른 부서에도 공통 상품으로
        Assert.Contains("성경 팔찌", await c.GetStringAsync("/kids/admin/gifts?t=ours"));   // 올린 부서는 부서 상품 탭에서 고친다

        // 체크하지 않으면 부서 상품
        form.Remove("common"); form["Input.Name"] = "우리만 스티커";
        await Post(c, "/kids/admin/gifts/edit", "/kids/admin/gifts/edit", form);
        using var raw2 = InMemoryDb.Raw(_db);
        Assert.Equal((int?)_kids.Id, raw2.GiftItems.Single(x => x.Name == "우리만 스티커").TenantId);
    }

    [Fact]
    public async Task 부서_관리자가_아니면_선물_설정에_들어가지_못한다()
    {
        var c = await Admin("teacher", _kids, admin: false);
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/kids/admin/gifts")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/kids/admin/gifts/edit")).StatusCode);
    }
}
