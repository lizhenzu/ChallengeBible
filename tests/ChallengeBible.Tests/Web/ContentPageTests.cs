using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>공용 콘텐츠 화면(확정 시안 platform-content, ADR 0088): 목록 자료와 실제 화면(Edge headless). 저장 규칙은 ContentAdminServiceTests.</summary>
public partial class ContentPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public ContentPageTests(WebApplicationFactory<Program> app) => _app = app.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
        s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        s.RemoveAll<TimeProvider>();
        s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
    }));

    private static DailyWord W(int day, string status = ContentStatus.Published, string title = "제목") => new()
    {
        ExposeDate = new DateOnly(2027, 1, day), BookId = 43, StartChapter = 3, StartVerse = 1, EndChapter = 3, EndVerse = 16,
        Title = title, Summary = "이야기", Status = status,
    };

    private void Seed(params DailyWord[] words)
    {
        using var raw = InMemoryDb.Raw(_db);
        foreach (var w in words) { w.CreatedByUserName = raw.Users.First().Id; raw.DailyWords.Add(w); }
        raw.SaveChanges();
    }

    [Fact]
    public async Task 목록에_성경_범위가_함께_나온다()
    {
        var op = InMemoryDb.SeedAccount(_db, "operator", "운영자");
        using (var raw = InMemoryDb.Raw(_db)) { InMemoryDb.AddRole(_db, raw.Users.Single().Id, IdentityRoles.SuperAdmin); raw.SaveChanges(); }
        Seed(W(5));
        var svc = new ContentAdminService(new InMemorySystemDb(_db), FixedTime.SeoulMorning());
        var month = await svc.MonthAsync(op.UserName!, ContentKind.Word, 2027, 1);
        Assert.Equal("요한복음 3장 1~16절", month[4].Sub);
        Assert.Null(month[3].Sub);
        Assert.All(await svc.MonthAsync(op.UserName!, ContentKind.Quiz, 2027, 1), d => Assert.Null(d.Sub));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Operator()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "운영자", ["Email"] = "operator@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using (var raw = InMemoryDb.Raw(_db)) { InMemoryDb.AddRole(_db, raw.Users.Single(a => a.Email == "operator@example.com").Id, IdentityRoles.SuperAdmin); raw.SaveChanges(); }
        var lh = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(lh).Groups[1].Value, ["Email"] = "operator@example.com", ["Password"] = "1234",
        }));
        return c;
    }

    private async Task Run(HttpClient op, string url, string checks)
    {
        var res = await op.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("OK", await EdgePage.RunAsync(EdgePage.Inline(await res.Content.ReadAsStringAsync(), "content-list.js", Helpers + checks + Finish)));
    }

    private const string Helpers = """
        var fails = [];
        window.addEventListener('error', function (e) { fetch('/result', { method: 'POST', body: 'script error: ' + e.message }); });
        function eq(name, got, want) { if (String(got) !== String(want)) fails.push(name + ': ' + got + ' != ' + want); }
        var q = function (s) { return document.querySelector(s); };
        var qa = function (s) { return Array.prototype.slice.call(document.querySelectorAll(s)); };
        var shown = function (el) { return !!el && el.getClientRects().length > 0; };
        // 휴대폰 폭(360px)에서 내용이 오른쪽으로 넘치지 않는지 본다
        document.documentElement.style.width = '360px';
        var overflow = function () { return qa('.cp *').filter(function (e) { return shown(e) && e.getBoundingClientRect().right > 361; }).length; };

        """;

    private const string Finish = "\nfetch('/result', { method: 'POST', body: fails.length ? fails.join('\\n') : 'OK' });";

    [Fact]
    public async Task 휴대폰_목록은_편집_칸_없이_나오고_거르기와_초안_고르기가_된다()
    {
        if (!EdgePage.Available) return;
        var op = await Operator();
        Seed(W(1), W(2, ContentStatus.Draft), W(3, ContentStatus.Draft), W(5, title: "<b>태그</b>"));
        await Run(op, "/platform/content/word?month=2027-01", """
            eq('edit hidden', shown(q('.cp-edit')), false);
            eq('list shown', shown(q('.cp-rows')), true);
            eq('rows', qa('.cp-rows li').length, 31);
            eq('overflow', overflow(), 0);
            eq('list not capped', getComputedStyle(q('.cp-rows')).maxHeight, 'none');
            eq('checkboxes only on drafts', qa('.cp-cb input').length, 2);
            eq('row checkbox', !!q('#d-2027-01-02 .cp-cb input'), true);
            eq('published no checkbox', !!q('#d-2027-01-01 .cp-cb input'), false);
            eq('range', q('#d-2027-01-01 .t small').textContent, '요한복음 3장 1~16절');
            eq('title is text', q('#d-2027-01-05 .tx').textContent, '<b>태그</b>');
            eq('today', !!q('#d-2027-01-05 .cp-today'), true);
            eq('sunday', q('#d-2027-01-03 .d').classList.contains('sun'), true);
            eq('empty text', q('#d-2027-01-04 .tx').textContent, '비어 있어요 · 눌러서 입력');
            eq('touch height', q('.cp-rows a').getBoundingClientRect().height >= 44, true);

            var bulk = q('#cpBulk');
            eq('bulk hidden', shown(bulk), false);
            q('#d-2027-01-02 .cp-cb input').click();
            eq('bulk shown', shown(bulk), true);
            eq('bulk count', bulk.textContent.trim(), '고른 초안 1개 공개');
            q('#d-2027-01-03 .cp-cb input').click();
            eq('bulk count 2', bulk.textContent.trim(), '고른 초안 2개 공개');

            q('.cp-chips button[data-f="Draft"]').click();
            eq('draft rows', qa('.cp-rows li').filter(shown).length, 2);
            eq('pressed', q('.cp-chips button[data-f="Draft"]').getAttribute('aria-pressed'), 'true');
            q('.cp-chips button[data-f="None"]').click();
            eq('none rows', qa('.cp-rows li').filter(shown).length, 27);
            q('.cp-chips button[data-f=""]').click();
            eq('all rows', qa('.cp-rows li').filter(shown).length, 31);
            """);
    }

    [Fact]
    public async Task 휴대폰_말씀_편집은_목록_없이_나오고_저장_버튼과_앞뒤_날짜가_있다()
    {
        if (!EdgePage.Available) return;
        var op = await Operator();
        Seed(W(5));
        await Run(op, "/platform/content/word?month=2027-01&date=2027-01-31", """
            eq('list hidden', shown(q('.cp-rows')), false);
            eq('head hidden', shown(q('.cp-tabs')), false);
            eq('edit shown', shown(q('.cp-edit')), true);
            eq('overflow', overflow(), 0);
            eq('dock sticky', getComputedStyle(q('.cp-form .cp-dock')).position, 'sticky');
            eq('none buttons', qa('.cp-form .cp-dock button').length, 2);
            eq('back', q('.cp-ehead .back').getAttribute('href'), '/platform/content/word?month=2027-01#d-2027-01-31');
            var arrows = qa('.cp-ehead a.ar');
            eq('prev', arrows[0].getAttribute('href'), '/platform/content/word?month=2027-01&date=2027-01-30');
            eq('next crosses month', arrows[1].getAttribute('href'), '/platform/content/word?month=2027-02&date=2027-02-01');
            q('[data-cp-r="sc"]').value = 3; q('[data-cp-r="sv"]').value = 1; q('[data-cp-r="ec"]').value = 3; q('[data-cp-r="ev"]').value = 16;
            q('#bk').value = '43';
            q('[data-cp-r="ev"]').dispatchEvent(new Event('input'));
            eq('preview', q('[data-cp-prev]').textContent, '요한복음 3장 1~16절');
            """);
    }

    [Fact]
    public async Task 공개된_말씀은_고친_내용_공개_버튼_하나다()
    {
        if (!EdgePage.Available) return;
        var op = await Operator();
        Seed(W(5));
        await Run(op, "/platform/content/word?month=2027-01&date=2027-01-05", """
            eq('one button', qa('.cp-form .cp-dock button').length, 1);
            eq('label', q('.cp-form .cp-dock button').textContent, '고친 내용 공개');
            eq('preview', q('[data-cp-prev]').textContent, '요한복음 3장 1~16절');
            """);
    }

    [Fact]
    public async Task 퀴즈_편집은_답_형식에_맞는_칸만_보인다()
    {
        if (!EdgePage.Available) return;
        var op = await Operator();
        await Run(op, "/platform/content/quiz?month=2027-01&date=2027-01-05", """
            var choice = q('[data-cp-for="Choice"]'), text = q('[data-cp-for="Text"]');
            eq('choice shown', shown(choice), true);
            eq('text hidden', shown(text), false);
            eq('overflow', overflow(), 0);
            eq('four choices', qa('.cp-opt').length, 4);
            q('input[data-cp-format][value="Text"]').click();
            eq('choice hidden', shown(choice), false);
            eq('text shown', shown(text), true);
            q('input[data-cp-format][value="Choice"]').click();
            q('.cp-opt input[type=radio]').click();
            eq('picked green', getComputedStyle(q('.cp-opt .in')).backgroundColor !== getComputedStyle(qa('.cp-opt .in')[1]).backgroundColor, true);
            eq('hint label', q('label[for="ex"]').textContent.indexOf('힌트') === 0, true);
            """);
    }

    [Fact]
    public async Task 기도_번호_목록과_앞뒤_번호가_맞다()
    {
        if (!EdgePage.Available) return;
        var op = await Operator();
        await Run(op, "/platform/content/prayer?no=1", """
            eq('list hidden', shown(q('.cp-rows')), false);
            eq('rows', qa('.cp-rows li').length, 31);
            eq('overflow', overflow(), 0);
            eq('first has no prev', !!q('.cp-ehead .ar.off'), true);
            eq('next', q('.cp-ehead a.ar').getAttribute('href'), '/platform/content/prayer?no=2');
            eq('back', q('.cp-ehead .back').getAttribute('href'), '/platform/content/prayer#n-1');
            """);
    }
}
