using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>책 개요 카드(R-116, ADR 0062, 확정 시안 통독-본문읽기-책개요). 오늘 = 2027-01-05 (서울).</summary>
public partial class BookOverviewTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public BookOverviewTests(WebApplicationFactory<Program> app)
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

    private void Seed(BibleBookOverview o)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.BibleBookOverviews.Add(o);
        raw.SaveChanges();
    }

    private static BibleBookOverview Genesis() => new()
    {
        BookId = 1, Author = "모세", WhenWritten = "B.C. 1446년 ~ 1406년경", Summary = "세상의 시작 이야기예요.\n둘째 줄", Purpose = "약속을 알려 주려고",
        DetailAuthor = "모세", AuthorDescription = "이스라엘의 지도자", WrittenFrom = "B.C. 1446", WrittenTo = "B.C. 1406", AlternativeDate = "B.C. 1290년경",
        WrittenPlace = "시내 광야", Keywords = "창조, 언약 · 족장", ContentSummary = "1 ~ 11장은 세상의 시작",
        VideoUrl1 = "https://youtu.be/abc", VideoUrl2 = "javascript:alert(1)",
    };

    [Fact]
    public async Task R116_그_책_1장에서는_개요가_펼쳐져_간략본_상세본이_보인다()
    {
        Seed(Genesis());
        var html = await (await Kid()).GetStringAsync("/jamsil-kids/reading/read/1/1");
        Assert.Contains("<details class=\"bov\" open aria-label=\"창세기 개요\">", html);
        Assert.Contains("<dt>저자</dt>", html);
        Assert.Contains("<dd>모세</dd>", html);
        Assert.Contains("data-p=\"d\">상세본</button>", html);
        Assert.Contains("<dd>B.C. 1446 ~ B.C. 1406(다른 설: B.C. 1290년경)</dd>", html);
        Assert.Matches("<dd class=\"kw\">\\s*<span>창조</span>\\s*<span>언약</span>\\s*<span>족장</span>", html);   // 핵심어는 칩
        Assert.DoesNotContain("관련 영상", html);                                      // 책 개요 영상은 듣기 · 영상 줄로(ADR 0063)
        Assert.DoesNotContain("javascript:alert", html);
    }

    [Fact]
    public async Task R116_다른_장에서는_접혀서_한_줄_개요만_보인다()
    {
        Seed(Genesis());
        var html = await (await Kid()).GetStringAsync("/jamsil-kids/reading/read/1/24");
        Assert.Contains("<details class=\"bov\"  aria-label=\"창세기 개요\">", html);
        Assert.Contains("<span>세상의 시작 이야기예요.</span>", html);                   // 첫 줄만
    }

    [Fact]
    public async Task R116_개요_글이_없는_책은_카드를_숨긴다()
    {
        Seed(new BibleBookOverview { BookId = 1 });
        var html = await (await Kid()).GetStringAsync("/jamsil-kids/reading/read/1/1");
        Assert.DoesNotContain("class=\"bov\"", html);
        Assert.DoesNotContain("class=\"bov\"", await (await Kid2()).GetStringAsync("/jamsil-kids/reading/read/2/1"));   // 표에 줄이 없는 책
    }

    private async Task<HttpClient> Kid2()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value, ["Email"] = "cheolsu@example.com", ["Password"] = "1234",
        }));
        return c;
    }

    [Fact]
    public async Task R116_상세본만_있으면_탭_없이_상세본을_보인다()
    {
        Seed(new BibleBookOverview { BookId = 1, ContentSummary = "요약만 있어요" });
        var html = await (await Kid()).GetStringAsync("/jamsil-kids/reading/read/1/1");
        Assert.DoesNotContain("role=\"tablist\"", html);
        Assert.Contains("<dl data-pane=\"d\" >", html);
        Assert.Contains("<dd>요약만 있어요</dd>", html);
    }

    [Fact]
    public async Task R116_말씀_성경_읽기에도_같은_카드가_보인다()
    {
        Seed(Genesis());
        var html = await (await Kid()).GetStringAsync("/jamsil-kids/read?from=word");
        Assert.Contains("<details class=\"bov\"  aria-label=\"창세기 개요\">", html);    // 창세기 24장에서 시작 → 접힘
    }

    [Fact]
    public void 연대는_부터_까지가_같으면_하나만()
    {
        var card = BookOverviewService.Card(new BibleBookOverview { BookId = 40, WrittenFrom = "A.D. 60", WrittenTo = "A.D. 60" }, 1)!;
        Assert.Equal("A.D. 60", card.Detail.Single(r => r.Label == "기록 연대").Text);
        Assert.Equal("마태복음", card.BookName);
        Assert.Empty(card.Brief);
    }
}
