using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// R-339 부서 사진첩(ADR 0118): 부서 관리자가 앨범을 만들고, 교사 · 관리자가 사진만(JPG · PNG · WEBP · GIF) 한 장씩 올린다(썸네일).
/// 보기는 공개 범위(누구나 · 로그인 · 부서 · 관리자), 내려받기는 앨범에서 끌 수 있다. 교사는 본인이 올린 것만 지운다. 부서마다 완전히 따로.
/// </summary>
public partial class AlbumTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-album-" + Guid.NewGuid().ToString("N"));
    private readonly Tenant _kids, _infant;

    public AlbumTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("Albums:Dir", _dir);
            b.ConfigureServices(s =>
            {
                s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
                s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
            });
        });
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _infant = InMemoryDb.SeedTenant(_db, "jamsil-infant", "JSINFA");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private static byte[] Png(int seed)
    {
        using var img = new Image<Rgba32>(40, 30, new Rgba32((byte)seed, 120, 200));
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private async Task<HttpClient> Member(string email, int tenantId, params string[] roles)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김" + email, ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        string id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Users.Single(a => a.Email == email + "@example.com").UserName!;
        InMemoryDb.SeedMembership(_db, tenantId, id, MembershipStatus.Approved, roles);
        return c;
    }

    private static async Task<int> MakeAlbum(HttpClient admin, string visibility = AlbumVisibility.Login, bool download = true)
    {
        var tok = TokenRx().Match(await admin.GetStringAsync("/jamsil-kids/album/edit")).Groups[1].Value;
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = tok, ["title"] = "여름성경학교", ["eventDate"] = "2026-07-31", ["visibility"] = visibility };
        if (download) form["allowDownload"] = "true";
        var r = await admin.PostAsync("/jamsil-kids/album/edit", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return int.Parse(r.Headers.Location!.OriginalString.Split('/').Last());
    }

    private static async Task<string> Upload(HttpClient c, int album, string name, byte[] bytes)
    {
        var tok = TokenRx().Match(await c.GetStringAsync($"/jamsil-kids/album/{album}")).Groups[1].Value;
        var f = new ByteArrayContent(bytes);
        f.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { new StringContent(tok), "__RequestVerificationToken" }, { f, "file", name } };
        return Regex.Unescape(await (await c.PostAsync($"/jamsil-kids/album/{album}?handler=Upload", form)).Content.ReadAsStringAsync());   // JSON 은 한글을 \uXXXX 로
    }

    private int PhotoId(string name) { using var raw = InMemoryDb.Raw(_db); return raw.Photos.Single(p => p.FileName == name).Id; }

    [Fact]
    public async Task R339_관리자가_앨범을_만들고_교사가_사진을_올리면_로그인한_사람은_보고_손님은_못_본다()
    {
        var admin = await Member("admin", _kids.Id, Roles.Teacher, Roles.TenantAdmin);
        var teacher = await Member("sunmi", _kids.Id, Roles.Teacher);
        Assert.NotEqual(HttpStatusCode.OK, (await teacher.GetAsync("/jamsil-kids/album/edit")).StatusCode);   // 앨범 만들기는 관리자만
        var album = await MakeAlbum(admin);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(AlbumVisibility.Login, raw.Albums.Single().Visibility);   // 기본: 로그인한 사람 모두

        Assert.Contains("\"ok\":true", await Upload(teacher, album, "a.png", Png(1)));
        Assert.Contains("\"ok\":true", await Upload(teacher, album, "b.png", Png(2)));
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal(2, raw.Photos.Count());
            Assert.All(raw.Photos, p => Assert.True(File.Exists(Path.Combine(_dir, _kids.Id.ToString(), p.ThumbName))));   // 부서 폴더 + 썸네일
        }

        var other = await Member("infant", _infant.Id, Roles.Participant);   // 다른 부서라도 로그인했으면(공개 범위 "로그인")
        Assert.Contains("여름성경학교", await other.GetStringAsync("/jamsil-kids/album"));
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync($"/jamsil-kids/album/photo/{PhotoId("a.png")}?t=1")).StatusCode);
        var guest = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/jamsil-kids/album/{album}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/jamsil-kids/album/photo/{PhotoId("a.png")}")).StatusCode);
        Assert.Contains("사진첩", await teacher.GetStringAsync("/jamsil-kids"));   // 부서 홈 카드
    }

    [Fact]
    public async Task R339_사진이_아니거나_같은_사진이거나_학생이면_못_올린다()
    {
        var admin = await Member("admin", _kids.Id, Roles.Teacher, Roles.TenantAdmin);
        var album = await MakeAlbum(admin);
        Assert.Contains("사진 파일이 아니에요", await Upload(admin, album, "movie.mp4", "....ftypisom...."u8.ToArray()));
        Assert.Contains("\"ok\":true", await Upload(admin, album, "a.png", Png(1)));
        Assert.Contains("이미 이 앨범에 있는 사진", await Upload(admin, album, "copy.png", Png(1)));
        var kid = await Member("cheolsu", _kids.Id, Roles.Participant);
        Assert.Contains("교사 · 관리자만", await Upload(kid, album, "c.png", Png(3)));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Single(raw.Photos);
    }

    [Fact]
    public async Task R339_다른_부서_주소로는_앨범도_사진도_안_열린다()
    {
        var admin = await Member("admin", _kids.Id, Roles.Teacher, Roles.TenantAdmin);
        var album = await MakeAlbum(admin, AlbumVisibility.Public);
        await Upload(admin, album, "a.png", Png(1));
        var guest = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/jamsil-kids/album/{album}")).StatusCode);   // 누구나
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/jamsil-infant/album/{album}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/jamsil-infant/album/photo/{PhotoId("a.png")}?t=1")).StatusCode);
        Assert.DoesNotContain("여름성경학교", await guest.GetStringAsync("/jamsil-infant/album"));
    }

    [Fact]
    public async Task R339_부서_사람만_앨범은_다른_부서가_못_보고_내려받기를_끄면_관리자만_받는다()
    {
        var admin = await Member("admin", _kids.Id, Roles.Teacher, Roles.TenantAdmin);
        var album = await MakeAlbum(admin, AlbumVisibility.Dept, download: false);
        await Upload(admin, album, "a.png", Png(1));
        await Upload(admin, album, "b.png", Png(2));
        var other = await Member("infant", _infant.Id, Roles.Participant);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/jamsil-kids/album/{album}")).StatusCode);
        var kid = await Member("cheolsu", _kids.Id, Roles.Participant);
        Assert.Equal(HttpStatusCode.OK, (await kid.GetAsync($"/jamsil-kids/album/{album}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await kid.GetAsync($"/jamsil-kids/album/photo/{PhotoId("a.png")}?dl=1")).StatusCode);   // 내려받기 꺼짐
        Assert.Equal(HttpStatusCode.NotFound, (await kid.GetAsync($"/jamsil-kids/album/{album}?handler=Zip")).StatusCode);

        var zip = await admin.GetAsync($"/jamsil-kids/album/{album}?handler=Zip");   // 관리자는 받는다
        Assert.Equal("application/zip", zip.Content.Headers.ContentType!.MediaType);
        using var z = new ZipArchive(new MemoryStream(await zip.Content.ReadAsByteArrayAsync()));
        Assert.Equal(["a.png", "b.png"], z.Entries.Select(e => e.Name).Order());
    }

    [Fact]
    public async Task R339_교사는_본인이_올린_사진만_지우고_앨범을_지우면_사진과_파일도_지워진다()
    {
        var admin = await Member("admin", _kids.Id, Roles.Teacher, Roles.TenantAdmin);
        var t1 = await Member("sunmi", _kids.Id, Roles.Teacher);
        var t2 = await Member("minho", _kids.Id, Roles.Teacher);
        var album = await MakeAlbum(admin);
        await Upload(t1, album, "a.png", Png(1));
        await Upload(t2, album, "b.png", Png(2));
        async Task Delete(HttpClient c, params int[] ids)
        {
            var tok = TokenRx().Match(await c.GetStringAsync($"/jamsil-kids/album/{album}")).Groups[1].Value;
            var form = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", tok) };
            form.AddRange(ids.Select(i => new KeyValuePair<string, string>("ids", i.ToString())));
            await c.PostAsync($"/jamsil-kids/album/{album}?handler=Delete", new FormUrlEncodedContent(form));
        }
        await Delete(t1, PhotoId("a.png"), PhotoId("b.png"));   // 본인 것(a)만 지워진다
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(["b.png"], raw.Photos.Select(p => p.FileName));

        var tok = TokenRx().Match(await admin.GetStringAsync($"/jamsil-kids/album/edit/{album}")).Groups[1].Value;
        await admin.PostAsync("/jamsil-kids/album/edit?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok, ["id"] = album.ToString() }));
        using var after = InMemoryDb.Raw(_db);
        Assert.Empty(after.Albums);
        Assert.Empty(after.Photos);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, _kids.Id.ToString())));
    }
}
