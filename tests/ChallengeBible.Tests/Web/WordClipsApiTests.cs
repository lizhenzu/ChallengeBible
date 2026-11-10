using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Api;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>말씀 영상 구간(R-127, ADR 0081): API 로 그날 목록을 통째로 넣고, 말씀 읽기 "듣기"가 그 구간을 재생한다. 오늘 = 2027-01-05 (서울).</summary>
public partial class WordClipsApiTests(WebApplicationFactory<Program> app) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Key = "test-key-0123456789-0123456789-0123456789";
    private const string Url = "/api/platform/words/2027-01-05/clips";
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly string _db = InMemoryDb.NewName();

    private WebApplicationFactory<Program> Host(string? key = Key, string? actor = null) => app.WithWebHostBuilder(b =>
    {
        b.UseSetting("ContentApi:Key", key ?? "");
        b.UseSetting("ContentApi:ActorUserName", actor?.ToString() ?? "");
        b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        });
    });

    private HttpClient Client(string? key = Key, string? actor = null) =>
        Host(key, actor).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private string Admin(bool platform = true)
    {
        var a = InMemoryDb.SeedAccount(_db, $"op{Guid.NewGuid():N}@example.com", "운영자");
        using var raw = InMemoryDb.Raw(_db);
        if (platform) InMemoryDb.AddRole(_db, raw.Users.Single(x => x.UserName == a.UserName!).Id, IdentityRoles.SuperAdmin);
        raw.SaveChanges();
        return a.UserName!;
    }

    private static HttpRequestMessage Put(object body, string? key = Key, string url = Url)
    {
        var m = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        if (key is not null) m.Headers.Add(ContentApi.KeyHeader, key);
        return m;
    }

    private static readonly object Two = new
    {
        clips = new object[]
        {
            new { chapter = 24, youtube = "https://youtu.be/dLv2ndgXrbo", start = "1:05", end = "12:34" },
            new { chapter = 25 },
        },
    };

    private List<DailyWordClip> Saved(DateOnly? date = null)
    {
        using var raw = InMemoryDb.Raw(_db);
        var day = date ?? Jan5;
        return raw.DailyWordClips.Where(c => c.WordDate == day).OrderBy(c => c.Seq).ToList();
    }

    [Fact]
    public async Task R127_키와_날짜와_계정은_다른_입력_API_와_똑같이_막는다()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client(key: null, actor: Admin()).SendAsync(Put(Two))).StatusCode);
        var c = Client(actor: Admin());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Put(Two, key: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Put(Two, key: "틀린 키"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Put(Two, url: "/api/platform/words/2027-13-01/clips"))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client().SendAsync(Put(Two))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client(actor: Admin(platform: false)).SendAsync(Put(Two))).StatusCode);
        Assert.Empty(Saved());
    }

    [Fact]
    public async Task R127_보낸_차례대로_저장하고_시간은_초로_영상은_번호로_바꾼다()
    {
        var res = await Client(actor: Admin()).SendAsync(Put(Two));   // 그날 말씀이 없어도 저장된다
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using (var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()))
        {
            Assert.Equal("2027-01-05", json.RootElement.GetProperty("date").GetString());
            Assert.Equal(2, json.RootElement.GetProperty("count").GetInt32());
        }
        var all = Saved();
        Assert.Equal(2, all.Count);
        Assert.Equal((0, (short)24, "dLv2ndgXrbo", 65, 754), (all[0].Seq, all[0].Chapter, all[0].YoutubeVideoId, all[0].StartSec!.Value, all[0].EndSec!.Value));
        Assert.Equal((1, (short)25, null, null, null), ((int)all[1].Seq, all[1].Chapter, all[1].YoutubeVideoId, all[1].StartSec, all[1].EndSec));
    }

    [Fact]
    public async Task R127_장의_범위와_차례는_보지_않는다_영시영분은_처음과_끝이다()
    {
        var body = new { clips = new object[] { new { chapter = 150, start = "0:00", end = "0:00" }, new { chapter = 1 } } };
        Assert.Equal(HttpStatusCode.OK, (await Client(actor: Admin()).SendAsync(Put(body))).StatusCode);
        var all = Saved();
        Assert.Equal([(short)150, (short)1], all.Select(c => c.Chapter));
        Assert.Null(all[0].StartSec);
        Assert.Null(all[0].EndSec);
    }

    [Fact]
    public async Task R127_다시_넣으면_그날_목록만_통째로_바뀌고_빈_목록은_지운다()
    {
        var c = Client(actor: Admin());
        await c.SendAsync(Put(Two));
        await c.SendAsync(Put(Two, url: "/api/platform/words/2027-01-06/clips"));

        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Put(new { clips = new object[] { new { chapter = 7 } } }))).StatusCode);
        Assert.Equal([(short)7], Saved().Select(x => x.Chapter));

        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Put(new { clips = Array.Empty<object>() }))).StatusCode);
        Assert.Empty(Saved());
        Assert.Equal(2, Saved(new DateOnly(2027, 1, 6)).Count);   // 다른 날은 그대로
    }

    [Fact]
    public async Task R127_목록_칸이_빠지면_지우지_않고_오류다()
    {
        var c = Client(actor: Admin());
        await c.SendAsync(Put(Two));
        var res = await c.SendAsync(Put(new { }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("\"clips\"", await res.Content.ReadAsStringAsync());
        Assert.Equal(2, Saved().Count);
    }

    [Theory]
    [InlineData(0, null, null, null, "clips[0].chapter")]
    [InlineData(151, null, null, null, "clips[0].chapter")]
    [InlineData(1, "https://evil.example/watch?v=dLv2ndgXrbo", null, null, "clips[0].youtube")]
    [InlineData(1, "짧은값", null, null, "clips[0].youtube")]
    [InlineData(1, null, "12", null, "clips[0].start")]
    [InlineData(1, null, "1:60", null, "clips[0].start")]
    [InlineData(1, null, "1:02:03", null, "clips[0].start")]
    [InlineData(1, null, "-1:00", null, "clips[0].start")]
    [InlineData(1, null, null, "a:bc", "clips[0].end")]
    [InlineData(1, null, "2:00", "1:00", "clips[0].end")]
    [InlineData(1, null, "2:00", "2:00", "clips[0].end")]
    public async Task R127_형식이_틀리면_해당_칸을_알려_주고_기존_목록을_지우지_않는다(int chapter, string? youtube, string? start, string? end, string field)
    {
        var c = Client(actor: Admin());
        await c.SendAsync(Put(Two));
        var res = await c.SendAsync(Put(new { clips = new object[] { new { chapter, youtube, start, end } } }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("Invalid", json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(2, Saved().Count);
    }

    [Fact]
    public async Task R127_구간은_쉰개까지다()
    {
        var c = Client(actor: Admin());
        var ok = new { clips = Enumerable.Range(1, ContentApi.MaxClips).Select(i => new { chapter = i }).ToArray() };
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Put(ok))).StatusCode);
        var over = new { clips = Enumerable.Range(1, ContentApi.MaxClips + 1).Select(i => new { chapter = 1 }).ToArray() };
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Put(over))).StatusCode);
    }

    // ── 화면 ──

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Kid()
    {
        var kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        ContentSeed.Word(_db, Jan5);                       // 창세기 24:1~9
        ContentSeed.Word(_db, Jan5.AddDays(-1));
        ContentSeed.Bible(_db);
        var c = Host().CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김철수", ["Email"] = "cheolsu@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        var id = raw.Users.Single(a => a.Email == "cheolsu@example.com").UserName!;
        var m = InMemoryDb.SeedMembership(_db, kids.Id, id, MembershipStatus.Approved, Roles.Participant);
        raw.ChapterMedia.Add(new ChapterMedia { BookId = 1, Chapter = 24, Kind = ChapterMediaKind.Listen, SortOrder = 1, YoutubeVideoId = "dLv2ndgXrbo", DurationSec = 492 });
        raw.ChapterMedia.Add(new ChapterMedia { BookId = 1, Chapter = 25, Kind = ChapterMediaKind.Listen, SortOrder = 1, YoutubeVideoId = "cMKPbDZhToo" });
        raw.SaveChanges();
        return c;
    }

    private void SeedClips(DateOnly date, params DailyWordClip[] clips)
    {
        using var raw = InMemoryDb.Raw(_db);
        byte seq = 0;
        foreach (var c in clips) { c.WordDate = date; c.Seq = seq++; raw.DailyWordClips.Add(c); }
        raw.SaveChanges();
    }

    private static string? ClipsOf(string html)
    {
        var m = Regex.Match(html, "data-yt-clips=\"([^\"]+)\"");   // Razor 는 data- 칸을 비어도 그린다(재생 창은 빈 값을 전체 재생으로 읽음)
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    [Fact]
    public async Task R127_구간이_있는_날_듣기는_구간을_차례로_재생하고_빈_영상은_장_기본_영상이다()
    {
        var c = await Kid();
        SeedClips(Jan5,
            new DailyWordClip { Chapter = 24, StartSec = 65, EndSec = 90 },
            new DailyWordClip { Chapter = 25 },
            new DailyWordClip { Chapter = 99 },                                          // 기본 영상이 없는 장은 건너뜀
            new DailyWordClip { Chapter = 99, YoutubeVideoId = "jIcsdztsKBE", EndSec = 30 });
        var html = await c.GetStringAsync("/jamsil-kids/read?from=word");
        Assert.Equal("[{\"v\":\"dLv2ndgXrbo\",\"s\":65,\"e\":90},{\"v\":\"cMKPbDZhToo\",\"s\":0,\"e\":null},{\"v\":\"jIcsdztsKBE\",\"s\":0,\"e\":30}]", ClipsOf(html));
        Assert.Contains("<span><em>24장</em>1:05 ~ 1:30</span>", html);                 // 버튼에 재생 구간(확정 시안 말씀듣기-clips ㄱ안)
        Assert.Contains("<span><em>25장</em>전체</span>", html);
        Assert.Contains("<span><em>99장</em>처음 ~ 0:30</span>", html);
        Assert.Equal(3, Regex.Matches(html, "<em>\\d+장</em>").Count);                  // 건너뛴 구간은 글에도 없음
        Assert.DoesNotContain("8:12", html);                                           // 장 전체 길이는 뺌
    }

    [Theory]
    [InlineData(null, null, "전체")]
    [InlineData(65, 460, "1:05 ~ 7:40")]
    [InlineData(380, null, "6:20 ~ 끝까지")]
    [InlineData(null, 70, "처음 ~ 1:10")]
    public void R127_구간_글은_분초와_처음_끝으로_쓴다(int? start, int? end, string text) =>
        Assert.Equal(text, ChapterMediaService.ClipRange(start, end));

    [Fact]
    public async Task R127_구간이_없는_날은_지금처럼_장_전체다()
    {
        var c = await Kid();
        SeedClips(Jan5.AddDays(1), new DailyWordClip { Chapter = 24, EndSec = 90 });     // 다른 날 구간은 섞이지 않는다
        var html = await c.GetStringAsync("/jamsil-kids/read?from=word");
        Assert.Null(ClipsOf(html));
        Assert.Contains("data-yt=\"dLv2ndgXrbo\"", html);
        Assert.Contains("<small>24장 전체 · 8:12</small>", html);
        Assert.DoesNotContain("class=\"clp\"", html);
    }

    [Fact]
    public async Task R127_지난_날을_열면_그날_구간이고_미션에서_오지_않으면_구간이_없다()
    {
        var c = await Kid();
        SeedClips(Jan5.AddDays(-1), new DailyWordClip { Chapter = 24, StartSec = 5 });
        Assert.Contains("\"s\":5", ClipsOf(await c.GetStringAsync("/jamsil-kids/read?from=word&date=2027-01-04")));
        Assert.Null(ClipsOf(await c.GetStringAsync("/jamsil-kids/read?from=word")));

        SeedClips(Jan5, new DailyWordClip { Chapter = 24, StartSec = 5 });
        Assert.Null(ClipsOf(await c.GetStringAsync("/jamsil-kids/read?book=1&sc=24&sv=1&ec=24&ev=9")));
        Assert.Null(ClipsOf(await c.GetStringAsync("/jamsil-kids/reading/read/1/24")));   // 통독은 장 전체 그대로
    }

    [Fact]
    public void R127_장에_듣기_영상이_없어도_구간에_영상이_있으면_듣기_버튼이_생기고_쓸_구간이_없으면_그대로다()
    {
        var made = ChapterMediaService.WithClips([], 19, 23, [new DailyWordClip { Chapter = 23, YoutubeVideoId = "dLv2ndgXrbo" }], []);
        var b = Assert.Single(made);
        Assert.True(b.Listen);
        Assert.Equal(("시편 23편", "dLv2ndgXrbo"), (b.Sub, b.VideoId));
        Assert.NotNull(b.Clips);

        Assert.Empty(ChapterMediaService.WithClips([], 19, 23, [new DailyWordClip { Chapter = 23 }], []));
    }
}
