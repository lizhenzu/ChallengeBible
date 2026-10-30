using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>미션 화면 + 버튼 API(API 계약 5-1). 오늘 = 2027-01-05 (서울).</summary>
public partial class MissionFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public MissionFlowTests(WebApplicationFactory<Program> app)
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
        ContentSeed.ChoiceQuiz(_db, Jan5);
        ContentSeed.Bible(_db);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Member(string email, params string[] roles)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김철수", ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        var id = raw.Accounts.Single(a => a.Email == email + "@example.com").Id;
        var m = new Membership { TenantId = _kids.Id, AccountId = id, Status = MembershipStatus.Approved,
            Roles = roles.Select(r => new MembershipRole { TenantId = _kids.Id, Role = r }).ToList() };
        raw.Memberships.Add(m);
        raw.SaveChanges();
        if (roles.Contains(Roles.Participant)) { raw.Participants.Add(new Participant { TenantId = _kids.Id, MembershipId = m.Id }); raw.SaveChanges(); }
        return c;
    }

    /// <summary>미션 화면의 위조 방지 토큰을 API 헤더로.</summary>
    private static async Task<string> PageToken(HttpClient c, string url) => TokenRx().Match(await c.GetStringAsync(url)).Groups[1].Value;

    private static async Task<HttpResponseMessage> Api(HttpClient c, string token, string path, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        req.Headers.Add("RequestVerificationToken", token);
        return await c.SendAsync(req);
    }

    [Fact]
    public async Task 말씀_화면에서_누르면_점수가_오른다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        Assert.Contains("믿음의 종의 기도", html);
        Assert.Contains("창세기 24장 1~9절", html);

        var r = await Api(c, TokenRx().Match(html).Groups[1].Value, "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = new[] { "Word.Me" } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, j.GetProperty("pointsEarned").GetInt32());
        Assert.Equal(1, j.GetProperty("seasonTotal").GetInt32());
        Assert.Equal("+1점! 올해 총 1점", j.GetProperty("message").GetString());

        Assert.Contains("aria-pressed=\"true\"", await c.GetStringAsync("/jamsil-kids/word"));
    }

    [Fact]
    public async Task R24_오늘_완료는_다시_눌러_취소한다_지난날과_퀴즈는_안_된다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        var t = TokenRx().Match(html).Groups[1].Value;
        await Api(c, t, "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = new[] { "Word.Me" } });
        var page = await c.GetStringAsync("/jamsil-kids/word");
        Assert.Contains("data-undo=\"1\"", page);
        Assert.Matches("data-item=\"Word.Me\" data-pt=\"1\" aria-pressed=\"true\">", page);   // 완료 버튼도 눌린다(disabled 없음 → 확인 창)
        Assert.Contains("한 번 더 눌러 취소해요 (오늘만)", page);

        var r = await Api(c, t, "/api/jamsil-kids/missions/undo", new { date = "2027-01-05", item = "Word.Me" });
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal((-1, 0, "취소했어요 · -1점"), (j.GetProperty("pointsEarned").GetInt32(), j.GetProperty("seasonTotal").GetInt32(), j.GetProperty("message").GetString()));
        Assert.Empty(j.GetProperty("completedItems").EnumerateArray());
        Assert.DoesNotContain("aria-pressed=\"true\"", await c.GetStringAsync("/jamsil-kids/word"));

        Assert.Equal(HttpStatusCode.BadRequest, (await Api(c, t, "/api/jamsil-kids/missions/undo", new { date = "2027-01-05", item = "Quiz.Correct" })).StatusCode);
        Assert.DoesNotContain("data-undo=\"1\"", await c.GetStringAsync("/jamsil-kids/word?date=2027-01-04"));   // 지난 날짜 화면은 취소 없음
    }

    [Fact]
    public async Task R23_아무것도_고르지_않으면_거절()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var t = await PageToken(c, "/jamsil-kids/word");
        var r = await Api(c, t, "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("NoItemSelected", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 퀴즈_정답이면_정답_보기와_해설을_돌려준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        int correct;
        using (var raw = InMemoryDb.Raw(_db)) correct = raw.DailyQuizzes.Single().CorrectChoiceId!.Value;
        var t = await PageToken(c, "/jamsil-kids/quiz");
        var r = await Api(c, t, "/api/jamsil-kids/missions/quiz/attempts", new { date = "2027-01-05", choiceId = correct });
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j.GetProperty("isCorrect").GetBoolean());
        Assert.Equal(correct, j.GetProperty("correctChoiceId").GetInt32());
        Assert.Contains("기도했어요", j.GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task 보호자는_보기만_하고_누를_수_없다()
    {
        var c = await Member("younghee", Roles.Guardian);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        Assert.Contains("보기만 해요", html);
        var r = await Api(c, TokenRx().Match(html).Groups[1].Value, "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = new[] { "Word.Me" } });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task 위조_방지_토큰이_없으면_거절()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var r = await Api(c, "nope", "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = new[] { "Word.Me" } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task 내_점수는_42칸_달력과_날짜별_표()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var t = await PageToken(c, "/jamsil-kids/word");
        await Api(c, t, "/api/jamsil-kids/missions/word", new { date = "2027-01-05", items = new[] { "Word.Me", "Word.Parent" } });

        var html = await c.GetStringAsync("/jamsil-kids/points");
        Assert.Equal(42, Regex.Matches(html, "class=\"c ").Count);   // R-70
        Assert.Contains("2027년 1월", html);
        Assert.Contains("aria-label=\"1월 5일 2점\"", html);
        Assert.Equal("/jamsil-kids/points?month=2027-01", (await c.GetAsync("/jamsil-kids/calendar?month=2027-01")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task R110_성경_읽기는_미션의_구절을_단위만큼_나눠_보여준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var all = await c.GetStringAsync("/jamsil-kids/read?from=word");   // 기본: 개역개정 · 전체(확정 시안 v2)
        Assert.Contains("KRV 1:24:9", all);
        Assert.DoesNotContain("brd-pager", all);                           // 한 쪽이면 쪽 이동 없음
        var p1 = await c.GetStringAsync("/jamsil-kids/read?from=word&unit=3");
        Assert.Contains("창세기", p1);
        Assert.Contains("24장 1~9절", p1);
        Assert.Contains("KRV 1:24:1", p1);
        Assert.Contains("KRV 1:24:3", p1);
        Assert.DoesNotContain("KRV 1:24:4", p1);
        Assert.DoesNotContain("NIV 1:24:1", p1);
        Assert.Contains("1–3절 / 9", p1);
        var p2 = await c.GetStringAsync("/jamsil-kids/read?from=word&unit=3&p=2");
        Assert.Contains("KRV 1:24:4", p2);
        Assert.Contains("4–6절 / 9", p2);
        Assert.Contains("이 페이지 읽은 절 0 / 3", p2);
        Assert.DoesNotContain("통독률", p2);                                // D25: 통독률은 2차
    }

    [Fact]
    public async Task 회차_기록_화면은_확인_뒤에_새_회차를_시작하고_읽은_장이_없으면_하지_않는다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/reading/rounds");
        Assert.Contains("회차 기록", html);
        Assert.Contains("아직 이번 회차에 읽은 장이 없어요", html);
        async Task<HttpResponseMessage> Post(string t) => await c.PostAsync("/jamsil-kids/reading/rounds",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = t }));

        // 읽은 장이 없으면 확인 상자도 보이지 않는다(빈 회차를 쌓지 않음 — 서버도 막음)
        Assert.DoesNotContain("새 회차를 시작할까요?", await c.GetStringAsync("/jamsil-kids/reading/rounds?ask=true"));

        using (var raw = InMemoryDb.Raw(_db))
        {
            var acc = raw.Accounts.Single(a => a.Email == "cheolsu@example.com").Id;
            raw.ReadingRounds.Add(new ReadingRound { AccountId = acc, RoundNo = 1, StartedUtc = DateTime.UtcNow });
            raw.ChapterReads.Add(new ChapterRead { AccountId = acc, RoundNo = 1, BookId = 1, Chapter = 1, ReadUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var ask = await c.GetStringAsync("/jamsil-kids/reading/rounds?ask=true");
        Assert.Contains("새 회차를 시작할까요?", ask);
        var r = await Post(TokenRx().Match(ask).Groups[1].Value);
        Assert.Equal("/jamsil-kids/reading/rounds?Started=2", r.Headers.Location!.OriginalString);
        var after = await c.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("2회차를 시작했어요", after);
        Assert.Contains("중간에 닫음", after);
    }

    [Fact]
    public async Task 통독_화면_세_개가_열리고_홈_카드가_연결된다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        Assert.Contains("href=\"/jamsil-kids/reading\"", await c.GetStringAsync("/jamsil-kids"));
        var dash = await c.GetStringAsync("/jamsil-kids/reading");
        Assert.Contains("나의 성경 통독 · <a href=\"/jamsil-kids/reading/rounds\" title=\"회차 기록\">1회차", dash);   // 회차 기록으로 가는 길
        Assert.Contains("이어 읽기", dash);
        Assert.Contains("<div class=\"t\">창세기 1장</div>", dash);                     // 옛 Bible2 배치(0071): 이어 읽기 큰 칸
        Assert.Contains("class=\"b2it on\" aria-current=\"page\">대시보드</a>", dash);   // 왼쪽 목차
        var list = await c.GetStringAsync("/jamsil-kids/reading/list?t=ot&b=1");
        Assert.Contains("율법서 · 5권", list);
        Assert.Contains("이어 읽기 1장", list);
        var read = await c.GetStringAsync("/jamsil-kids/reading/read/1/24");
        Assert.Contains("KRV 1:24:9", read);
        Assert.Contains("class=\"brd-chs\"", read);           // 장 번호 줄(ADR 0057)
        Assert.Contains("data-api=\"reading\"", read);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/jamsil-kids/reading/read/1/51")).StatusCode);
    }

    [Fact]
    public async Task 북마크_모음은_담은_말씀을_본문과_함께_모으고_정렬한다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        Assert.Contains("아직 담은 말씀이 없어요", await c.GetStringAsync("/jamsil-kids/reading/bookmarks"));
        var token = TokenRx().Match(await c.GetStringAsync("/jamsil-kids/reading/read/1/24")).Groups[1].Value;
        foreach (var url in new[] { "/api/me/bible/bookmarks/1/24/7", "/api/me/bible/bookmarks/43/3/16", "/api/me/bible/bookmarks/1/24/2" })
        {
            var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Add("RequestVerificationToken", token);
            Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(req)).StatusCode);
        }
        var recent = await c.GetStringAsync("/jamsil-kids/reading/bookmarks");
        Assert.Contains("3절", recent);
        Assert.Contains("KRV 43:3:16", recent);
        Assert.Contains("href=\"/jamsil-kids/reading/read/1/24?v=7\"", recent);   // 본문으로 → 그 절이 보이는 곳
        var bible = await c.GetStringAsync("/jamsil-kids/reading/bookmarks?sort=bible");
        Assert.True(bible.IndexOf("창세기 24:2", StringComparison.Ordinal) < bible.IndexOf("창세기 24:7", StringComparison.Ordinal));
        Assert.True(bible.IndexOf("창세기 24:7", StringComparison.Ordinal) < bible.IndexOf("요한복음 3:16", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 성경_검색은_결과를_보여_주고_인기_검색어에_남고_구절_주소는_본문으로_간다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids/reading/search");
        Assert.Contains("아직 찾은 말이 없어요", home);
        // 시험 본문은 "KRV 1:24:n" · "NIV 1:24:n" 이라 한글이 없다 → 영어 검색어는 NIV 에서 찾고, 보이는 번역은 고른 대로
        var res = await c.GetStringAsync("/jamsil-kids/reading/search?q=NIV%201:24");          // 처음 찾기 → 기록
        Assert.Contains("“NIV 1:24”", res);
        Assert.Contains("9절 · 개역개정", res);
        Assert.Contains("href=\"/jamsil-kids/reading/read/1/24?v=9\"", res);
        var niv = await c.GetStringAsync("/jamsil-kids/reading/search?q=NIV%201:24&tr=NIV&s=all");   // 번역만 바꿔 보기 → 기록 안 함
        Assert.Contains("<mark>NIV 1:24</mark>", niv);
        Assert.Contains("1회", await c.GetStringAsync("/jamsil-kids/reading/search"));
        var go = await c.GetAsync("/jamsil-kids/reading/search?q=" + Uri.EscapeDataString("요 3:16"));
        Assert.Equal(HttpStatusCode.Redirect, go.StatusCode);
        Assert.Equal("/jamsil-kids/reading/read/43/3?v=16", go.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task R159_참여자_현황은_공개일_때만_보이고_관리자가_통독_현황에서_끈다()
    {
        var kid = await Member("cheolsu", Roles.Participant);
        var admin = await Member("eunhye", Roles.Teacher, Roles.TenantAdmin);
        var board = await kid.GetStringAsync("/jamsil-kids/reading/participants");
        Assert.Contains("성경통독 참여자", board);
        Assert.Contains("아직 완독한 사람이 없어요", board);
        Assert.Contains("class=\"mine\"", board);                                       // 내 카드에 "나"
        Assert.Contains("href=\"/jamsil-kids/reading/participants\"", await kid.GetStringAsync("/jamsil-kids/reading"));
        Assert.NotEqual(HttpStatusCode.OK, (await kid.GetAsync("/jamsil-kids/admin/reading")).StatusCode);   // 관리자만

        var page = await admin.GetStringAsync("/jamsil-kids/admin/reading");
        Assert.Contains("href=\"/jamsil-kids/admin/reading\"", page);                   // 관리 메뉴 "통독 현황"
        Assert.Contains("aria-checked=\"true\"", page);
        var off = await admin.PostAsync("/jamsil-kids/admin/reading?handler=Public", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["on"] = "false",
        }));
        Assert.Equal(HttpStatusCode.Redirect, off.StatusCode);
        Assert.Contains("aria-checked=\"false\"", await admin.GetStringAsync("/jamsil-kids/admin/reading"));
        Assert.Contains("참여자 현황을 공개하지 않아요",await kid.GetStringAsync("/jamsil-kids/reading/participants"));
        Assert.Contains("관리자 · 교사에게만 보이는", await admin.GetStringAsync("/jamsil-kids/reading/participants"));
    }

    [Fact]
    public async Task R150_통독을_끈_부서는_통독_화면이_없다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(t => t.Id == _kids.Id).ReadingEnabled = false; raw.SaveChanges(); }
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/jamsil-kids/reading")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/jamsil-kids/reading/read/1/1")).StatusCode);
        Assert.DoesNotContain("/jamsil-kids/reading", await c.GetStringAsync("/jamsil-kids"));
    }

    [Fact]
    public async Task R160_R162_통독_본문에서_이_페이지를_다_읽으면_장이_완료되고_진도에_반영된다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/reading/read/1/24");
        var token = TokenRx().Match(html).Groups[1].Value;
        async Task<JsonElement> Send(HttpMethod m, string url)
        {
            var req = new HttpRequestMessage(m, url);
            req.Headers.Add("RequestVerificationToken", token);
            var res = await c.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        }
        Assert.False((await Send(HttpMethod.Put, "/api/me/reading/verses/1/24?from=1&to=8")).GetProperty("chapterDone").GetBoolean());
        Assert.True((await Send(HttpMethod.Put, "/api/me/reading/verses/1/24/9")).GetProperty("chapterDone").GetBoolean());
        Assert.Contains("1 / 1,189장", await c.GetStringAsync("/jamsil-kids/reading"));
        Assert.Contains("다 읽은 장", await c.GetStringAsync("/jamsil-kids/reading/read/1/24"));
        Assert.False((await Send(HttpMethod.Delete, "/api/me/reading/verses/1/24/9")).GetProperty("chapterDone").GetBoolean());
        Assert.Contains("0 / 1,189장", await c.GetStringAsync("/jamsil-kids/reading"));
        // 책 목록에서 바로 완료(R-151)
        Assert.True((await Send(HttpMethod.Put, "/api/me/reading/chapters/1/1")).GetProperty("chapterDone").GetBoolean());
        Assert.Contains("1 / 1,189장", await c.GetStringAsync("/jamsil-kids/reading"));
    }

    [Fact]
    public async Task R112_성경_읽기의_읽음과_책갈피는_PUT_DELETE_로_켜고_끈다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/read?from=word");
        var token = TokenRx().Match(html).Groups[1].Value;
        Assert.Contains("0</b>절 읽음", html);

        async Task<HttpStatusCode> Send(HttpMethod m, string url, object? body = null)
        {
            var req = new HttpRequestMessage(m, url);
            req.Headers.Add("RequestVerificationToken", token);
            if (body is not null) req.Content = JsonContent.Create(body);
            return (await c.SendAsync(req)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await Send(HttpMethod.Put, "/api/me/bible/read/1/24/1"));
        Assert.Equal(HttpStatusCode.OK, await Send(HttpMethod.Put, "/api/me/bible/read/1/24/1"));   // 멱등
        Assert.Equal(HttpStatusCode.OK, await Send(HttpMethod.Put, "/api/me/bible/bookmarks/1/24/2"));
        Assert.Equal(HttpStatusCode.NotFound, await Send(HttpMethod.Put, "/api/me/bible/read/1/24/99"));
        var after = await c.GetStringAsync("/jamsil-kids/read?from=word");
        Assert.Contains("1</b>절 읽음", after);
        Assert.Contains("aria-pressed=\"true\" aria-label=\"1절 읽음\"", after);
        Assert.Contains("aria-pressed=\"true\" aria-label=\"2절 책갈피\"", after);

        Assert.Equal(HttpStatusCode.OK, await Send(HttpMethod.Put, "/api/me/bible/read-range", new { book = 1, startChapter = 24, startVerse = 1, endChapter = 24, endVerse = 9 }));
        Assert.Contains("9</b>절 읽음", await c.GetStringAsync("/jamsil-kids/read?from=word"));
        Assert.Equal(HttpStatusCode.OK, await Send(HttpMethod.Delete, "/api/me/bible/read-range?book=1&startChapter=24&startVerse=1&endChapter=24&endVerse=9"));
        Assert.Equal(HttpStatusCode.OK, await Send(HttpMethod.Delete, "/api/me/bible/read/1/24/1"));   // 이미 꺼진 것을 다시 꺼도 OK
        Assert.Contains("0</b>절 읽음", await c.GetStringAsync("/jamsil-kids/read?from=word"));

        // 위조 방지 토큰이 없으면 거절
        var bad = new HttpRequestMessage(HttpMethod.Put, "/api/me/bible/read/1/24/1");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(bad)).StatusCode);
    }

    [Fact]
    public async Task R111_성경_읽기는_번역을_바꾸거나_모두_나란히_보여준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var niv = await c.GetStringAsync("/jamsil-kids/read?from=word&tr=NIV&unit=0");
        Assert.Contains("NIV 1:24:9", niv);          // 전체 = 9절 한 쪽
        Assert.DoesNotContain("KRV 1:24:1", niv);
        var all = await c.GetStringAsync("/jamsil-kids/read?from=word&tr=ALL&unit=1");
        Assert.Contains("KRV 1:24:1", all);
        Assert.Contains("KLB 1:24:1", all);
        Assert.Contains("NIV 1:24:1", all);
    }

    [Fact]
    public async Task R115_구절을_직접_지정해_읽을_수_있고_잘못된_범위는_안내한다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var john = await c.GetStringAsync("/jamsil-kids/read?book=43&sc=3&sv=16");
        Assert.Contains("요한복음", john);
        Assert.Contains("KRV 43:3:16", john);
        var bad = await c.GetStringAsync("/jamsil-kids/read?book=99&sc=1&sv=1");
        Assert.Contains("읽을 구절이 없어요", bad);
    }

    [Fact]
    public async Task 말씀_화면의_읽으러_가기는_성경_읽기로_연결된다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        Assert.Contains("href=\"/jamsil-kids/read?from=word\"", html);
        Assert.Contains("창세기 24장 읽으러 가기", html);
    }

    [Fact]
    public async Task 기도_화면과_암송_화면은_시안의_구성을_따른다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var prayer = await c.GetStringAsync("/jamsil-kids/prayer");
        // 이 픽스처에는 기도 · 암송 콘텐츠가 없다: 머리말 이름과 빈 상태만 본다(구성은 화면 캡처로 비교)
        Assert.Contains("오늘의 성품기도", prayer);
        Assert.Contains("이날은 기도가 없어요", prayer);
        var recite = await c.GetStringAsync("/jamsil-kids/recite");
        Assert.Contains("이번 주는 암송 말씀이 없어요", recite);
        // 선택 항목 이름은 시안 문구(RuleSet 기본값)
        Assert.Equal("나를 위해 기도했어요", global::ChallengeBible.Domain.Rules.RuleSet.Default.Item(global::ChallengeBible.Domain.Rules.MissionItem.PrayerMe).DisplayName);
    }

    [Fact]
    public async Task 말씀_화면은_이야기_접기와_읽는_시간_성경_읽기_줄을_보여준다()
    {
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        Assert.Contains("id=\"story\"", html);
        Assert.Contains("id=\"storyMore\"", html);      // 3줄을 넘길 때만 JS 가 보인다
        Assert.Contains("10초", html);                  // 읽는 시간
        Assert.Contains("읽으러 가기", html);
    }

    [Fact]
    public async Task 퀴즈_화면은_보기를_고른_뒤_답을_제출한다()
    {
        // 확정 시안: 03 정답을 골라요(라디오) → 답 제출하기, 해설은 끝난 뒤에만
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/quiz");
        Assert.Contains("정답을 골라요", html);
        Assert.Contains("type=\"radio\"", html);
        Assert.Contains("답 제출하기", html);
        Assert.Contains("id=\"explain\" hidden", html);
        Assert.Contains("기회 2번", html);
    }

    [Fact]
    public async Task 미션_홈은_옛_모양으로_남은_미션과_달력을_보여준다()
    {
        // 확정 시안 미션홈-옛모양(ADR 0069): 환영 → 인사 → 나의 기록 → 최근 7일 → 네 칸 숫자 → 미션 카드 4장 → 이달 달력 → 맨 끝 메뉴 줄
        var c = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("김철수님, 안녕하세요", html);
        Assert.Contains("\"거룩한 습관 만들기\" 프로그램에 오심을 환영해요.", html);
        Assert.Contains("올해 총 포인트", html);
        Assert.Contains("네 가지 다 한 날", html);
        Assert.DoesNotContain("오늘 네 가지 모두 했어요", html);        // 다 했을 때만 도장 칸
        Assert.Contains("class=\"hM w next\"", html);                    // 말씀이 다음 할 것(파란 테두리), 기도 · 암송은 콘텐츠 없음
        Assert.Contains("class=\"hM q\"", html);
        Assert.Contains("class=\"hM pr none\"", html);
        // 날짜를 눌러 그날 미션을 본다(오늘 이전 날짜는 ?date= 링크), 최근 7일 막대도 같은 링크
        Assert.Contains("?date=2027-01-04", html);
        Assert.Contains("class=\"hCalG\"", html);
        Assert.Contains("class=\"hTab\"", html);                          // 아래 메뉴 줄은 페이지 안(따라다니지 않음)
        Assert.DoesNotContain("우리 가족", html);                        // 더 보기 · 우리 가족 · 선물은 뺌
    }
}
