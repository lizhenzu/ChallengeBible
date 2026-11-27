using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// R-338 부서 주보(ADR 0117): 교사 · 관리자가 날짜 · 제목 · 내용 + 사진 · PDF 여러 개를 올리고, 누구나 본다.
/// 부서마다 완전히 따로 — 다른 부서 주소로는 주보도 파일도 안 열린다. 사진 · PDF 가 아닌 파일, 10개 넘게는 막는다.
/// </summary>
public partial class BulletinTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-bulletin-" + Guid.NewGuid().ToString("N"));
    private readonly Tenant _kids, _infant;

    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9];
    private static readonly byte[] Pdf = "%PDF-1.4 test"u8.ToArray();

    public BulletinTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("Bulletins:Dir", _dir);
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
        using var raw = InMemoryDb.Raw(_db);
        raw.WorshipSchedules.Add(new WorshipSchedule { TenantId = _kids.Id, SortOrder = 1, DayOfWeek = 0, StartTime = new TimeOnly(10, 0), Place = "비전센터 믿음홀(3F)" });
        raw.SaveChanges();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Member(string email, int tenantId, params string[] roles)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김선생", ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        string id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Users.Single(a => a.Email == email + "@example.com").UserName!;
        InMemoryDb.SeedMembership(_db, tenantId, id, MembershipStatus.Approved, roles);
        return c;
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient c, string slug, params (string Name, byte[] Bytes)[] files)
    {
        var token = TokenRx().Match(await c.GetStringAsync($"/{slug}/bulletin/edit")).Groups[1].Value;
        var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("2027-01-10"), "date" },
            { new StringContent("추수감사주일"), "title" },
            { new StringContent("예배 후 감사 카드를 써요."), "body" },
        };
        foreach (var (name, bytes) in files)
        {
            var f = new ByteArrayContent(bytes);
            f.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(f, "files", name);
        }
        return await c.PostAsync($"/{slug}/bulletin/edit", form);
    }

    [Fact]
    public async Task R338_교사가_여러_파일을_올리면_누구나_본다()
    {
        var teacher = await Member("sunmi", _kids.Id, Roles.Teacher);
        var guest = _app.CreateClient();

        var r = await Upload(teacher, "jamsil-kids", ("앞면.jpg", Jpg), ("뒷면.png", Png), ("교회주보.pdf", Pdf));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var b = raw.Bulletins.Single();
            Assert.Equal((_kids.Id, new DateOnly(2027, 1, 10), "추수감사주일"), (b.TenantId, b.Date, b.Title));
            Assert.Equal(3, raw.BulletinFiles.Count(f => f.BulletinId == b.Id));
            Assert.All(raw.BulletinFiles, f => Assert.True(File.Exists(Path.Combine(_dir, _kids.Id.ToString(), f.StoredName))));   // 부서 폴더
        }

        Assert.DoesNotContain("주보 보기", await guest.GetStringAsync("/jamsil-kids"));   // 홈 카드는 2026-11-22 확정으로 뺐다(ADR 0127)
        var page = await guest.GetStringAsync("/jamsil-kids/bulletin");   // 로그인 안 해도 본다
        Assert.Contains("2027년 1월 10일", page);
        Assert.DoesNotContain("추수감사주일", page);   // 제목 · 내용은 보는 화면에서 숨긴다(2026-11-26 사용자, 올리는 화면 값은 그대로)
        Assert.Contains("교회주보.pdf", page);
        Assert.DoesNotContain("class=\"pb-act\"", page);
        Assert.Contains("주보 서재", page);                                   // 책장 서재(2026-11-27 새 퍼블리시본)
        Assert.Contains("class=\"bk-tap bk-login\"", page);   // 손님 앱바 오른쪽 = 사람 아이콘 + 「로그인」(새 퍼블리시본 앱바, 2026-11-27)
        Assert.Contains("href=\"/jamsil-kids/welcome\"", page);   // 왼쪽 = 집 + 「메인화면」
        int fileId;
        using (var raw = InMemoryDb.Raw(_db)) fileId = raw.BulletinFiles.Single(f => f.ContentType == "application/pdf").Id;
        var file = await guest.GetAsync($"/jamsil-kids/bulletin/file/{fileId}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task R338_주보_보기_머리줄은_홈_주보게시판_손님은_아이콘_교사는_주보작성()
    {
        var teacher = await Member("sunmi2", _kids.Id, Roles.Teacher);
        await Upload(teacher, "jamsil-kids", ("앞면.jpg", Jpg));
        var page = await teacher.GetStringAsync("/jamsil-kids/bulletin");
        Assert.Contains("href=\"/jamsil-kids/bulletin/edit\"", page);          // 「+ 새 주보」(교사 · 관리자만)
        Assert.Contains("aria-label=\"새 주보 올리기\"", page);                  // 주황 ＋(교사 · 관리자만)
        Assert.DoesNotContain("class=\"bk-tap bk-login\"", page);
        Assert.Contains("주보 서재", page);
        Assert.Contains("메인화면", page);                                       // 왼쪽 위 = 집 + 메인화면
        Assert.Contains("data-bk-menu", page);                                  // 읽기 화면 ⋯ 메뉴(고치기 · 지우기)
        Assert.Contains("data-bk-delform", page);
    }

    [Fact]
    public async Task R338_주보는_날짜_칩_없이_모두_최근_순으로_아래로_쭉_나열된다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.Bulletins.AddRange(
                new Bulletin { TenantId = _kids.Id, Date = new DateOnly(2027, 1, 3), Title = "새해 첫 주일" },
                new Bulletin { TenantId = _kids.Id, Date = new DateOnly(2027, 1, 10), Title = "추수감사주일" },
                new Bulletin { TenantId = _kids.Id, Date = new DateOnly(2026, 12, 27), Title = "송구영신" },
                new Bulletin { TenantId = _infant.Id, Date = new DateOnly(2027, 1, 17), Title = "다른부서주보" });
            raw.SaveChanges();
        }
        var page = await _app.CreateClient().GetStringAsync("/jamsil-kids/bulletin");
        Assert.DoesNotContain("jb-chip", page);   // 날짜 칩으로 옮겨 다니지 않는다(2026-11-26 사용자)
        Assert.DoesNotContain("다른부서주보", page);   // 다른 부서 주보는 안 보인다
        var a = page.IndexOf("2027년 1월 10일", StringComparison.Ordinal);
        var b = page.IndexOf("2027년 1월 3일", StringComparison.Ordinal);
        var c = page.IndexOf("2026년 12월 27일", StringComparison.Ordinal);
        Assert.True(a > 0 && a < b && b < c);   // 최근 → 오래된 순으로 한 화면에
        Assert.Single(Regex.Matches(page, "class=\"bk-badge\""));   // 「이번 주」 표시는 맨 위 한 권만
    }

    [Fact]
    public async Task R338_다른_부서_주소로는_주보도_파일도_안_열린다()
    {
        var teacher = await Member("sunmi", _kids.Id, Roles.Teacher);
        await Upload(teacher, "jamsil-kids", ("앞면.jpg", Jpg));
        int bid, fid;
        using (var raw = InMemoryDb.Raw(_db)) (bid, fid) = (raw.Bulletins.Single().Id, raw.BulletinFiles.Single().Id);
        var guest = _app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/jamsil-infant/bulletin/file/{fid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/jamsil-infant/bulletin?id={bid}")).StatusCode);
        Assert.Contains("아직 올린 주보가 없어요", await guest.GetStringAsync("/jamsil-infant/bulletin"));
    }

    [Fact]
    public async Task R338_학생은_못_올리고_사진_PDF_아닌_파일과_10개_넘게는_막는다()
    {
        var kid = await Member("cheolsu", _kids.Id, Roles.Participant);
        Assert.NotEqual(HttpStatusCode.OK, (await kid.GetAsync("/jamsil-kids/bulletin/edit")).StatusCode);

        var teacher = await Member("sunmi", _kids.Id, Roles.Teacher);
        var bad = await (await Upload(teacher, "jamsil-kids", ("문서.hwp", "HWP Document"u8.ToArray()))).Content.ReadAsStringAsync();
        Assert.Contains("사진(JPG · PNG)과 PDF만", System.Net.WebUtility.HtmlDecode(bad));
        var many = await (await Upload(teacher, "jamsil-kids", Enumerable.Range(1, 11).Select(i => ($"{i}.jpg", Jpg)).ToArray())).Content.ReadAsStringAsync();
        Assert.Contains("10개까지", many);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.Bulletins);
    }

    [Fact]
    public async Task R338_고치면서_파일을_빼고_지우면_파일도_지워진다()
    {
        var admin = await Member("eunhye", _kids.Id, Roles.Teacher, Roles.TenantAdmin);
        await Upload(admin, "jamsil-kids", ("앞면.jpg", Jpg), ("뒷면.png", Png));
        int bid, gone; string stored;
        using (var raw = InMemoryDb.Raw(_db)) { bid = raw.Bulletins.Single().Id; var f = raw.BulletinFiles.OrderBy(x => x.Id).First(); (gone, stored) = (f.Id, f.StoredName); }
        var token = TokenRx().Match(await admin.GetStringAsync($"/jamsil-kids/bulletin/edit/{bid}")).Groups[1].Value;
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { new StringContent("2027-01-10"), "date" }, { new StringContent("고친 제목"), "title" }, { new StringContent(gone.ToString()), "remove" } };
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync($"/jamsil-kids/bulletin/edit/{bid}", form)).StatusCode);
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal("고친 제목", raw.Bulletins.Single().Title);
            Assert.Single(raw.BulletinFiles);
        }
        Assert.False(File.Exists(Path.Combine(_dir, _kids.Id.ToString(), stored)));

        var t2 = TokenRx().Match(await admin.GetStringAsync("/jamsil-kids/bulletin")).Groups[1].Value;
        await admin.PostAsync("/jamsil-kids/bulletin?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = t2, ["id"] = bid.ToString() }));
        using var after = InMemoryDb.Raw(_db);
        Assert.Empty(after.Bulletins);
        Assert.Empty(after.BulletinFiles);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, _kids.Id.ToString())));
    }
}
