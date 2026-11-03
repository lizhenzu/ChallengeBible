using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>얼굴 사진(ADR 0078, R-200 ~ R-203): 본인 · 부서 관리자가 올리고 지우고, 같은 부서에 승인된 사람만 본다.</summary>
public partial class PhotoTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-photos-" + Guid.NewGuid().ToString("N"));
    private readonly Tenant _kids, _elem;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0xFF, 0xD9];

    public PhotoTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("Photos:Dir", _dir);
            b.ConfigureServices(s =>
            {
                s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
                s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            });
        });
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
    }

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<(HttpClient C, int Id)> SignUp(string login, string name)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value,
            ["DisplayName"] = name, ["Email"] = login + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        return (c, raw.Accounts.Single(a => a.Email == login + "@example.com").Id);
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient c, string page, string url, byte[] bytes)
    {
        var token = TokenRx().Match(await c.GetStringAsync(page)).Groups[1].Value;
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "photo", "photo.jpg");
        return await c.PostAsync(url, form);
    }

    private string? PhotoOf(int id) { using var raw = InMemoryDb.Raw(_db); return raw.Accounts.Single(a => a.Id == id).PhotoPath; }

    [Fact]
    public async Task R200_R201_본인이_올리면_홈_동그라미에_보이고_같은_부서_사람만_본다()
    {
        var (kid, kidId) = await SignUp("cheolsu", "김철수");
        var (friend, friendId) = await SignUp("younghee", "김영희");
        var (stranger, strangerId) = await SignUp("minsu", "박민수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kidId, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _kids.Id, friendId, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _elem.Id, strangerId, MembershipStatus.Approved, Roles.Participant);

        Assert.Contains("href=\"/account/photo?back=/jamsil-kids\"", await kid.GetStringAsync("/jamsil-kids"));   // 사진 없음 → 첫 글자 + 📷
        var res = await Upload(kid, "/account/photo", "/account/photo", Jpeg);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var file = PhotoOf(kidId);
        Assert.NotNull(file);
        Assert.True(File.Exists(Path.Combine(_dir, file!)));
        Assert.Contains($"<img src=\"/account/photo?handler=img&amp;v={file}\"", await kid.GetStringAsync("/jamsil-kids"));

        var own = await kid.GetAsync("/account/photo?handler=img");
        Assert.Equal("image/jpeg", own.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await friend.GetAsync($"/jamsil-kids/photo/{kidId}")).StatusCode);          // 같은 부서
        Assert.NotEqual(HttpStatusCode.OK, (await stranger.GetAsync($"/jamsil-kids/photo/{kidId}")).StatusCode);     // 다른 부서 사람
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/jamsil-elem/photo/{kidId}")).StatusCode); // 그 부서 소속이 아닌 사람의 사진
        Assert.NotEqual(HttpStatusCode.OK, (await _app.CreateClient(new() { AllowAutoRedirect = false }).GetAsync($"/jamsil-kids/photo/{kidId}")).StatusCode);

        // 바꾸면 옛 파일은 지우고, 지우면 첫 글자로
        Assert.Equal(HttpStatusCode.Redirect, (await Upload(kid, "/account/photo", "/account/photo", Jpeg)).StatusCode);
        Assert.False(File.Exists(Path.Combine(_dir, file)));
        var token = TokenRx().Match(await kid.GetStringAsync("/account/photo")).Groups[1].Value;
        await kid.PostAsync("/account/photo?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Null(PhotoOf(kidId));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task R202_사진이_아니거나_400KB_넘으면_받지_않는다()
    {
        var (kid, kidId) = await SignUp("cheolsu", "김철수");
        Assert.Contains("JPEG · PNG · WebP", await (await Upload(kid, "/account/photo", "/account/photo", "hello world"u8.ToArray())).Content.ReadAsStringAsync());
        var big = new byte[PhotoService.MaxBytes + 1];
        Jpeg.CopyTo(big, 0);
        Assert.Contains("너무 커요", await (await Upload(kid, "/account/photo", "/account/photo", big)).Content.ReadAsStringAsync());
        Assert.Null(PhotoOf(kidId));
        Assert.Equal("png", PhotoService.Kind([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal("webp", PhotoService.Kind("RIFF\0\0\0\0WEBP"u8));
    }

    [Fact]
    public async Task R203_관리자가_올리고_지우면_바꾼_기록에_남고_다른_부서_사람은_못_건드린다()
    {
        var (admin, adminId) = await SignUp("eunhye", "이은혜");
        var (_, kidId) = await SignUp("cheolsu", "김철수");
        var (_, otherId) = await SignUp("minsu", "박민수");
        InMemoryDb.SeedMembership(_db, _kids.Id, adminId, MembershipStatus.Approved, Roles.TenantAdmin);
        var km = InMemoryDb.SeedMembership(_db, _kids.Id, kidId, MembershipStatus.Approved, Roles.Participant);
        var om = InMemoryDb.SeedMembership(_db, _elem.Id, otherId, MembershipStatus.Approved, Roles.Participant);

        var page = $"/jamsil-kids/admin/members/{km.Id}";
        Assert.Contains("사진 올리기", await admin.GetStringAsync(page));
        Assert.Equal(HttpStatusCode.Redirect, (await Upload(admin, page, page + "?handler=Photo", Jpeg)).StatusCode);
        Assert.NotNull(PhotoOf(kidId));
        Assert.Contains($"<img src=\"/jamsil-kids/photo/{kidId}?v=", await admin.GetStringAsync(page));

        var token = TokenRx().Match(await admin.GetStringAsync(page)).Groups[1].Value;
        await admin.PostAsync(page + "?handler=PhotoDelete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Null(PhotoOf(kidId));
        using (var raw = InMemoryDb.Raw(_db))
            Assert.Equal(["사진 바꿈: 김철수", "사진 지움: 김철수"], raw.AuditLogs.Where(l => l.Action == PhotoService.LogAction).OrderBy(l => l.Id).Select(l => l.Detail).ToList());

        Assert.Equal(HttpStatusCode.NotFound, (await Upload(admin, page, $"/jamsil-kids/admin/members/{om.Id}?handler=Photo", Jpeg)).StatusCode);   // 다른 부서 소속
        Assert.Null(PhotoOf(otherId));
    }
}
