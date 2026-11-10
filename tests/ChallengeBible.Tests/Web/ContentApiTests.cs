using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>퀴즈 입력 API(ADR 0079, R-125).</summary>
public class ContentApiTests(WebApplicationFactory<Program> app) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Key = "test-key-0123456789-0123456789-0123456789";
    private const string Url = "/api/platform/quizzes/2027-01-05";
    private readonly string _db = InMemoryDb.NewName();

    private HttpClient Client(string? key = Key, string? actor = null)
    {
        var f = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("ContentApi:Key", key ?? "");
            b.UseSetting("ContentApi:ActorUserName", actor?.ToString() ?? "");
            b.ConfigureServices(s =>
            {
                s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
                s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            });
        });
        return f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

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

    private static readonly object Quiz = new
    {
        question = "첫 사람은 누구인가요?", background = "창세기 이야기", choices = new[] { "아담", "노아", "모세" }, correctNumber = 1, explanation = "아담이에요.",
    };

    [Fact]
    public async Task R125_키가_없거나_틀리면_넣지_못한다()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client(key: null, actor: Admin()).SendAsync(Put(Quiz))).StatusCode);   // 서버에 키 없음 = 꺼짐
        Assert.Equal(HttpStatusCode.NotFound, (await Client(key: "짧은키", actor: Admin()).SendAsync(Put(Quiz, "짧은키"))).StatusCode);
        var c = Client(actor: Admin());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Put(Quiz, key: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Put(Quiz, key: "틀린 키"))).StatusCode);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.DailyQuizzes.IgnoreQueryFilters());
    }

    [Fact]
    public async Task R125_R124_객관식을_넣고_같은_날을_다시_넣으면_새_개정이다()
    {
        var actor = Admin();
        var c = Client(actor: actor);
        var res = await c.SendAsync(Put(Quiz));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2027-01-05", json.GetProperty("date").GetString());
        Assert.Equal(ContentStatus.Published, json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("created").GetBoolean());

        using (var raw = InMemoryDb.Raw(_db))
        {
            var q = raw.DailyQuizzes.IgnoreQueryFilters().Include(x => x.Choices).Single();
            Assert.Null(q.TenantId);
            Assert.Equal(actor, q.CreatedByUserName);
            Assert.Equal(new DateOnly(2027, 1, 5), q.ExposeDate);
            Assert.Equal("창세기 이야기", q.Background);
            Assert.Equal(["아담", "노아", "모세"], q.Choices.OrderBy(x => x.SortOrder).Select(x => x.Text));
            Assert.Equal("아담", q.Choices.Single(x => x.Id == q.CorrectChoiceId).Text);
        }

        res = await c.SendAsync(Put(new { question = "둘째 문제", choices = new[] { "가", "나" }, correctNumber = 2 }));
        Assert.False((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetBoolean());
        using (var raw = InMemoryDb.Raw(_db))
        {
            var rows = raw.DailyQuizzes.IgnoreQueryFilters().OrderBy(x => x.Revision).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(ContentStatus.Archived, rows[0].Status);
            Assert.Equal("둘째 문제", rows[1].Question);
            Assert.True(rows[1].IsCurrent);
        }

        // 공개된 날은 초안으로 되돌리지 못한다
        res = await c.SendAsync(Put(new { question = "초안", choices = new[] { "가", "나" }, correctNumber = 1, publish = false }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task R125_주관식과_초안도_넣는다()
    {
        var c = Client(actor: Admin());
        var res = await c.SendAsync(Put(new { question = "첫 사람은?", format = "Text", acceptedAnswers = new[] { "아담", "아 담" }, publish = false }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(ContentStatus.Draft, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        using var raw = InMemoryDb.Raw(_db);
        var q = raw.DailyQuizzes.IgnoreQueryFilters().Include(x => x.AcceptedAnswers).Single();
        Assert.Equal(QuizFormat.Text, q.Format);
        Assert.Null(q.CorrectChoiceId);
        Assert.NotEmpty(q.AcceptedAnswers);
    }

    public static TheoryData<object, string> Bad => new()
    {
        { new { choices = new[] { "가", "나" }, correctNumber = 1 }, "question" },
        { new { question = new string('가', ContentApi.MaxQuestion + 1), choices = new[] { "가", "나" }, correctNumber = 1 }, "question" },
        { new { question = "문제", choices = new[] { "가" }, correctNumber = 1 }, "choices" },
        { new { question = "문제", choices = new[] { "가", "나", "다", "라", "마" }, correctNumber = 1 }, "choices" },
        { new { question = "문제", choices = new[] { "가", " " }, correctNumber = 1 }, "choices" },
        { new { question = "문제", choices = new[] { "가", "나" }, correctNumber = 0 }, "correctNumber" },
        { new { question = "문제", choices = new[] { "가", "나" }, correctNumber = 3 }, "correctNumber" },
        { new { question = "문제", format = "Text" }, "acceptedAnswers" },
        { new { question = "문제", format = "Essay" }, "format" },
    };

    [Theory]
    [MemberData(nameof(Bad))]
    public async Task R125_틀린_값은_칸별로_알려_준다(object body, string field)
    {
        var res = await Client(actor: Admin()).SendAsync(Put(body));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("/api/platform/quizzes/2027-13-01")]
    [InlineData("/api/platform/quizzes/01-05-2027")]
    public async Task R125_날짜_형식이_틀리면_400(string url) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Client(actor: Admin()).SendAsync(Put(Quiz, url: url))).StatusCode);

    [Fact]
    public async Task R125_JSON_이_아니면_400()
    {
        var m = new HttpRequestMessage(HttpMethod.Put, Url) { Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json") };
        m.Headers.Add(ContentApi.KeyHeader, Key);
        var res = await Client(actor: Admin()).SendAsync(m);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("BadJson", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // ── 말씀 입력(ADR 0080, R-126) ──

    private const string WordUrl = "/api/platform/words/2027-01-05";

    private static readonly object Word = new
    {
        title = "믿음의 시작", book = 1, startChapter = 12, startVerse = 1, endChapter = 12, endVerse = 9, summary = "아브람이 길을 떠나요.", commentary = "해설",
    };

    [Fact]
    public async Task R126_말씀을_넣고_같은_날을_다시_넣으면_새_개정이다()
    {
        var actor = Admin();
        var c = Client(actor: actor);
        var res = await c.SendAsync(Put(Word, url: WordUrl));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2027-01-05", json.GetProperty("date").GetString());
        Assert.Equal(ContentStatus.Published, json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("created").GetBoolean());

        using (var raw = InMemoryDb.Raw(_db))
        {
            var w = raw.DailyWords.IgnoreQueryFilters().Single();
            Assert.Null(w.TenantId);
            Assert.Equal(actor, w.CreatedByUserName);
            Assert.Equal(new DateOnly(2027, 1, 5), w.ExposeDate);
            Assert.Equal("믿음의 시작", w.Title);
            Assert.Equal("아브람이 길을 떠나요.", w.Summary);
            Assert.Equal("해설", w.Commentary);
            Assert.Equal((1, 12, 1, 12, 9), ((int)w.BookId, (int)w.StartChapter, (int)w.StartVerse, (int)w.EndChapter, (int)w.EndVerse));
        }

        res = await c.SendAsync(Put(new { title = "둘째", book = 40, startChapter = 5, startVerse = 1, endChapter = 5, endVerse = 12, summary = "산상수훈" }, url: WordUrl));
        Assert.False((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetBoolean());
        using (var raw = InMemoryDb.Raw(_db))
        {
            var rows = raw.DailyWords.IgnoreQueryFilters().OrderBy(x => x.Revision).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(ContentStatus.Archived, rows[0].Status);
            Assert.True(rows[1].IsCurrent);
            Assert.Equal("둘째", rows[1].Title);
        }

        res = await c.SendAsync(Put(new { title = "초안", book = 1, startChapter = 1, startVerse = 1, endChapter = 1, endVerse = 2, summary = "요약", publish = false }, url: WordUrl));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);   // 공개된 날은 초안으로 되돌리지 못한다
        Assert.True((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task R126_초안으로도_넣는다()
    {
        var res = await Client(actor: Admin()).SendAsync(Put(new { title = "제목", book = 1, startChapter = 1, startVerse = 1, endChapter = 1, endVerse = 2, summary = "요약", publish = false }, url: WordUrl));
        Assert.Equal(ContentStatus.Draft, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    public static TheoryData<object, string> BadWord => new()
    {
        { new { book = 1, startChapter = 1, startVerse = 1, endChapter = 1, endVerse = 2, summary = "요약" }, "title" },
        { new { title = new string('가', ContentApi.MaxWordTitle + 1), book = 1, startChapter = 1, startVerse = 1, endChapter = 1, endVerse = 2, summary = "요약" }, "title" },
        { new { title = "제목", book = 1, startChapter = 1, startVerse = 1, endChapter = 1, endVerse = 2 }, "summary" },
        { new { title = "제목", startChapter = 1, startVerse = 1, endChapter = 1, endVerse = 2, summary = "요약" }, "book" },
        { new { title = "제목", book = 67, startChapter = 1, startVerse = 1, endChapter = 1, endVerse = 2, summary = "요약" }, "book" },
        { new { title = "제목", book = 300, startChapter = 1, startVerse = 1, endChapter = 1, endVerse = 2, summary = "요약" }, "book" },
        { new { title = "제목", book = 1, startChapter = 0, startVerse = 1, endChapter = 1, endVerse = 2, summary = "요약" }, "startChapter" },
        { new { title = "제목", book = 1, startChapter = 1, startVerse = 1, endChapter = 70000, endVerse = 2, summary = "요약" }, "endChapter" },
        { new { title = "제목", book = 1, startChapter = 1, startVerse = 177, endChapter = 1, endVerse = 2, summary = "요약" }, "startVerse" },
        { new { title = "제목", book = 1, startChapter = 1, startVerse = 1, endChapter = 1, summary = "요약" }, "endVerse" },
        { new { title = "제목", book = 1, startChapter = 51, startVerse = 1, endChapter = 51, endVerse = 2, summary = "요약" }, "range" },   // 창세기는 50장
        { new { title = "제목", book = 1, startChapter = 2, startVerse = 1, endChapter = 1, endVerse = 2, summary = "요약" }, "range" },     // 끝이 시작보다 앞
    };

    [Theory]
    [MemberData(nameof(BadWord))]
    public async Task R126_틀린_값은_칸별로_알려_주고_넣지_않는다(object body, string field)
    {
        var res = await Client(actor: Admin()).SendAsync(Put(body, url: WordUrl));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty(field, out _));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.DailyWords.IgnoreQueryFilters());
    }

    [Fact]
    public async Task R126_키와_날짜와_계정은_퀴즈와_똑같이_막는다()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client(key: null, actor: Admin()).SendAsync(Put(Word, url: WordUrl))).StatusCode);
        var c = Client(actor: Admin());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Put(Word, key: "틀린 키", url: WordUrl))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Put(Word, url: "/api/platform/words/2027-13-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client(actor: Admin(platform: false)).SendAsync(Put(Word, url: WordUrl))).StatusCode);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.DailyWords.IgnoreQueryFilters());
    }

    // ── 관리 화면 "퀴즈 넣기"(키 설정 없이) ──

    private async Task<(HttpClient C, string Id)> SignUp(string login, bool platform)
    {
        var c = Client(key: null);
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(await c.GetStringAsync("/account/signup")),
            ["DisplayName"] = "운영자", ["Email"] = login + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        string id;
        using (var raw = InMemoryDb.Raw(_db))
        {
            var a = raw.Users.Single(x => x.Email == login + "@example.com");
            if (platform) InMemoryDb.AddRole(_db, a.Id, IdentityRoles.SuperAdmin);
            raw.SaveChanges();
            id = a.UserName!;
        }
        var token = Token(await c.GetStringAsync("/account/login"));   // 관리자 표시는 로그인할 때 읽는다
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Email"] = login + "@example.com", ["Password"] = "1234",
        }));
        return (c, id);
    }

    private static string Token(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task<HttpResponseMessage> PostScreen(HttpClient c, string date, string json) =>
        await c.PostAsync("/platform/quiz-input", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(await c.GetStringAsync("/platform/quiz-input")), ["Date"] = date, ["Json"] = json,
        }));

    private const string SampleJson = """{"question":"첫 사람은?","choices":["아담","노아"],"correctNumber":1}""";

    [Fact]
    public async Task R125_화면은_플랫폼_관리자만_연다()
    {
        Assert.NotEqual(HttpStatusCode.OK, (await Client(key: null).GetAsync("/platform/quiz-input")).StatusCode);   // 로그인 전
        var (member, _) = await SignUp("kid", platform: false);
        Assert.NotEqual(HttpStatusCode.OK, (await member.GetAsync("/platform/quiz-input")).StatusCode);
        var (op, _) = await SignUp("op", platform: true);
        var html = await op.GetStringAsync("/platform/quiz-input");
        Assert.Contains("href=\"/platform/quiz-input\" aria-current=\"page\"", html);
        Assert.Contains("correctNumber", html);   // 예제 자리 글자
    }

    [Fact]
    public async Task R125_화면에서_JSON_을_넣으면_저장되고_다시_넣으면_바뀐다()
    {
        var (op, id) = await SignUp("op", platform: true);
        var res = await PostScreen(op, "2027-01-05", SampleJson);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Contains("2027-01-05 퀴즈를 새로 넣었어요 · 공개", await op.GetStringAsync(res.Headers.Location!.OriginalString));
        using (var raw = InMemoryDb.Raw(_db))
        {
            var q = raw.DailyQuizzes.IgnoreQueryFilters().Single();
            Assert.Equal(id, q.CreatedByUserName);   // 만든 사람 = 로그인한 관리자
            Assert.Equal(new DateOnly(2027, 1, 5), q.ExposeDate);
        }
        res = await PostScreen(op, "2027-01-05", SampleJson);
        Assert.Contains("2027-01-05 퀴즈를 바꿨어요", await op.GetStringAsync(res.Headers.Location!.OriginalString));
    }

    [Theory]
    [InlineData("2027-01-05", """{"question":"문제","choices":["가","나"],"correctNumber":3}""", "correctNumber: ")]
    [InlineData("2027-01-05", "{ not json", "JSON 모양이 틀렸어요")]
    [InlineData("", SampleJson, "날짜를 골라 주세요")]
    public async Task R125_화면에서_틀리면_안내하고_쓴_JSON_은_남는다(string date, string json, string message)
    {
        var (op, _) = await SignUp("op", platform: true);
        var res = await PostScreen(op, date, json);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = System.Net.WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());
        Assert.Contains(message, html);
        Assert.Contains(">" + json + "</textarea>", html);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.DailyQuizzes.IgnoreQueryFilters());
    }

    [Fact]
    public async Task R125_넣는_계정이_플랫폼_관리자가_아니면_503()
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client(actor: Admin(platform: false)).SendAsync(Put(Quiz))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client().SendAsync(Put(Quiz))).StatusCode);
    }
}
