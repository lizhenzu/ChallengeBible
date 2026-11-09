using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Pages.Platform;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>말씀 입력 점검(R-216, 확정 시안 word-check): 판정 함수 · 한 달 조회 · 실제 화면(Edge headless).</summary>
public partial class WordCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Day5 = new(2027, 1, 5);

    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public WordCheckTests(WebApplicationFactory<Program> app) => _app = app.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
        s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        s.RemoveAll<TimeProvider>();
        s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
    }));

    private static DailyWord W(short sc = 3, short sv = 1, short ec = 3, short ev = 16, byte book = 43, string title = "제목", string summary = "이야기") => new()
    {
        ExposeDate = Day5, BookId = book, StartChapter = sc, StartVerse = sv, EndChapter = ec, EndVerse = ev,
        Title = title, Summary = summary, Status = ContentStatus.Published,
    };

    private static DailyWordClip C(short chapter, int? start = 75, int? end = 206, byte seq = 0, string? video = null) =>
        new() { WordDate = Day5, Chapter = chapter, StartSec = start, EndSec = end, Seq = seq, YoutubeVideoId = video };

    private static IReadOnlyList<string> Missing(DailyWord w, params DailyWordClip[] clips) => WordCheck.Day(w, clips).Missing;

    [Fact]
    public void R216_네_가지가_있으면_빠진_곳이_없다() => Assert.Empty(Missing(W(), C(3)));

    [Fact]
    public void R216_제목이_공백뿐이면_없음() => Assert.Equal(["제목"], Missing(W(title: "  \t "), C(3)));

    [Fact]
    public void R216_이야기가_공백뿐이면_없음() => Assert.Equal(["오늘 이야기"], Missing(W(summary: "\n "), C(3)));

    [Fact]
    public void R216_시작_절이_0이면_범위_없음() => Assert.Equal(["성경 범위"], Missing(W(sv: 0), C(3)));

    [Fact]
    public void R216_책이_없으면_범위_없음이고_글자도_없다()
    {
        var day = WordCheck.Day(W(book: 0), [C(3)]);
        Assert.Equal(["성경 범위"], day.Missing);
        Assert.Null(day.Ref);
    }

    [Fact]
    public void R216_종료_시점만_있어도_입력됨() => Assert.Empty(Missing(W(), C(3, start: null, end: 200)));

    [Fact]
    public void R216_30초만_지정해도_입력됨() => Assert.Empty(Missing(W(), C(3, start: 30, end: null)));

    [Fact]
    public void R216_시점이_0이나_비어_있으면_영상이_있어도_없음() =>
        Assert.Equal(["영상 구간"], Missing(W(), C(3, start: 0, end: null, video: "abcdefghijk")));

    [Fact]
    public void R216_여러_장이면_어느_장이든_시점이_있으면_입력됨() =>
        Assert.Empty(Missing(W(sc: 3, ec: 5), C(3, null, null, 0), C(4, null, null, 1), C(5, 10, null, 2)));

    [Fact]
    public void R216_말씀_장_범위_밖_구간은_세지_않는다() => Assert.Equal(["영상 구간"], Missing(W(), C(4)));

    [Fact]
    public void R216_긴_범위의_뒤쪽_장_구간도_인정한다() => Assert.Empty(Missing(W(sc: 1, ec: 40), C(38)));

    [Fact]
    public void R216_전부_비면_네_가지가_정해진_순서로_나온다() =>
        Assert.Equal(["성경 범위", "제목", "오늘 이야기", "영상 구간"], Missing(W(sc: 0, sv: 0, ec: 0, ev: 0, book: 0, title: "", summary: "")));

    [Fact]
    public void R216_구간_글은_재생_차례대로_나오고_시편은_편으로_쓴다()
    {
        var day = WordCheck.Day(W(sc: 1, ec: 2, book: 19), [C(2, 10, 20, 1), C(1, null, null, 0)]);
        Assert.Equal([new WordCheckClip("1편", "전체"), new WordCheckClip("2편", "0:10 ~ 0:20")], day.Clips);
    }

    private ContentAdminService Svc => new(new InMemorySystemDb(_db), FixedTime.SeoulMorning());

    private int SeedOperator()
    {
        var op = InMemoryDb.SeedAccount(_db, "operator", "운영자");
        using var raw = InMemoryDb.Raw(_db);
        raw.Accounts.Single(a => a.Id == op.Id).IsPlatformAdmin = true;
        raw.SaveChanges();
        return op.Id;
    }

    private void Seed(params DailyWord[] words)
    {
        using var raw = InMemoryDb.Raw(_db);
        foreach (var w in words) { w.CreatedByUserName = raw.Accounts.First().Id; raw.DailyWords.Add(w); }
        raw.SaveChanges();
    }

    private static DailyWord On(int day, string title = "제목", string status = ContentStatus.Published, bool current = true, int? tenant = null, string summary = "이야기")
    {
        var w = W(title: title, summary: summary);
        (w.ExposeDate, w.Status, w.IsCurrent, w.TenantId) = (new DateOnly(2027, 1, day), status, current, tenant);
        return w;
    }

    [Fact]
    public async Task R216_한_달은_현재_개정의_공용_말씀만_읽는다()
    {
        var op = SeedOperator();
        Seed(On(5, "옛 개정", current: false), On(5, "새 개정"), On(6, "부서 덮어쓰기", tenant: 7), new[] { On(1) }[0]);
        var old = On(1); old.ExposeDate = new DateOnly(2026, 12, 31);
        Seed(old);

        var days = await Svc.WordCheckMonthAsync(op, new DateOnly(2027, 1, 1));
        Assert.Equal(["2027-01-01", "2027-01-05"], days.Select(d => d.Date));
        Assert.Equal("새 개정", days[1].Title);
    }

    [Fact]
    public async Task R216_영상_구간은_같은_날짜만_판정에_쓴다()
    {
        var op = SeedOperator();
        Seed(On(5), On(6));
        using (var raw = InMemoryDb.Raw(_db)) { raw.DailyWordClips.Add(C(3)); raw.SaveChanges(); }
        var days = await Svc.WordCheckMonthAsync(op, new DateOnly(2027, 1, 1));
        Assert.Empty(days[0].Missing);
        Assert.Equal(["영상 구간"], days[1].Missing);
    }

    [Fact]
    public async Task R216_플랫폼_관리자가_아니면_못_본다()
    {
        var who = InMemoryDb.SeedAccount(_db, "kid", "아이");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.WordCheckMonthAsync(who.Id, new DateOnly(2027, 1, 1)));
    }

    [Theory]
    [InlineData("2026-11", true)]
    [InlineData("2026-13", false)]
    [InlineData("abc", false)]
    [InlineData("1000-01", false)]
    [InlineData(null, false)]
    public void R216_달_값이_틀리면_이번_달로_돌아간다(string? value, bool ok) => Assert.Equal(ok, ContentCheckModel.ParseMonth(value) is not null);

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
        using (var raw = InMemoryDb.Raw(_db)) { raw.Accounts.Single(a => a.Email == "operator@example.com").IsPlatformAdmin = true; raw.SaveChanges(); }
        var lh = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(lh).Groups[1].Value, ["Email"] = "operator@example.com", ["Password"] = "1234",
        }));
        return c;
    }

    [Fact]
    public async Task R216_로그인하지_않으면_화면이_열리지_않는다()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await c.GetAsync("/platform/content/word/check");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public void R216_스크립트에_닫는_태그_글자가_없다() =>
        Assert.DoesNotContain("</script", File.ReadAllText(Path.Combine(EdgePage.WebRoot(), "js", "content-check.js")), StringComparison.OrdinalIgnoreCase);

    /// <summary>실제 페이지 HTML + 실제 스크립트 · CSS 를 Edge headless 에서 돌린다. Edge 가 없는 환경에서는 건너뛴다.</summary>
    [Fact]
    public async Task R216_화면_달력_칩_그날칸이_브라우저에서_맞게_움직인다()
    {
        if (!EdgePage.Available) return;

        var op = await Operator();
        var draft = On(4, status: ContentStatus.Draft);
        var bad = On(5, title: "<b>태그</b> & \"따옴표\"", summary: " ");
        Seed(On(1), On(2), On(3), draft, bad);
        using (var raw = InMemoryDb.Raw(_db))
        {
            foreach (var d in new[] { 1, 2, 4 })
            {
                var clip = C(3); clip.WordDate = new DateOnly(2027, 1, d);
                raw.DailyWordClips.Add(clip);
            }
            raw.SaveChanges();
        }

        var res = await op.GetAsync("/platform/content/word/check?month=2027-01&date=2027-01-05");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var page = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("&#x", Regex.Match(page, "id=\"wkData\"[^>]*>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value);

        Assert.Equal("OK", await EdgePage.RunAsync(Inline(page, BrowserChecks)));
    }

    /// <summary>실제 페이지 HTML 에 실제 스크립트 · CSS 와 시험 스크립트를 넣는다.</summary>
    private static string Inline(string page, string checks) => EdgePage.Inline(page, "content-check.js", checks);

    private static DailyQuiz Q(int day, string? background = "배경", string question = "문제", string? hint = "힌트", string format = QuizFormat.Choice, int choices = 4, bool correct = true, string status = ContentStatus.Published, bool isCurrent = true, string[]? answers = null)
    {
        var q = new DailyQuiz
        {
            Id = 1000 + day * 10 + (isCurrent ? 1 : 0), ExposeDate = new DateOnly(2027, 1, day), Background = background, Question = question, Explanation = hint,
            Format = format, Status = status, IsCurrent = isCurrent,
        };
        for (var i = 0; i < choices; i++) q.Choices.Add(new DailyQuizChoice { Id = q.Id * 10 + i, SortOrder = (byte)i, Text = $"보기{i + 1}" });
        if (correct && choices > 0) q.CorrectChoiceId = q.Choices[0].Id;
        foreach (var a in answers ?? []) q.AcceptedAnswers.Add(new DailyQuizAcceptedAnswer { NormalizedText = a });
        return q;
    }

    private static IReadOnlyList<string> QMissing(DailyQuiz q) => QuizCheck.Day(q).Missing;

    [Fact]
    public void R217_네_가지가_있으면_빠진_곳이_없다() => Assert.Empty(QMissing(Q(1)));

    [Fact]
    public void R217_배경이_공백뿐이면_없음() => Assert.Equal(["배경"], QMissing(Q(1, background: "  ")));

    [Fact]
    public void R217_문제가_공백뿐이면_없음() => Assert.Equal(["문제"], QMissing(Q(1, question: " \n")));

    [Fact]
    public void R217_힌트가_비면_없음() => Assert.Equal(["힌트"], QMissing(Q(1, hint: null)));

    [Fact]
    public void R217_보기가_3개면_4지선다_없음() => Assert.Equal(["4지선다"], QMissing(Q(1, choices: 3)));

    [Fact]
    public void R217_보기_하나가_공백이면_4지선다_없음()
    {
        var q = Q(1);
        q.Choices[2].Text = "  ";
        Assert.Equal(["4지선다"], QMissing(q));
    }

    [Fact]
    public void R217_정답이_없으면_4지선다_없음() => Assert.Equal(["4지선다"], QMissing(Q(1, correct: false)));

    [Fact]
    public void R217_주관식은_맞는_답이_있으면_입력됨() => Assert.Empty(QMissing(Q(1, format: QuizFormat.Text, choices: 0, answers: ["이삭"])));

    [Fact]
    public void R217_주관식에_맞는_답이_없으면_없음() => Assert.Equal(["4지선다"], QMissing(Q(1, format: QuizFormat.Text, choices: 0)));

    [Fact]
    public void R217_전부_비면_네_가지가_정해진_순서로_나온다() =>
        Assert.Equal(["배경", "문제", "4지선다", "힌트"], QMissing(Q(1, background: null, question: "", hint: "", choices: 0)));

    [Fact]
    public void R217_보기는_정해진_차례대로_나온다()
    {
        var q = Q(1);
        q.Choices.Reverse();
        var day = QuizCheck.Day(q);
        Assert.Equal(["보기1", "보기2", "보기3", "보기4"], day.Choices.Select(c => c.Text));
        Assert.True(day.Choices[0].Correct);
    }

    private void SeedQuiz(params DailyQuiz[] quizzes)
    {
        using var raw = InMemoryDb.Raw(_db);
        foreach (var q in quizzes) { q.CreatedByUserName = raw.Accounts.First().Id; raw.DailyQuizzes.Add(q); }
        raw.SaveChanges();
    }

    [Fact]
    public async Task R217_한_달은_현재_개정의_공용_퀴즈만_읽는다()
    {
        var op = SeedOperator();
        var other = Q(6); other.TenantId = 7;
        var old = Q(1); old.ExposeDate = new DateOnly(2026, 12, 31);
        SeedQuiz(Q(5, question: "옛 개정", isCurrent: false), Q(5, question: "새 개정"), other, old);
        var days = await Svc.QuizCheckMonthAsync(op, new DateOnly(2027, 1, 1));
        Assert.Equal("2027-01-05", Assert.Single(days).Date);
        Assert.Equal("새 개정", days[0].Question);
        Assert.Equal(4, days[0].Choices.Count);
    }

    [Fact]
    public async Task R217_플랫폼_관리자가_아니면_못_본다()
    {
        var who = InMemoryDb.SeedAccount(_db, "kid", "아이");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.QuizCheckMonthAsync(who.Id, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public async Task R216_다른_종류_주소는_없는_화면이다()
    {
        var op = await Operator();
        Assert.Equal(HttpStatusCode.NotFound, (await op.GetAsync("/platform/content/prayer/check")).StatusCode);
    }

    /// <summary>실제 페이지 HTML + 실제 스크립트 · CSS 를 Edge headless 에서 돌린다. Edge 가 없는 환경에서는 건너뛴다.</summary>
    [Fact]
    public async Task R217_퀴즈_화면이_브라우저에서_맞게_움직인다()
    {
        if (!EdgePage.Available) return;

        var op = await Operator();
        SeedQuiz(Q(1), Q(2, format: QuizFormat.Text, choices: 0, answers: ["이삭"]), Q(3, choices: 3, correct: false),
            Q(4, background: "", hint: null, question: "<b>태그</b> & \"따옴표\""), Q(5, format: QuizFormat.Text, choices: 0), Q(6, status: ContentStatus.Draft));

        var res = await op.GetAsync("/platform/content/quiz/check?month=2027-01&date=2027-01-04");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("OK", await EdgePage.RunAsync(Inline(await res.Content.ReadAsStringAsync(), QuizChecks)));
    }

    /// <summary>자료: 1일 객관식 완성, 2일 주관식 완성, 3일 보기 3개 · 정답 없음, 4일 배경 · 힌트 없음 + 문제에 태그, 5일 주관식 답 없음, 6일 초안, 나머지 없음.</summary>
    private const string QuizChecks = """
        (function () {
          var fails = [];
          window.addEventListener('error', function (e) { fetch('/result', { method: 'POST', body: 'script error: ' + e.message }); });
          function eq(name, got, want) { if (String(got) !== String(want)) fails.push(name + ': ' + got + ' != ' + want); }
          var q = function (s, r) { return (r || document).querySelector(s); };
          var qa = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
          var cell = function (d) { return q('.wk-cell[data-day="' + d + '"]'); };
          var chip = function (k) { return q('#wkChips button[data-k="' + k + '"]'); };
          var status = function () { return qa('.wk-st div').map(function (x) { return x.textContent; }).join('|'); };
          eq('heading', q('.wk h1').textContent, '퀴즈 입력 점검');
          qa('.wk-cell').forEach(function (c) { eq('dots ' + c.dataset.day, qa('.wk-dots i', c).length, 4); });
          eq('done', q('#wkDone').textContent, '31일 중 2일 완성');
          eq('full 1', cell(1).classList.contains('all'), true);
          eq('full text 2', cell(2).classList.contains('all'), true);
          eq('dots 3', qa('i.o', cell(3)).length, 1);
          eq('dots 4', qa('i.o', cell(4)).length, 2);
          eq('no row', qa('i.o', cell(20)).length, 4);
          eq('draft', cell(6).classList.contains('dr'), true);
          eq('aria', cell(3).getAttribute('aria-label'), '1월 3일, 빠진 곳 4지선다');
          eq('chip none', chip('퀴즈 없음').textContent, '퀴즈 없음 25');
          eq('chip choice', chip('4지선다').textContent, '4지선다 없음 2');
          eq('chip zero', chip('문제').disabled, true);

          eq('status 4', status(), '✕ 배경|✓ 문제|✓ 4지선다|✕ 힌트');
          var ask = qa('.wk-f')[1];
          eq('tag as text', q('p', ask).textContent, '<b>태그</b> & "따옴표"');
          eq('no element', qa('b', q('p', ask)).length, 0);
          eq('edit', q('.wk-go').getAttribute('href'), '/platform/content/quiz?month=2027-01&date=2027-01-04');
          eq('edit text', q('.wk-go').textContent, '퀴즈 수정');
          eq('right answer', qa('.wk-tag.ok').filter(function (t) { return t.textContent === '정답'; }).length, 1);

          cell(3).click();
          eq('short', q('.wk-day').textContent.indexOf('보기 3개') >= 0, true);
          eq('no right', q('.wk-day').textContent.indexOf('정답 없음') >= 0, true);
          cell(2).click();
          eq('text status', status(), '✓ 배경|✓ 문제|✓ 주관식 답|✓ 힌트');
          eq('text answer', q('.wk-day').textContent.indexOf('이삭') >= 0, true);
          cell(5).click();
          eq('text none', status(), '✓ 배경|✓ 문제|✕ 주관식 답|✓ 힌트');
          cell(20).click();
          eq('empty', q('.wk-empty').textContent, '이 날은 아직 퀴즈가 없어요');
          eq('empty link', q('.wk-go').textContent, '퀴즈 입력하기');

          chip('4지선다').click();
          eq('filter hit', cell(5).classList.contains('dim'), false);
          eq('filter off', cell(1).classList.contains('dim'), true);
          chip('4지선다').click();
          eq('cleared', qa('.wk-cell.dim').length, 0);

          fetch('/result', { method: 'POST', body: fails.length ? fails.join('\n') : 'OK' });
        })();
        """;

    /// <summary>자료: 1 · 2 · 3 · 4 · 5일에 말씀(3일은 영상 없음, 4일은 초안, 5일은 이야기 · 영상 없음 + 제목에 태그), 나머지는 없음.</summary>
    private const string BrowserChecks = """
        (function () {
          var fails = [];
          window.addEventListener('error', function (e) { fetch('/result', { method: 'POST', body: 'script error: ' + e.message }); });
          function eq(name, got, want) { if (String(got) !== String(want)) fails.push(name + ': ' + got + ' != ' + want); }
          var q = function (s, r) { return (r || document).querySelector(s); };
          var qa = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
          var cell = function (d) { return q('.wk-cell[data-day="' + d + '"]'); };
          var chip = function (k) { return q('#wkChips button[data-k="' + k + '"]'); };
          var cells = qa('.wk-cell');
          eq('days', cells.length, 31);
          cells.forEach(function (c) { eq('dots ' + c.dataset.day, qa('.wk-dots i', c).length, 4); });
          eq('dot size', getComputedStyle(q('.wk-cell .wk-dots i')).width, '6px');
          eq('dot gap', getComputedStyle(q('.wk-cell .wk-dots')).columnGap, '3px');
          eq('leading blanks', qa('.wk-out').length, 5);
          eq('empty dots 1', qa('i.o', cell(1)).length, 0);
          eq('empty dots 3', qa('i.o', cell(3)).length, 1);
          eq('empty dots 5', qa('i.o', cell(5)).length, 2);
          eq('no row', qa('i.o', cell(20)).length, 4);
          eq('all class', cell(1).classList.contains('all'), true);
          eq('some class', cell(3).classList.contains('some'), true);
          eq('none class', cell(20).classList.contains('none'), true);
          eq('draft', cell(4).classList.contains('dr') && cell(4).classList.contains('some'), true);
          eq('sunday', cell(3).classList.contains('sun'), true);
          eq('done', q('#wkDone').textContent, '31일 중 2일 완성');
          eq('aria', cell(3).getAttribute('aria-label'), '1월 3일, 빠진 곳 영상 구간');
          eq('aria none', cell(20).getAttribute('aria-label'), '1월 20일, 말씀 없음');
          eq('aria full', cell(1).getAttribute('aria-label'), '1월 1일, 모두 입력');
          eq('selected', cell(5).classList.contains('sel'), true);
          eq('head', q('#wkDay h2').textContent, '1월 5일 화요일');
          eq('status', qa('.wk-st div').map(function (x) { return x.textContent; }).join('|'), '✓ 성경 범위|✓ 제목|✕ 오늘 이야기|✕ 영상 구간');
          var title = qa('.wk-f')[0];
          eq('tag as text', q('p', title).textContent, '<b>태그</b> & "따옴표"');
          eq('no tag element', qa('b', q('p', title)).length, 0);
          eq('edit link', q('.wk-go').getAttribute('href'), '/platform/content/word?month=2027-01&date=2027-01-05');

          eq('zero chip disabled', chip('제목').disabled, true);
          eq('zero chip green', chip('제목').classList.contains('z'), true);
          chip('영상 구간').click();
          eq('filter hit', cell(3).classList.contains('dim'), false);
          eq('filter off', cell(1).classList.contains('dim'), true);
          eq('pressed', chip('영상 구간').getAttribute('aria-pressed'), 'true');
          chip('말씀 없음').click();
          eq('switch hit', cell(20).classList.contains('dim'), false);
          eq('switch off', cell(3).classList.contains('dim'), true);
          eq('switch unpress', chip('영상 구간').getAttribute('aria-pressed'), 'false');
          chip('말씀 없음').click();
          eq('cleared', qa('.wk-cell.dim').length, 0);
          chip('제목').click();
          eq('zero chip no filter', qa('.wk-cell.dim').length, 0);

          cell(20).click();
          eq('empty day', q('.wk-empty').textContent, '이 날은 아직 말씀이 없어요');
          eq('empty link', q('.wk-go').textContent, '말씀 입력하기');
          eq('moved', cell(5).classList.contains('sel'), false);
          cell(1).click();
          eq('full status', qa('.wk-st .y').length, 4);
          eq('clip line', qa('.wk-f').pop().querySelector('p').textContent, '3장 1:15 ~ 3:26');

          fetch('/result', { method: 'POST', body: fails.length ? fails.join('\n') : 'OK' });
        })();
        """;
}
