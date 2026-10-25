using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>듣기 · 영상(R-166, ADR 0063, 확정 시안 통독-본문읽기-듣기영상). 오늘 = 2027-01-05 (서울).</summary>
public partial class ChapterMediaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public ChapterMediaTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        ContentSeed.Word(_db, Jan5);
        ContentSeed.Bible(_db);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Kid()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김철수", ["Email"] = "cheolsu@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        var id = raw.Accounts.Single(a => a.Email == "cheolsu@example.com").Id;
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, Roles.Participant);
        raw.Participants.Add(new Participant { TenantId = _kids.Id, MembershipId = m.Id });
        raw.SaveChanges();
        return c;
    }

    private void Seed()
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.ChapterMedia.Add(new ChapterMedia { BookId = 1, Chapter = 24, Kind = ChapterMediaKind.Listen, SortOrder = 1, YoutubeVideoId = "dLv2ndgXrbo", DurationSec = 492 });
        raw.BibleBookOverviews.Add(new BibleBookOverview { BookId = 1, Summary = "세상의 시작", VideoUrl1 = "jIcsdztsKBE", VideoUrl2 = "https://youtu.be/cMKPbDZhToo" });
        raw.SaveChanges();
    }

    [Fact]
    public async Task R166_통독_본문에_장_듣기와_개요_해설_버튼이_보인다()
    {
        Seed();
        var html = await (await Kid()).GetStringAsync("/jamsil-kids/reading/read/1/24");
        Assert.Contains("aria-label=\"듣기 · 영상\"", html);
        Assert.Contains("data-yt=\"dLv2ndgXrbo\" data-yt-title=\"창세기 24장 듣기\"", html);
        Assert.Contains("<b>유튜브 듣기</b><small>창세기 24장 · 8:12</small>", html);   // 영상 길이(초 → 분:초)
        Assert.Contains("data-yt=\"jIcsdztsKBE\"", html);
        Assert.Contains("data-yt=\"cMKPbDZhToo\"", html);                               // 주소에서 영상 번호를 뽑음
        Assert.Contains("<b>개요 해설 2</b>", html);
        Assert.Contains("id=\"ytm\" hidden", html);                                     // 재생 창은 누르기 전엔 숨김
        Assert.Contains("data-rnv-open=\"md\"", html);                               // 도구 줄 [🎧 듣기] 버튼(보기설정 ㄴ안)
        Assert.True(html.IndexOf("data-rnv-pan=\"md\"") < html.IndexOf("class=\"bmd\""));   // 영상 버튼은 듣기 판 안
    }

    [Fact]
    public async Task R166_장_듣기가_없는_장도_책_개요_해설은_보인다()
    {
        Seed();
        var html = await (await Kid()).GetStringAsync("/jamsil-kids/reading/read/1/2");
        Assert.DoesNotContain("유튜브 듣기", html);
        Assert.Contains("<b>개요 해설 1</b>", html);
    }

    [Fact]
    public async Task R166_영상이_하나도_없으면_줄을_숨긴다()
    {
        var html = await (await Kid()).GetStringAsync("/jamsil-kids/reading/read/1/24");
        Assert.DoesNotContain("class=\"bmd\"", html);
        Assert.DoesNotContain("id=\"ytm\"", html);
        Assert.DoesNotContain("data-rnv-open=\"md\"", html);                          // 듣기 버튼도 없음
        Assert.Contains("<b>24장</b><small>/ 50장</small>", html);                   // 도구 줄: 장 · 보기(처음엔 접힘)
        Assert.Contains("<b>전체 · 개역개정</b>", html);
        Assert.Contains("data-rnv-open=\"ch\" aria-expanded=\"false\"", html);
    }

    [Fact]
    public async Task R166_말씀_성경_읽기에도_구절이_시작하는_장의_영상이_보인다()
    {
        Seed();
        var html = await (await Kid()).GetStringAsync("/jamsil-kids/read?from=word");   // 창세기 24:1~9
        Assert.Contains("data-yt=\"dLv2ndgXrbo\"", html);
        Assert.Contains("class=\"rnv-bar two\"", html);                                // 같은 도구 줄 — 장 번호판 없이 [보기] [듣기]
        Assert.Contains("data-rnv-open=\"set\" aria-expanded=\"false\"", html);
        Assert.Contains("data-rnv-open=\"md\"", html);
        Assert.DoesNotContain("data-rnv-open=\"ch\"", html);
    }

    [Theory]
    [InlineData("dLv2ndgXrbo", "dLv2ndgXrbo")]
    [InlineData(" https://www.youtube.com/watch?v=dLv2ndgXrbo&t=3 ", "dLv2ndgXrbo")]
    [InlineData("https://youtu.be/dLv2ndgXrbo", "dLv2ndgXrbo")]
    [InlineData("https://www.youtube.com/embed/dLv2ndgXrbo?rel=0", "dLv2ndgXrbo")]
    [InlineData("https://m.youtube.com/shorts/dLv2ndgXrbo", "dLv2ndgXrbo")]
    [InlineData("https://evil.example/watch?v=dLv2ndgXrbo", null)]                   // 유튜브 주소만
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void R166_영상_번호는_유튜브_주소나_번호에서만_뽑는다(string? value, string? expected) =>
        Assert.Equal(expected, ChapterMediaService.VideoId(value));

    [Fact]
    public void R166_같은_개요_영상은_한_번만_시편은_편()
    {
        var b = ChapterMediaService.Buttons(19, 23,
            [new ChapterMedia { BookId = 19, Chapter = 23, Kind = ChapterMediaKind.Listen, SortOrder = 1, YoutubeVideoId = "bad id" }],
            ["jIcsdztsKBE", "https://youtu.be/jIcsdztsKBE"]);
        Assert.Single(b);                                   // 듣기는 영상 번호가 틀려 빠지고, 같은 개요 영상은 하나로
        Assert.Equal("개요 해설", b[0].Title);
        Assert.Equal("시편 개요 해설", b[0].PlayerTitle);
        var l = ChapterMediaService.Buttons(19, 23, [new ChapterMedia { BookId = 19, Chapter = 23, Kind = ChapterMediaKind.Listen, SortOrder = 1, YoutubeVideoId = "dLv2ndgXrbo" }], []);
        Assert.Equal("시편 23편", l[0].Sub);
    }
}
