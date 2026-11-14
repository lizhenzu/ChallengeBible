using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>플랫폼 선물 상품 관리(확정 시안 플랫폼-선물상품 B, R-278 · R-279): 쿠팡 링크 필수, 삭제 대상 살리기, 내리기 · 삭제, 사진, 플랫폼 관리자만.</summary>
public partial class GiftCatalogTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly string _images = Path.Combine(Path.GetTempPath(), "cb-img-" + Guid.NewGuid().ToString("N"));

    public GiftCatalogTests(WebApplicationFactory<Program> app)
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
    }

    public void Dispose() { if (Directory.Exists(_images)) Directory.Delete(_images, true); }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(bool platformAdmin = true)
    {
        var u = InMemoryDb.SeedAccount(_db, platformAdmin ? "boss@example.com" : "kim@example.com", "관리자");
        using (var raw = InMemoryDb.Raw(_db))
        {
            var row = raw.Users.Single(x => x.Id == u.Id);
            row.PasswordHash = new PasswordHasher<AppUser>().HashPassword(row, "1234");
            raw.SaveChanges();
        }
        if (platformAdmin) InMemoryDb.MakePlatformAdmin(_db, u);
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

    private int Seed(string name, string status = GiftItemStatus.Active, string? url = "https://www.coupang.com/vp/products/1", string? reason = null, int price = 4500)
    {
        using var raw = InMemoryDb.Raw(_db);
        var i = new GiftItem { Name = name, Price = price, Status = status, CoupangUrl = url, ReviewReason = reason };
        raw.GiftItems.Add(i);
        raw.SaveChanges();
        return i.Id;
    }

    [Theory]
    [InlineData("https://www.coupang.com/vp/products/123", true)]
    [InlineData("https://link.coupang.com/a/abc", true)]
    [InlineData("http://coupang.com/x", true)]
    [InlineData("https://www.11st.co.kr/products/1", false)]
    [InlineData("https://coupang.com.evil.example/x", false)]
    [InlineData("coupang.com/vp/1", false)]
    [InlineData("", false)]
    public void R278_쿠팡_주소만_받는다(string url, bool ok) => Assert.Equal(ok, GiftCatalogService.IsCoupang(url));

    [Fact]
    public async Task R278_등록은_쿠팡_링크가_있어야_하고_저장하면_공통_판매_중()
    {
        var c = await Login();
        var bad = await Post(c, "/platform/gifts/edit", "/platform/gifts/edit", new() { ["Input.Name"] = "볼펜", ["Input.Price"] = "4,500", ["Input.CoupangUrl"] = "https://www.11st.co.kr/1" });
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("쿠팡 상품 주소(coupang.com)를 넣어 주세요.", await bad.Content.ReadAsStringAsync());
        var ok = await Post(c, "/platform/gifts/edit", "/platform/gifts/edit", new() { ["Input.Name"] = "볼펜 12자루", ["Input.Price"] = "4,500", ["Input.CoupangUrl"] = "https://www.coupang.com/vp/products/1" });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        using var raw = InMemoryDb.Raw(_db);
        var i = raw.GiftItems.Single();
        Assert.Equal(("볼펜 12자루", 4500, GiftItemStatus.Active, (int?)null), (i.Name, i.Price, i.Status, i.TenantId));
        Assert.Contains("볼펜 12자루", await c.GetStringAsync("/platform/gifts"));
        Assert.Contains(raw.AuditLogs, l => l.Action == GiftCatalogService.LogAction);
    }

    [Fact]
    public async Task R279_삭제_대상은_쿠팡_링크를_넣어_저장하면_판매_중으로_살아난다()
    {
        var id = Seed("그림 성경", GiftItemStatus.Review, url: null, reason: "no link");
        var c = await Login();
        var list = await c.GetStringAsync("/platform/gifts?s=Review");
        Assert.Contains("그림 성경", list);
        Assert.Contains("링크 없음", list);
        Assert.DoesNotContain("그림 성경", await c.GetStringAsync("/platform/gifts"));
        await Post(c, $"/platform/gifts/edit/{id}", $"/platform/gifts/edit/{id}", new() { ["Input.Name"] = "그림 성경", ["Input.Price"] = "12000", ["Input.CoupangUrl"] = "https://www.coupang.com/vp/products/9" });
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal((GiftItemStatus.Active, (string?)null), raw.GiftItems.Where(x => x.Id == id).Select(x => ValueTuple.Create(x.Status, x.ReviewReason)).Single());
    }

    [Fact]
    public async Task R279_고른_기록이_있으면_삭제는_안_되고_목록에서_내리기만_된다()
    {
        var t = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var id = Seed("곰 인형");
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.Gifts.Add(new Gift { TenantId = t.Id, Kind = GiftKinds.Birthday, Year = 2026, UserName = "a", SourceKey = "B2026", MaxAmount = 5000, GiftItemId = id, State = GiftStates.Picked });
            raw.SaveChanges();
        }
        var c = await Login();
        var page = await c.GetStringAsync($"/platform/gifts/edit/{id}");
        Assert.DoesNotContain("삭제(상품 + 사진)", page);   // 버튼도 없다
        var del = await Post(c, $"/platform/gifts/edit/{id}", $"/platform/gifts/edit/{id}?handler=Delete", []);
        Assert.Contains("고른 기록이 있는 상품은 지울 수 없어요", await del.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, $"/platform/gifts/edit/{id}", $"/platform/gifts/edit/{id}?handler=Retire", [])).StatusCode);
        using var raw2 = InMemoryDb.Raw(_db);
        Assert.Equal(GiftItemStatus.Retired, raw2.GiftItems.Single(x => x.Id == id).Status);
        Assert.DoesNotContain("곰 인형", await c.GetStringAsync("/platform/gifts"));
        Assert.Contains("곰 인형", await c.GetStringAsync("/platform/gifts?s=Retired"));
    }

    /// <summary>칸 + 사진을 한 폼으로 저장(2026-11-14 사용자: 상품 등록에서 사진도 함께).</summary>
    private async Task<HttpResponseMessage> SaveWithPhoto(HttpClient c, string url, byte[]? photo, string name = "연필", string link = "https://www.coupang.com/vp/products/1")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(TokenRx().Match(await c.GetStringAsync(url)).Groups[1].Value), "__RequestVerificationToken" },
            { new StringContent(name), "Input.Name" }, { new StringContent("3,000"), "Input.Price" }, { new StringContent(link), "Input.CoupangUrl" },
        };
        if (photo is not null) form.Add(new ByteArrayContent(photo), "photo", "photo.jpg");
        return await c.PostAsync(url, form);
    }

    [Fact]
    public async Task R278_상품을_등록하면서_사진도_함께_저장한다()
    {
        var c = await Login();
        var r = await SaveWithPhoto(c, "/platform/gifts/edit", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3], "색연필");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.EndsWith("?m=saved", r.Headers.Location!.OriginalString);
        using var raw = InMemoryDb.Raw(_db);
        var i = raw.GiftItems.Single();
        Assert.Equal(("색연필", true), (i.Name, i.HasImage));
        Assert.True(File.Exists(Path.Combine(_images, "AwardItem", $"{i.Id}.jpg")));
    }

    [Fact]
    public async Task R278_칸이_틀리면_사진도_올리지_않고_사진만_틀리면_상품은_저장하고_알린다()
    {
        var c = await Login();
        var bad = await SaveWithPhoto(c, "/platform/gifts/edit", [0xFF, 0xD8, 0xFF, 0xE0], link: "https://www.11st.co.kr/1");
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.GiftItems);
        Assert.False(Directory.Exists(Path.Combine(_images, "AwardItem")) && Directory.EnumerateFiles(Path.Combine(_images, "AwardItem")).Any());

        var png = await SaveWithPhoto(c, "/platform/gifts/edit", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
        Assert.EndsWith("?m=nophoto", png.Headers.Location!.OriginalString);   // PNG 는 안 받는다
        using var raw2 = InMemoryDb.Raw(_db);
        Assert.False(raw2.GiftItems.Single().HasImage);
    }

    [Fact]
    public async Task R279_삭제하면_상품과_서버_사진이_지워진다()
    {
        var id = Seed("연필");
        var c = await Login();
        Assert.Equal(HttpStatusCode.Redirect, (await SaveWithPhoto(c, $"/platform/gifts/edit/{id}", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3])).StatusCode);
        var path = Path.Combine(_images, "AwardItem", $"{id}.jpg");
        Assert.True(File.Exists(path));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, $"/platform/gifts/edit/{id}", $"/platform/gifts/edit/{id}?handler=Delete", [])).StatusCode);
        Assert.False(File.Exists(path));
        using var raw3 = InMemoryDb.Raw(_db);
        Assert.Empty(raw3.GiftItems);
    }

    [Fact]
    public async Task 상품_화면에는_점수_칸이_없다()
    {
        var c = await Login();
        Assert.DoesNotContain("필요한 점수", await c.GetStringAsync("/platform/gifts/edit"));   // 2026-11-14 사용자: 포인트는 상품과 별개
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_상품_관리에_들어가지_못한다()
    {
        var c = await Login(platformAdmin: false);
        var r = await c.GetAsync("/platform/gifts");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }
}
