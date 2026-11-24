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
/// R-368 ~ R-371 부서 소개 화면(ADR 0140, 2026-11-23 사용자 "유년부 전용 · 처음 들어온 사람에게 먼저"):
/// 소개 줄이 있는 부서(유년부)만 소개 화면이 있고, 들어올 때 먼저 보여 준다. 「1주일 후 다시 보기」 · 「다시 보지 않기」는 계정 기준.
/// 다른 부서에는 소개 화면이 없다. 글 · 사진은 그 부서 관리자만 고친다.
/// </summary>
public partial class IntroTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-intro-" + Guid.NewGuid().ToString("N"));
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _infant;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9];
    private static readonly byte[] Pdf = "%PDF-1.4 test"u8.ToArray();

    public IntroTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("Intro:Dir", _dir);
            b.ConfigureServices(s =>
            {
                s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
                s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(_time);
            });
        });
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _infant = InMemoryDb.SeedTenant(_db, "jamsil-infant", "JSINFA");
        using var raw = InMemoryDb.Raw(_db);
        raw.TenantIntros.Add(new TenantIntro { TenantId = _kids.Id, Title = "유년부에 오신 것을 환영해요", Body = "말씀 안에서 자라 가는 유년부예요.", IsActive = true, UpdatedUtc = _time.Now.UtcDateTime });
        raw.WorshipSchedules.Add(new WorshipSchedule { TenantId = _kids.Id, SortOrder = 1, DayOfWeek = 0, StartTime = new TimeOnly(10, 0), Place = "비전센터 믿음홀(3F)", Address = "서울특별시 송파구 석촌호수로 102", IsActive = true });
        raw.SaveChanges();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private HttpClient NewClient() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> Member(string email, int tenantId, params string[] roles)
    {
        var c = NewClient();
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김철수", ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        string id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Users.Single(a => a.Email == email + "@example.com").UserName!;
        InMemoryDb.SeedMembership(_db, tenantId, id, MembershipStatus.Approved, roles);
        return c;
    }

    /// <summary>같은 계정으로 다른 기기(쿠키 없는 새 브라우저)에서 로그인.</summary>
    private async Task<HttpClient> Login(string email)
    {
        var c = NewClient();
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value,
            ["Email"] = email + "@example.com", ["Password"] = "1234",
        }));
        return c;
    }

    private static bool GoesToWelcome(HttpResponseMessage r, string slug = "jamsil-kids") =>
        r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location?.ToString() == $"/{slug}/welcome";

    /// <summary>다른 화면(from)에서 링크를 눌러 온 요청(Referer).</summary>
    private static Task<HttpResponseMessage> FromPage(HttpClient c, string url, string from)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Referrer = new Uri("http://localhost" + from);
        return c.SendAsync(req);
    }

    private async Task<HttpResponseMessage> PostWelcome(HttpClient c, string handler)
    {
        var token = TokenRx().Match(await c.GetStringAsync("/jamsil-kids/welcome")).Groups[1].Value;
        return await c.PostAsync($"/jamsil-kids/welcome?handler={handler}", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }

    [Fact]   // R-368 — 소개 줄이 있는 부서(유년부)만. 다른 부서는 그대로 홈이 열리고 소개 화면 주소는 홈으로 돌려보낸다
    public async Task R368_소개_화면은_유년부_전용이고_다른_부서에는_없다()
    {
        var guest = NewClient();
        Assert.True(GoesToWelcome(await guest.GetAsync("/jamsil-kids")));                     // 유년부: 손님도 들어오면 먼저 소개 화면

        var home = await guest.GetAsync("/jamsil-infant");                                    // 다른 부서: 소개 없이 홈
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var w = await guest.GetAsync("/jamsil-infant/welcome");
        Assert.Equal(HttpStatusCode.Redirect, w.StatusCode);
        Assert.Equal("/jamsil-infant?intro=0", w.Headers.Location?.ToString());
        Assert.DoesNotContain("환영해요", await guest.GetStringAsync("/jamsil-infant"));

        var infantMember = await Member("mina", _infant.Id, Roles.Participant);
        Assert.Equal(HttpStatusCode.OK, (await infantMember.GetAsync("/jamsil-infant")).StatusCode);

        using var raw = InMemoryDb.Raw(_db);
        raw.TenantIntros.Single(i => i.TenantId == _kids.Id).IsActive = false;               // 꺼 두면 유년부도 소개 없이 홈
        raw.SaveChanges();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/jamsil-kids")).StatusCode);
    }

    [Fact]   // 웰컴 / 웰컴2(2026-11-24) — 같은 화면, 「오늘의 안내」만 표 ↔ 색 카드. 위쪽 버튼으로 서로 오간다
    public async Task 웰컴2는_같은_화면에서_안내_칸만_카드_모양이고_버튼으로_오간다()
    {
        var guest = NewClient();
        var a = await guest.GetStringAsync("/jamsil-kids/welcome");
        var b = await guest.GetStringAsync("/jamsil-kids/welcome2");
        Assert.Contains("class=\"wc2-grid\"", a);
        Assert.DoesNotContain("class=\"wc2-cards\"", a);
        Assert.Contains("href=\"/jamsil-kids/welcome2\"", a);      // 카드로 보기
        Assert.Contains("class=\"wc2-cards\"", b);
        Assert.DoesNotContain("class=\"wc2-grid\"", b);
        Assert.Contains("href=\"/jamsil-kids/welcome\"", b);       // 표로 보기
        foreach (var page in new[] { a, b })                         // 기능은 같다
        {
            Assert.Contains("href=\"/jamsil-kids/bulletin\"", page);
            Assert.Contains("href=\"/jamsil-kids/album\"", page);
            Assert.Contains("href=\"/jamsil-kids/welcome?handler=Go\"", page);
            Assert.Contains("유년부에 오신 것을 환영해요", page);
        }
        Assert.Contains("가정예배용", b);
        var kid = await Member("cheolsu", _kids.Id, Roles.Participant);
        Assert.Contains("다시 보지 않기", await kid.GetStringAsync("/jamsil-kids/welcome2"));
    }

    [Fact]   // R-369 — 화면 구성: 글 · 예배 안내 · 주보 · 사진첩 · 성경말씀으로 가기. 손님에게는 「다시 보기」 버튼이 없다
    public async Task R369_소개_화면에는_글과_예배_안내_주보_사진첩_이동이_있다()
    {
        var guest = NewClient();
        var page = await guest.GetStringAsync("/jamsil-kids/welcome");
        Assert.Contains("유년부에 오신 것을 환영해요", page);
        Assert.Contains("말씀 안에서 자라 가는 유년부예요.", page);
        Assert.Contains("주일 오전 10:00", page);
        Assert.Contains("비전센터 믿음홀(3F)", page);
        Assert.Contains("https://map.naver.com/p/search/", page);
        Assert.Contains("href=\"/jamsil-kids/bulletin\"", page);
        Assert.Contains("href=\"/jamsil-kids/album\"", page);
        Assert.Contains("href=\"/jamsil-kids/welcome?handler=Go\"", page);
        Assert.DoesNotContain("1주일 후 다시 보기", page);                                   // 손님: 기억할 계정이 없다
        Assert.DoesNotContain("다시 보지 않기", page);

        var kid = await Member("cheolsu", _kids.Id, Roles.Participant);
        var mine = await kid.GetStringAsync("/jamsil-kids/welcome");
        Assert.Contains("1주일 후 다시 보기", mine);
        Assert.Contains("다시 보지 않기", mine);
    }

    [Fact]   // R-370 — 처음 들어온 계정에게 먼저. 「가기」는 이번 방문만, 「1주일 후」 · 「다시 보지 않기」는 계정 기준(다른 기기에서도)
    public async Task R370_처음_들어온_사람에게_먼저_보이고_계정_기준으로_기억한다()
    {
        var a = await Member("cheolsu", _kids.Id, Roles.Participant);
        Assert.True(GoesToWelcome(await a.GetAsync("/jamsil-kids")));                         // 처음

        var go = await a.GetAsync("/jamsil-kids/welcome?handler=Go");                         // 「성경말씀으로 가기」
        Assert.Equal("/jamsil-kids", go.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await FromPage(a, "/jamsil-kids", "/jamsil-kids/welcome")).StatusCode);     // 소개 화면에서 온 요청은 되돌려 보내지 않는다
        Assert.Equal(HttpStatusCode.OK, (await FromPage(a, "/jamsil-kids", "/jamsil-kids/bulletin")).StatusCode);    // 주보 · 사진첩 등 그 부서 화면에서 「부서 홈」으로 와도 마찬가지
        Assert.True(GoesToWelcome(await a.GetAsync("/jamsil-kids")));                         // 버그 회귀(2026-11-23): 「가기」를 눌렀어도 **두 번째 방문**(주소창 · 즐겨찾기)에는 또 나온다
        Assert.True(GoesToWelcome(await FromPage(a, "/jamsil-kids", "/account/login")));      // 첫 화면 · 로그인에서 들어와도 나온다(그 부서 화면이 아니다)
        Assert.True(GoesToWelcome(await NewClient().GetAsync("/jamsil-kids")));               // 손님도 들어올 때마다
        var other = await Login("cheolsu");

        var later = await PostWelcome(other, "Later");                                         // 1주일 후 다시 보기
        Assert.Equal(HttpStatusCode.Redirect, later.StatusCode);
        var third = await Login("cheolsu");
        Assert.Equal(HttpStatusCode.OK, (await third.GetAsync("/jamsil-kids")).StatusCode);   // 다른 기기에서도 숨김
        _time.Now = _time.Now.AddDays(6);
        Assert.Equal(HttpStatusCode.OK, (await (await Login("cheolsu")).GetAsync("/jamsil-kids")).StatusCode);   // 6일 뒤: 아직
        _time.Now = _time.Now.AddDays(2);
        Assert.True(GoesToWelcome(await (await Login("cheolsu")).GetAsync("/jamsil-kids")));  // 8일 뒤: 다시 나옴

        var never = await Login("cheolsu");
        await PostWelcome(never, "Never");                                                    // 다시 보지 않기
        _time.Now = _time.Now.AddDays(400);
        var again = await Login("cheolsu");
        var home = await again.GetAsync("/jamsil-kids");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("href=\"/jamsil-kids/welcome\"", await home.Content.ReadAsStringAsync());   // ☰ 메뉴에서는 언제든 다시 본다

        var b = await Member("younghee", _kids.Id, Roles.Participant);                        // 다른 계정은 영향 없음
        Assert.True(GoesToWelcome(await b.GetAsync("/jamsil-kids")));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Single(raw.TenantIntroSeens);                                                  // 계정 한 명 = 한 줄(부서 격리 키)
        Assert.Equal(_kids.Id, raw.TenantIntroSeens.Single().TenantId);
    }

    private static async Task<HttpResponseMessage> Save(HttpClient c, string slug, string title, string body, byte[]? photo = null, string photoName = "사진.png", bool removePhoto = false)
    {
        var token = TokenRx().Match(await c.GetStringAsync($"/{slug}/admin/intro")).Groups[1].Value;
        var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(title), "title" }, { new StringContent(body), "body" }, { new StringContent("true"), "active" },
        };
        if (removePhoto) form.Add(new StringContent("true"), "removePhoto");
        if (photo is not null)
        {
            var f = new ByteArrayContent(photo);
            f.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(f, "photo", photoName);
        }
        return await c.PostAsync($"/{slug}/admin/intro?handler=Save", form);
    }

    [Fact]   // R-371 — 관리자만 고친다: 제목 · 글 · 사진(JPG · PNG), 기록이 남는다. 줄이 없는 부서는 고칠 화면도 없다
    public async Task R371_부서_관리자가_글과_사진을_고치고_다른_부서는_못_고친다()
    {
        var admin = await Member("eunhye", _kids.Id, Roles.Teacher, Roles.TenantAdmin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/jamsil-kids/admin/intro")).StatusCode);
        var editHtml = await admin.GetStringAsync("/jamsil-kids/admin/intro");
        Assert.Contains("data-photo=\"wide\" data-photo-ratio=\"1.6\" data-photo-size=\"1280\" data-photo-defer", editHtml);   // 대표 사진은 가로 16:10 틀로 잘라서 올린다(2026-11-24)
        Assert.Matches("src=\"/js/photo(\\.[a-z0-9]+)?\\.js\"", editHtml);   // 자르기 스크립트(파일 이름에 지문이 붙는다)

        var r = await Save(admin, "jamsil-kids", "함께 자라요", "새 소개 글", Png);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var guest = NewClient();
        var page = await guest.GetStringAsync("/jamsil-kids/welcome");
        Assert.Contains("함께 자라요", page);
        Assert.Contains("새 소개 글", page);
        Assert.Contains("src=\"/jamsil-kids/welcome/photo?v=", page);
        var photo = await guest.GetAsync("/jamsil-kids/welcome/photo");                        // 사진은 누구나, 파일은 부서 폴더
        Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var row = raw.TenantIntros.Single();
            Assert.True(File.Exists(Path.Combine(_dir, _kids.Id.ToString(), row.PhotoStoredName!)));
            Assert.Contains(raw.AuditLogs, l => l.Action == "TenantIntro" && l.TenantId == _kids.Id);
        }

        var bad = await Save(admin, "jamsil-kids", "그대로여야 해요", "x", Pdf, "주보.pdf");     // PDF 는 사진이 아니다
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("사진(JPG · PNG)만", await bad.Content.ReadAsStringAsync());
        Assert.Contains("함께 자라요", await guest.GetStringAsync("/jamsil-kids/welcome"));      // 저장 안 됨
        var empty = await Save(admin, "jamsil-kids", " ", "x");
        Assert.Contains("제목을 넣어 주세요", await empty.Content.ReadAsStringAsync());

        await Save(admin, "jamsil-kids", "함께 자라요", "새 소개 글", removePhoto: true);          // 사진 빼기
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/jamsil-kids/welcome/photo")).StatusCode);

        var teacher = await Member("sunmi", _kids.Id, Roles.Teacher);                          // 교사는 못 고친다
        Assert.NotEqual(HttpStatusCode.OK, (await teacher.GetAsync("/jamsil-kids/admin/intro")).StatusCode);

        var infantAdmin = await Member("mina", _infant.Id, Roles.Teacher, Roles.TenantAdmin); // 다른 부서 관리자: 줄이 없으니 화면도 없다
        Assert.Equal(HttpStatusCode.NotFound, (await infantAdmin.GetAsync("/jamsil-infant/admin/intro")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await infantAdmin.GetAsync("/jamsil-infant/welcome/photo")).StatusCode);
        Assert.DoesNotContain("소개 화면", await infantAdmin.GetStringAsync("/jamsil-infant/admin"));   // 관리 메뉴에도 없다
    }

    [Fact]   // 버그 수정(2026-11-24 사용자 신고) — 로그인한 사람의 얇은 머리말(주보 · 막힌 화면 등)에 「@if (User…」 소스가 글자로 찍히던 문제
    public async Task 얇은_머리말에_소스_글자가_찍히지_않는다()
    {
        var kid = await Member("cheolsu", _kids.Id, Roles.Participant);
        foreach (var url in new[] { "/jamsil-kids/bulletin", "/jamsil-kids/admin/intro" })   // 둘 다 얇은 머리말(_Top)을 쓴다(뒤쪽은 권한이 없어 막힌 화면)
        {
            var r = await kid.GetAsync(url);
            var html = r.StatusCode == HttpStatusCode.Redirect ? await (await kid.GetAsync(r.Headers.Location!.ToString())).Content.ReadAsStringAsync() : await r.Content.ReadAsStringAsync();
            Assert.DoesNotContain("@if", html);
            Assert.DoesNotContain("ImpersonatorId", html);
        }
        Assert.Contains("class=\"hb\"", await kid.GetStringAsync("/jamsil-kids/bulletin"));   // 머리줄 오른쪽은 이동 버튼(이름 알약은 부서 홈 · 첫 화면에만)
    }

    [Fact]   // 머리줄 ② 얇은 머리말(2026-11-24 확정 · 기준표 5판): 부서 안 = ☰ + 부서 › 화면 + 알약 · 밖 = ‹ 뒤로 · 「도전!!! 성경말씀」 글자 없음 · 로그아웃은 ☰ 메뉴로
    public async Task 얇은_머리말은_부서_안에서는_메뉴와_경로이고_밖에서는_뒤로_가기이다()
    {
        var kid = await Member("cheolsu", _kids.Id, Roles.Participant);
        var bul = await kid.GetStringAsync("/jamsil-kids/bulletin");
        Assert.Contains("data-pnav-open", bul);                                   // ☰ 메뉴가 새로 생김
        Assert.DoesNotContain("class=\"par\"", bul);                            // 부서 이름(유년부 ›)은 머리줄에서 뺐다(2026-11-24 사용자)
        Assert.Contains("class=\"cur\">주보</b>", bul);                           // 현재 화면
        Assert.Contains("class=\"hb\" href=\"/jamsil-kids\">", bul);                // 오른쪽은 「홈으로」 이동 버튼(알약은 부서 홈 · 첫 화면에만, 기준표 6판)
        Assert.Contains("홈으로</a>", bul);
        Assert.DoesNotContain("class=\"hpill\"", bul);
        Assert.DoesNotContain("class=\"brand\"", bul);                            // 「도전!!! 성경말씀」 글자는 뺐다
        Assert.Contains("id=\"pnav\"", bul);
        Assert.Contains("action=\"/account/logout\"", bul);                       // 로그아웃은 ☰ 메뉴에서

        // 쪽지 화면은 이미 ☰ 머리말 부품(_GenTop)을 써서 ③ 자체 머리줄 묶음에서 맞춘다

        var guest = NewClient();
        var login = await guest.GetStringAsync("/account/login");
        Assert.Contains("aria-label=\"뒤로\"", login);
        Assert.Contains(">가입하기</a>", login);                                    // 반대 이동 링크 하나
        Assert.DoesNotContain("class=\"brand\"", login);
        Assert.DoesNotContain("data-pnav-open", login);
        Assert.Contains("href=\"/account/login\">로그인</a>", await guest.GetStringAsync("/account/signup"));
    }
}
