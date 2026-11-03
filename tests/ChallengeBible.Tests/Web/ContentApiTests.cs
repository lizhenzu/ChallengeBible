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

    private HttpClient Client(string? key = Key, int? actor = null)
    {
        var f = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("ContentApi:Key", key ?? "");
            b.UseSetting("ContentApi:ActorAccountId", actor?.ToString() ?? "");
            b.ConfigureServices(s =>
            {
                s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
                s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            });
        });
        return f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private int Admin(bool platform = true)
    {
        var a = InMemoryDb.SeedAccount(_db, $"op{Guid.NewGuid():N}@example.com", "운영자");
        using var raw = InMemoryDb.Raw(_db);
        raw.Accounts.Single(x => x.Id == a.Id).IsPlatformAdmin = platform;
        raw.SaveChanges();
        return a.Id;
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
            Assert.Equal(actor, q.CreatedByAccountId);
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

    [Fact]
    public async Task R125_넣는_계정이_플랫폼_관리자가_아니면_503()
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client(actor: Admin(platform: false)).SendAsync(Put(Quiz))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client().SendAsync(Put(Quiz))).StatusCode);
    }
}
