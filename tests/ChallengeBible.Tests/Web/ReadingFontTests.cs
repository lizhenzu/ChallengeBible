using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>성경통독 글자 크기(R-218, ADR 0089): 실제 페이지 + 스크립트 + CSS 를 Edge headless 에서. Edge 가 없는 환경에서는 건너뛴다.</summary>
public partial class ReadingFontTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public ReadingFontTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
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

    private const string Helpers = """
        var fails = [];
        window.addEventListener('error', function (e) { fetch('/result', { method: 'POST', body: 'script error: ' + e.message }); });
        function eq(name, got, want) { if (String(got) !== String(want)) fails.push(name + ': ' + got + ' != ' + want); }
        var q = function (s) { return document.querySelector(s); };
        var root = document.documentElement;
        var rs = getComputedStyle(root).getPropertyValue('--rs').trim();
        var stored = function (k) { return localStorage.getItem(k); };

        """;

    /// <summary>절 하나를 만들어 구분선 모양을 본다(시험 자료에 본문이 없어도 된다).</summary>
    private const string VerseLine = """
        var wrap = document.createElement('div'); wrap.className = 'brd';
        var verse = document.createElement('div'); verse.className = 'vr';
        wrap.appendChild(verse); document.body.appendChild(wrap);
        var verseLine = getComputedStyle(verse).borderBottomStyle;

        """;

    private const string Finish ="\nfetch('/result', { method: 'POST', body: fails.length ? fails.join('\\n') : 'OK' });";

    /// <summary>글자가 있는 요소마다 --rs 를 1 로 되돌려 보고, 안 줄어든 것을 찾는다(11px 이하 · 화살표 아이콘은 그대로).</summary>
    private const string AllShrink = """
        var texted = [].slice.call(document.querySelectorAll('body *')).filter(function (e) {
          return !/^(SCRIPT|STYLE)$/.test(e.tagName) && !e.classList.contains('chev')
            && [].some.call(e.childNodes, function (n) { return n.nodeType === 3 && n.textContent.trim(); });
        });
        var now = texted.map(function (e) { return parseFloat(getComputedStyle(e).fontSize); });
        var buttonNow = q('.brd-pills a').getBoundingClientRect().height;
        var numberNow = q('[data-fs="-1"]').getBoundingClientRect().height;
        root.style.setProperty('--rs', '1');
        var missed = texted.filter(function (e, i) { var b = parseFloat(getComputedStyle(e).fontSize); return Math.abs(b - now[i]) < .01 && b > 11; });
        eq('every text shrinks', missed.map(function (e) { return e.tagName + '.' + e.className + ':' + e.textContent.trim().slice(0, 12); }).join(' | '), '');
        eq('no text under 11px unless it was', texted.filter(function (e, i) { return now[i] < 11 && parseFloat(getComputedStyle(e).fontSize) > 11; }).length, 0);
        eq('pill height stays', q('.brd-pills a').getBoundingClientRect().height >= 36, true);
        eq('size button stays', q('[data-fs="-1"]').getBoundingClientRect().height, numberNow);
        root.style.removeProperty('--rs');

        """;

    private async Task<string?> Run(HttpClient c, string url, string? legacy, string? newer, string checks, params string[] css)
    {
        var html = await c.GetStringAsync(url);
        var setup = "<script>" + (legacy is null ? "" : $"localStorage.setItem('cb.fs','{legacy}');") + (newer is null ? "" : $"localStorage.setItem('cb.fs.rd','{newer}');") + "</script>";
        var page = EdgePage.Inline(html, "mission.js", Helpers + checks + Finish, css).Replace("</head>", setup + "</head>");
        return await EdgePage.RunAsync(page);
    }

    private static readonly string[] ReadingCss = ["reading.css", "reading-b2.css"];
    private const string ReadUrl = "/jamsil-kids/reading/read/1/1";

    [Fact]
    public async Task R218_통독_글자가_모두_줄고_버튼은_그대로이며_기본이_100이다()
    {
        if (!EdgePage.Available) return;
        Assert.Equal("OK", await Run(await Kid(), ReadUrl, null, null, AllShrink + VerseLine + """
            eq('no line between verses', verseLine, 'none');
            eq('rs', rs, '.85');
            eq('label', q('.fs .pct').textContent, '100%');
            eq('fs', root.style.getPropertyValue('--fs'), '1');
            eq('saved', stored('cb.fs.rd'), '5');
            eq('mission key untouched', stored('cb.fs'), null);
            q('[data-fs="-1"]').click();
            eq('smaller', q('.fs .pct').textContent, '95%');
            eq('saved smaller', stored('cb.fs.rd'), '4');
            q('[data-fs="0"]').click();
            eq('reset', q('.fs .pct').textContent, '100%');
            q('[data-fs="1"]').click();
            eq('bigger', q('.fs .pct').textContent, '110%');
            """, ReadingCss));
    }

    [Theory]
    [InlineData("2", "100%")]   // 85% 쓰던 사람: 보이는 크기 그대로
    [InlineData("0", "90%")]    // 75% → 88% 에 가까운 단계
    [InlineData("5", "100%")]   // 100% 쓰던 사람: 새 기본
    [InlineData("6", "130%")]   // 110% → 129% 에 가까운 단계
    [InlineData("10", "150%")]  // 넘치면 맨 끝
    public async Task R218_전에_고른_크기를_옮긴다(string legacy, string label)
    {
        if (!EdgePage.Available) return;
        Assert.Equal("OK", await Run(await Kid(), ReadUrl, legacy, null, $"eq('label', q('.fs .pct').textContent, '{label}');\neq('old stays', stored('cb.fs'), '{legacy}');", ReadingCss));
    }

    [Fact]
    public async Task R218_통독_값이_있으면_다시_옮기지_않는다()
    {
        if (!EdgePage.Available) return;
        Assert.Equal("OK", await Run(await Kid(), ReadUrl, "2", "7", "eq('label', q('.fs .pct').textContent, '120%');", ReadingCss));
    }

    [Fact]
    public async Task R218_말씀_미션은_크기와_저장이_그대로다()
    {
        if (!EdgePage.Available) return;
        var c = await Kid();
        Assert.DoesNotContain("reading.css", await c.GetStringAsync("/jamsil-kids/word"));
        Assert.Equal("OK", await Run(c, "/jamsil-kids/word", "2", null, VerseLine + """
            eq('mission keeps line', verseLine, 'solid');
            eq('no scale', rs, '');
            eq('label', q('.fs .pct').textContent, '85%');
            eq('saved', stored('cb.fs'), '2');
            eq('reading key untouched', stored('cb.fs.rd'), null);
            eq('body size', getComputedStyle(document.body).fontSize, '15px');
            """));
    }
}
