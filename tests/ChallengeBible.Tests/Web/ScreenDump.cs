using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 화면 확인용: 환경 변수 CB_SCREENS_DIR 이 있을 때만, 시험 데이터로 화면 HTML 을 그 폴더에 저장한다
/// (CSS 를 안에 넣어 파일로 열 수 있게). 캡처는 그 HTML 을 브라우저로 찍어서 만든다. 평소 테스트에서는 아무것도 안 한다.
/// </summary>
public partial class ScreenDump(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    [Fact]
    public async Task 화면_저장()
    {
        var dir = Environment.GetEnvironmentVariable("CB_SCREENS_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);

        var db = InMemoryDb.NewName();
        var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        var kids = InMemoryDb.SeedTenant(db, "jamsil-kids", "JSKIDS");
        var jan = Enumerable.Range(1, 5).Select(d => new DateOnly(2027, 1, d)).ToList();
        foreach (var d in jan) { ContentSeed.Word(db, d); ContentSeed.Prayer(db, d); ContentSeed.ChoiceQuiz(db, d); }
        ContentSeed.Verse(db, 1);
        ContentSeed.Bible(db);
        using (var raw = InMemoryDb.Raw(db))
        {
            var r = raw.WeeklyVerses.Single();
            r.LessonNo = 1; r.LessonSummary = "하나님은 나를 사랑하세요"; r.LessonPoint = "하나님은 세상을 너무 사랑하셔서 예수님을 보내 주셨어요."; r.LessonApply = "이번 주에 \"하나님이 나를 사랑하셔\"를 세 번 말해 봐요.";
            raw.SaveChanges();
        }

        async Task<HttpClient> SignUp(string id, string name)
        {
            var c = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var h = await c.GetStringAsync("/account/signup");
            await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = TokenRx().Match(h).Groups[1].Value, ["DisplayName"] = name, ["Email"] = id + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
            }));
            return c;
        }
        int Acc(string id) { using var raw = InMemoryDb.Raw(db); return raw.Accounts.Single(a => a.Email == id + "@example.com").Id; }

        var anon = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Save(anon, "/", "01-첫화면");
        await Save(anon, "/account/login", "02-로그인");
        await Save(anon, "/account/signup", "03-회원가입");

        var admin = await SignUp("eunhye", "이은혜");
        var kid = await SignUp("cheolsu", "김철수");
        foreach (var (id, name) in new[] { ("haneul", "정하늘"), ("seojun", "오서준") }) await SignUp(id, name);
        InMemoryDb.SeedMembership(db, kids.Id, Acc("eunhye"), MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var km = InMemoryDb.SeedMembership(db, kids.Id, Acc("cheolsu"), MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(db, kids.Id, Acc("haneul"), MembershipStatus.Requested, Roles.Participant);
        InMemoryDb.SeedMembership(db, kids.Id, Acc("seojun"), MembershipStatus.Requested, Roles.Participant, Roles.Guardian);
        using (var raw = InMemoryDb.Raw(db)) { raw.Memberships.Single(m => m.Id == km.Id).GroupLabel = "사랑반"; raw.Participants.Add(new Participant { TenantId = kids.Id, MembershipId = km.Id }); raw.SaveChanges(); }

        var newbie = await SignUp("minji", "김민지");
        await Save(newbie, "/join?code=JSKIDS", "04-참여신청");

        // 김철수: 3 · 4일 미션 + 오늘 말씀 하나
        var token = TokenRx().Match(await kid.GetStringAsync("/jamsil-kids/word")).Groups[1].Value;
        async Task Post(string path, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/jamsil-kids/missions/{path}") { Content = JsonContent.Create(body) };
            req.Headers.Add("RequestVerificationToken", token);
            (await kid.SendAsync(req)).EnsureSuccessStatusCode();
        }
        int Correct(DateOnly d) { using var raw = InMemoryDb.Raw(db); return raw.DailyQuizzes.Single(q => q.ExposeDate == d).CorrectChoiceId!.Value; }
        int Wrong(DateOnly d)
        {
            var right = Correct(d);
            using var raw = InMemoryDb.Raw(db);
            var qid = raw.DailyQuizzes.Single(q => q.ExposeDate == d).Id;
            return raw.DailyQuizChoices.First(c => c.DailyQuizId == qid && c.Id != right).Id;
        }
        foreach (var d in new[] { jan[2], jan[3] })
        {
            await Post("word", new { date = d, items = new[] { "Word.Me", "Word.Parent" } });
            await Post("prayer", new { date = d, items = new[] { "Prayer.Me", "Prayer.Others" } });
            await Post("quiz/attempts", new { date = d, choiceId = Correct(d) });
        }
        await Post("recite", new { date = jan[3] });
        await Post("word", new { date = jan[4], items = new[] { "Word.Me" } });
        await Post("quiz/attempts", new { date = jan[4], choiceId = Wrong(jan[4]) });

        await Save(kid, "/jamsil-kids", "05-부서홈");
        await Save(kid, "/jamsil-kids/word-mission", "06-미션홈");
        await Save(kid, "/jamsil-kids/word", "07-말씀");
        await Save(kid, "/jamsil-kids/read?from=word", "07b-성경읽기");
        await Save(kid, "/jamsil-kids/read?from=word&tr=ALL&unit=1", "07c-성경읽기-번역모두");

        // 통독(시안 예시와 비슷하게): 창세기 1~23장을 읽었고 24장 3절에서 멈춤
        using (var raw = InMemoryDb.Raw(db))
        {
            var me = Acc("cheolsu");
            var start = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            raw.ReadingRounds.Add(new ReadingRound { AccountId = me, RoundNo = 1, StartedUtc = start });
            for (short c = 1; c <= 23; c++)
                raw.ChapterReads.Add(new ChapterRead { AccountId = me, RoundNo = 1, BookId = 1, Chapter = c, ReadUtc = start.AddDays(Math.Min(4, (c - 1) / 5)) });
            raw.RoundVerseReads.Add(new RoundVerseRead { AccountId = me, RoundNo = 1, BookId = 1, Chapter = 24, Verse = 1, ReadUtc = start.AddDays(4) });
            raw.RoundVerseReads.Add(new RoundVerseRead { AccountId = me, RoundNo = 1, BookId = 1, Chapter = 24, Verse = 2, ReadUtc = start.AddDays(4) });
            raw.ChapterProgresses.Add(new ChapterProgress { AccountId = me, RoundNo = 1, BookId = 1, Chapter = 24, LastViewedVerse = 3, UpdatedUtc = start.AddDays(4) });
            raw.Bookmarks.Add(new Bookmark { AccountId = me, BookId = 1, Chapter = 24, Verse = 2, CreatedUtc = start.AddDays(4) });
            raw.Bookmarks.Add(new Bookmark { AccountId = me, BookId = 43, Chapter = 3, Verse = 16, CreatedUtc = start.AddDays(1) });
            raw.SaveChanges();
        }
        await Save(kid, "/jamsil-kids/reading", "18-통독-대시보드");
        await Save(kid, "/jamsil-kids/reading/list?t=ot&b=1", "19-통독-성경목록");
        await Save(kid, "/jamsil-kids/reading/read/1/24?unit=3", "20-통독-본문읽기");
        await Save(kid, "/jamsil-kids/reading/bookmarks", "21-통독-북마크");
        using (var raw = InMemoryDb.Raw(db))
        {
            var at = new DateTime(2027, 1, 4, 0, 0, 0, DateTimeKind.Utc);
            foreach (var (w, n) in new[] { ("사랑", 5), ("아브라함", 4), ("노아", 3), ("믿음", 2), ("다윗", 1) })
                for (var i = 0; i < n; i++) raw.SearchLogs.Add(new SearchLog { TenantId = kids.Id, Term = w, SearchedUtc = at });
            raw.SaveChanges();
        }
        await Save(kid, "/jamsil-kids/reading/search", "22-통독-검색");
        await Save(kid, "/jamsil-kids/reading/search?q=NIV%201:24&tr=ALL&s=all", "23-통독-검색결과");
        using (var raw = InMemoryDb.Raw(db))
        {
            // 이은혜 선생님도 마태복음 1~5장을 읽는 중(어제)
            var t = Acc("eunhye");
            var at = new DateTime(2027, 1, 4, 1, 0, 0, DateTimeKind.Utc);
            raw.ReadingRounds.Add(new ReadingRound { AccountId = t, RoundNo = 1, StartedUtc = at });
            for (short c = 1; c <= 5; c++) raw.ChapterReads.Add(new ChapterRead { AccountId = t, RoundNo = 1, BookId = 40, Chapter = c, ReadUtc = at });
            raw.SaveChanges();
        }
        await Save(kid, "/jamsil-kids/reading/participants", "24-통독-참여자현황");
        await Save(admin, "/jamsil-kids/admin/reading", "25-관리-통독현황");
        await Save(kid, "/jamsil-kids/quiz", "08-퀴즈");
        await Save(kid, "/jamsil-kids/prayer", "09-기도");
        await Save(kid, "/jamsil-kids/recite", "10-암송");
        await Save(kid, "/jamsil-kids/points", "11-내점수");
        await Save(admin, "/jamsil-kids/admin", "11b-관리홈");
        await Save(admin, "/jamsil-kids/admin/members", "12-소속관리");
        // 이름 · 색: 초록 · "유년부 말씀 챌린지"로 바꾼 뒤 모습(바꾼 기록 1건), 찍고 나서 기본값으로 되돌린다
        void Brand(string? name, string? shortName, string color)
        {
            using var raw = InMemoryDb.Raw(db);
            var t = raw.Tenants.Single(x => x.Id == kids.Id);
            (t.AppName, t.AppShortName, t.ThemeColor) = (name, shortName, color);
            raw.SaveChanges();
        }
        Brand("유년부 말씀 챌린지", "유년부", "green");
        using (var raw = InMemoryDb.Raw(db))
        {
            raw.AuditLogs.Add(new AuditLog { OccurredUtc = new DateTime(2027, 1, 4, 1, 0, 0, DateTimeKind.Utc), ActorAccountId = Acc("eunhye"), Action = BrandingService.LogAction,
                TenantId = kids.Id, Detail = "앱 이름 기본 → 유년부 말씀 챌린지, 짧은 이름 기본 → 유년부, 대표 색 파랑 → 초록" });
            raw.SaveChanges();
        }
        await Save(admin, "/jamsil-kids/admin/branding", "12c-관리-이름색");
        await Save(kid, "/jamsil-kids", "12d-부서홈-대표색초록");
        Brand(null, null, "blue");
        await Save(admin, "/jamsil-kids/admin/season?D=2027-01-04", "12e-관리-시즌");   // 1월 4일로 늦추면 빠지는 점수

        // 보호자 김영희 → 김철수 연결
        var mom = await SignUp("younghee", "김영희");
        var mm = InMemoryDb.SeedMembership(db, kids.Id, Acc("younghee"), MembershipStatus.Approved, Roles.Guardian);
        using (var raw = InMemoryDb.Raw(db))
        {
            raw.GuardianLinks.Add(new GuardianLink { TenantId = kids.Id, GuardianMembershipId = mm.Id, ParticipantId = raw.Participants.Single(p => p.MembershipId == km.Id).Id, CreatedUtc = DateTime.UtcNow, CreatedByAccountId = Acc("eunhye") });
            raw.SaveChanges();
        }
        await Save(admin, $"/jamsil-kids/admin/members?Tab={MembershipStatus.Approved}", "12b-소속관리-승인됨",
            h => Regex.Replace(h, "<details>(?=\\s*<summary class=\"line\">\\s*<span class=\"av\">철수)", "<details open>"));   // 김철수 줄 펼침
        await Save(mom, "/jamsil-kids/guardian", "16-보호자");
        await Save(mom, "/jamsil-kids/guardian?date=2027-01-04", "17-보호자-지난날");

        // 진급(시안 예시와 같은 지어낸 이름): 유치부 → 유년부(받은 요청), 유년부 → 초등부(보낸 요청)
        var pre = InMemoryDb.SeedTenant(db, "jamsil-pre", "JSPREE");
        var elem = InMemoryDb.SeedTenant(db, "jamsil-elem", "JSELEM");
        var jan1 = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var raw = InMemoryDb.Raw(db))
        {
            raw.Tenants.Single(t => t.Id == pre.Id).Name = "잠실새내교회 유치부";
            raw.Tenants.Single(t => t.Id == pre.Id).ThemeColor = "red";
            raw.Tenants.Single(t => t.Id == elem.Id).Name = "잠실새내교회 초등부";
            raw.Tenants.Single(t => t.Id == elem.Id).ThemeColor = "green";
            raw.SaveChanges();
        }
        int NewKid(string email, string name)
        {
            using var raw = InMemoryDb.Raw(db);
            var a = new Account { Email = email + "@example.com", DisplayName = name, PasswordHash = "-" };
            raw.Accounts.Add(a);
            raw.SaveChanges();
            return a.Id;
        }
        var jiwoo = NewKid("jiwoo", "한지우");
        InMemoryDb.SeedMembership(db, pre.Id, jiwoo, MembershipStatus.Approved, Roles.Participant);
        var daon = NewKid("daon", "오다온");
        var seoa = NewKid("seoa", "진서아");
        using (var raw = InMemoryDb.Raw(db))
        {
            var preTeacher = Acc("eunhye");
            raw.PromotionRequests.AddRange(
                new PromotionRequest { AccountId = jiwoo, SourceTenantId = pre.Id, TargetTenantId = kids.Id, RequestedUtc = jan1.AddDays(2), RequestedByAccountId = preTeacher },
                new PromotionRequest { AccountId = daon, SourceTenantId = pre.Id, TargetTenantId = kids.Id, RequestedUtc = jan1.AddDays(-3), RequestedByAccountId = preTeacher,
                    Status = PromotionStatus.Approved, DecidedUtc = jan1.AddDays(-2), DecidedByAccountId = Acc("eunhye") },
                new PromotionRequest { AccountId = seoa, SourceTenantId = kids.Id, TargetTenantId = elem.Id, RequestedUtc = jan1.AddDays(-4), RequestedByAccountId = Acc("eunhye"),
                    Status = PromotionStatus.Approved, DecidedUtc = jan1.AddDays(-3), DecidedByAccountId = Acc("eunhye") },
                new PromotionRequest { AccountId = Acc("cheolsu"), SourceTenantId = kids.Id, TargetTenantId = elem.Id, RequestedUtc = jan1.AddDays(1), RequestedByAccountId = Acc("eunhye") });
            raw.SaveChanges();
        }
        await Save(admin, "/jamsil-kids/admin", "26-관리홈-진급요청");
        await Save(admin, "/jamsil-kids/admin/promotions", "27-진급-받은요청",
            h => h.Replace("<details class=\"ask ok\">", "<details class=\"ask ok\" open>"));   // 승인 확인 펼침
        await Save(admin, "/jamsil-kids/admin/promotions?Tab=out", "28-진급-보낸요청");
        var minjun = NewKid("minjun", "최민준");
        var mj = InMemoryDb.SeedMembership(db, kids.Id, minjun, MembershipStatus.Approved, Roles.Participant);
        await SavePost(admin, $"/jamsil-kids/admin/members?Tab={MembershipStatus.Approved}", "29-소속관리-진급보내기",
            ("handler", "PromoteCheck"), ("pick", mj.Id.ToString()), ("code", "JSELEM"));

        // 참여 신청 v2(0059): 목록에 보이기를 켠 부서에서 고르기
        using (var raw = InMemoryDb.Raw(db))
        {
            foreach (var t in raw.Tenants) t.JoinListed = true;
            raw.SaveChanges();
        }
        await Save(newbie, "/join", "30-참여신청-목록");
        await Save(newbie, "/join?code=JSELEM", "31-참여신청-주소로");

        // 운영자: 가입 → 플랫폼 관리자 → 다시 로그인
        var op = await SignUp("operator", "운영자");
        using (var raw = InMemoryDb.Raw(db)) { raw.Accounts.Single(a => a.Email == "operator@example.com").IsPlatformAdmin = true; raw.SaveChanges(); }
        var lh = await op.GetStringAsync("/account/login");
        await op.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(lh).Groups[1].Value, ["Email"] = "operator@example.com", ["Password"] = "1234",
        }));
        await Save(op, "/platform/tenants", "13-플랫폼부서");
        await Save(op, "/platform/content/word?month=2027-01&date=2027-01-05", "14-공용콘텐츠-말씀");
        await Save(op, "/platform/content/quiz?month=2027-01&date=2027-01-05", "15-공용콘텐츠-퀴즈");
    }

    private static async Task Save(HttpClient c, string url, string name, Func<string, string>? tweak = null)
    {
        var html = await c.GetStringAsync(url);
        if (tweak is not null) html = tweak(html);
        await Write(html, name);
    }

    /// <summary>폼을 보낸 결과 화면(예: 진급 보내기의 부서 확인)을 저장한다.</summary>
    private static async Task SavePost(HttpClient c, string url, string name, params (string, string)[] form)
    {
        var token = TokenRx().Match(await c.GetStringAsync(url)).Groups[1].Value;
        var fields = form.Append(("__RequestVerificationToken", token)).Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2));
        var r = await c.PostAsync(url + (url.Contains('?') ? "&" : "?") + "handler=" + fields.First(f => f.Key == "handler").Value,
            new FormUrlEncodedContent(fields.Where(f => f.Key != "handler")));
        await Write(await r.Content.ReadAsStringAsync(), name);
    }

    private static async Task Write(string html, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CB_SCREENS_DIR")!;
        // 배포 때는 파일 이름에 지문이 붙는다(/css/site.css?v=…) — 화면이 쓰는 스타일 파일(site · reading)을 그대로 안에 넣는다
        foreach (var name2 in new[] { "site", "reading" })
        {
            var css = await File.ReadAllTextAsync(Path.Combine(FindWeb(), "wwwroot", "css", name2 + ".css"));
            html = Regex.Replace(html, $"<link rel=\"stylesheet\" href=\"/css/{name2}[^\"]*\\.css[^\"]*\" />", _ => $"<style>{css}</style>");
        }
        html = Regex.Replace(html, "<script src=\"/js/(mission|reading)[^\"]*\"></script>", "");
        await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), html);
    }

    private static string FindWeb()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "ChallengeBible.Web"))) d = d.Parent;
        return Path.Combine(d!.FullName, "src", "ChallengeBible.Web");
    }
}
