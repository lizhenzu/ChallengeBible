using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
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
        await Save(kid, "/jamsil-kids/quiz", "08-퀴즈");
        await Save(kid, "/jamsil-kids/prayer", "09-기도");
        await Save(kid, "/jamsil-kids/recite", "10-암송");
        await Save(kid, "/jamsil-kids/points", "11-내점수");
        await Save(admin, "/jamsil-kids/admin/members", "12-소속관리");

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
        var dir = Environment.GetEnvironmentVariable("CB_SCREENS_DIR")!;
        var html = await c.GetStringAsync(url);
        if (tweak is not null) html = tweak(html);
        var css = await File.ReadAllTextAsync(Path.Combine(FindWeb(), "wwwroot", "css", "site.css"));
        // 배포 때는 파일 이름에 지문이 붙는다(/css/site.abc123.css) — 그대로 안에 넣는다
        html = Regex.Replace(html, "<link rel=\"stylesheet\" href=\"/css/site[^\"]*\\.css[^\"]*\" />", _ => $"<style>{css}</style>");
        html = Regex.Replace(html, "<script src=\"/js/mission[^\"]*\"></script>", "");
        await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), html);
    }

    private static string FindWeb()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "ChallengeBible.Web"))) d = d.Parent;
        return Path.Combine(d!.FullName, "src", "ChallengeBible.Web");
    }
}
