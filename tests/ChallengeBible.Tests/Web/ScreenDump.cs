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
        string Acc(string id) { using var raw = InMemoryDb.Raw(db); return raw.Users.Single(a => a.Email == id + "@example.com").UserName!; }

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
        using (var raw = InMemoryDb.Raw(db)) { raw.SaveChanges(); }

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
        await Save(kid, "/jamsil-kids/word-mission?date=2027-01-08", "06b-미션홈-미래날짜");   // 오늘 이후는 보기만 가능해요(2026-11-04)
        await Save(kid, "/jamsil-kids/word-mission?date=2027-01-04", "06c-미션홈-지난날-할수있음");   // 주황 카드 ②(확정 시안 미션홈-다른날카드)
        await Save(kid, "/jamsil-kids/word", "07-말씀");
        await Save(kid, "/jamsil-kids/word?date=2027-01-08", "07d-말씀-미래날짜");   // 주황 카드(각 미션 공용)
        await Save(kid, "/jamsil-kids/quiz?date=2027-01-04", "07e-퀴즈-지난날");
        await Save(kid, "/jamsil-kids/read?from=word", "07b-성경읽기");
        await Save(kid, "/jamsil-kids/read?from=word&tr=ALL&unit=1", "07c-성경읽기-번역모두");

        // 통독(시안 예시와 비슷하게): 창세기 1~23장을 읽었고 24장 3절에서 멈춤
        using (var raw = InMemoryDb.Raw(db))
        {
            var me = Acc("cheolsu");
            var start = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            raw.ReadingRounds.Add(new ReadingRound { UserName = me, RoundNo = 1, StartedAt = start });
            for (short c = 1; c <= 23; c++)
                raw.ReadingChapterReads.Add(new ReadingChapterRead { UserName = me, RoundNo = 1, BookId = 1, Chapter = c, ReadAt = start.AddDays(Math.Min(4, (c - 1) / 5)) });
            raw.ReadingVerses.Add(new ReadingVerse { UserName = me, RoundNo = 1, BookId = 1, Chapter = 24, Verse = 1, ReadAt = start.AddDays(4) });
            raw.ReadingVerses.Add(new ReadingVerse { UserName = me, RoundNo = 1, BookId = 1, Chapter = 24, Verse = 2, ReadAt = start.AddDays(4) });
            raw.ReadingChapterProgresses.Add(new ReadingChapterProgress { UserName = me, RoundNo = 1, BookId = 1, Chapter = 24, LastViewedVerse = 3, UpdatedAt = start.AddDays(4) });
            raw.ReadingBookmarks.Add(new ReadingBookmark { UserName = me, BookId = 1, Chapter = 24, Verse = 2, CreatedAt = start.AddDays(4) });
            raw.ReadingBookmarks.Add(new ReadingBookmark { UserName = me, BookId = 43, Chapter = 3, Verse = 16, CreatedAt = start.AddDays(1) });
            raw.SaveChanges();
        }
        await Save(kid, "/jamsil-kids/reading", "18-통독-대시보드");
        await Save(kid, "/jamsil-kids/reading/list?t=ot&b=1", "19-통독-성경목록");
        await Save(kid, "/jamsil-kids/reading/read/1/24?unit=3", "20-통독-본문읽기");
        // 책 개요(R-116, 확정 시안 통독-본문읽기-책개요): 1장은 펼침, 다른 장은 접힘
        using (var raw = InMemoryDb.Raw(db))
        {
            raw.BibleBookOverviews.Add(new BibleBookOverview
            {
                BookId = 1, Author = "모세", WhenWritten = "예수님이 태어나기 전(B.C.) 1446년 ~ 1406년경",
                Summary = "하나님이 세상을 만드시고, 아브라함 · 이삭 · 야곱 · 요셉으로 이어지는 믿음의 이야기예요.",
                Purpose = "하나님이 세상과 사람을 만드시고, 한 가족을 골라 복을 주시겠다고 약속하신 것을 알려 주기 위해 기록했어요.",
                DetailAuthor = "모세", AuthorDescription = "이스라엘 백성을 이집트에서 이끌어 낸 지도자예요.", WrittenFrom = "B.C. 1446년", WrittenTo = "1406년",
                AlternativeDate = "B.C. 1290년경", WrittenPlace = "시내 광야", WrittenTarget = "광야에서 지내던 이스라엘 백성", HistoricalPeriod = "창조 ~ 족장 시대",
                Keywords = "창조, 타락, 언약, 족장", ContentSummary = "1 ~ 11장은 세상의 시작, 12 ~ 50장은 아브라함 · 이삭 · 야곱 · 요셉의 이야기예요.",
                VideoUrl1 = "dLv2ndgXrbo", VideoUrl2 = "jIcsdztsKBE",
            });
            // 듣기 · 영상(R-166, 확정 시안 통독-본문읽기-듣기영상): 창세기 24장 듣기
            raw.ChapterMedia.Add(new ChapterMedia { BookId = 1, Chapter = 24, Kind = ChapterMediaKind.Listen, SortOrder = 1, YoutubeVideoId = "cMKPbDZhToo", DurationSec = 492 });
            raw.SaveChanges();
        }
        await Save(kid, "/jamsil-kids/reading/read/1/1?unit=3", "20b-통독-본문-책개요-1장");
        await Save(kid, "/jamsil-kids/reading/read/1/24?unit=3", "20c-통독-본문-책개요-접힘");
        await Save(kid, "/jamsil-kids/reading/read/1/24?unit=3", "20d-통독-본문-듣기-재생창",
            h => h.Replace("id=\"ytm\" hidden", "id=\"ytm\"").Replace("<b id=\"ytmT\"></b>", "<b id=\"ytmT\">🎧 창세기 24장 듣기</b>"));   // 재생 창 열린 모습
        // 말씀 듣기 구간(R-127, 확정 시안 말씀듣기-clips ㄱ안): 듣기 칸을 펼친 모습 — 구간 하나 · 구간 둘 · 구간 없는 날
        using (var raw = InMemoryDb.Raw(db))
        {
            raw.DailyWordClips.Add(new DailyWordClip { WordDate = jan[4], Seq = 0, Chapter = 24, StartSec = 65, EndSec = 460 });
            raw.DailyWordClips.Add(new DailyWordClip { WordDate = jan[3], Seq = 0, Chapter = 24, StartSec = 380 });
            raw.DailyWordClips.Add(new DailyWordClip { WordDate = jan[3], Seq = 1, Chapter = 25, YoutubeVideoId = "dLv2ndgXrbo", EndSec = 70 });
            raw.SaveChanges();
        }
        static string OpenListen(string h) => h.Replace("class=\"rnv-pan\" id=\"rnvMd\"", "class=\"rnv-pan open\" id=\"rnvMd\"").Replace("data-rnv-open=\"md\" aria-expanded=\"false\"", "data-rnv-open=\"md\" aria-expanded=\"true\"");
        await Save(kid, "/jamsil-kids/read?from=word", "07d-성경읽기-듣기-구간하나", OpenListen);
        await Save(kid, "/jamsil-kids/read?from=word&date=2027-01-04", "07e-성경읽기-듣기-구간둘", OpenListen);
        await Save(kid, "/jamsil-kids/read?from=word&date=2027-01-03", "07f-성경읽기-듣기-구간없음", OpenListen);
        await Save(kid, "/jamsil-kids/reading/bookmarks", "21-통독-북마크");
        // 회차 기록(확정 시안 통독-회차기록): 지금까지 읽은 회차를 "중간에 닫음"으로 두고 2회차에 두 장, 새 회차 시작 확인을 펼친 모습
        var kidAcc = Acc("cheolsu");
        using (var raw = InMemoryDb.Raw(db))
        {
            var first = raw.ReadingRounds.Single(r => r.UserName == kidAcc);
            first.ClosedAt = new DateTime(2027, 1, 4, 0, 0, 0, DateTimeKind.Utc);
            raw.ReadingRounds.Add(new ReadingRound { UserName = kidAcc, RoundNo = first.RoundNo + 1, StartedAt = new DateTime(2027, 1, 4, 0, 0, 0, DateTimeKind.Utc) });
            foreach (short ch in new short[] { 1, 2 })
                raw.ReadingChapterReads.Add(new ReadingChapterRead { UserName = kidAcc, RoundNo = first.RoundNo + 1, BookId = 40, Chapter = ch, ReadAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        await Save(kid, "/jamsil-kids/reading/rounds?ask=true", "21c-통독-회차기록");
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
            raw.ReadingRounds.Add(new ReadingRound { UserName = t, RoundNo = 1, StartedAt = at });
            for (short c = 1; c <= 5; c++) raw.ReadingChapterReads.Add(new ReadingChapterRead { UserName = t, RoundNo = 1, BookId = 40, Chapter = c, ReadAt = at });
            raw.SaveChanges();
        }
        await Save(kid, "/jamsil-kids/reading/participants", "24-통독-참여자현황");
        await Save(admin, "/jamsil-kids/admin/reading", "25-관리-통독현황");
        await Save(admin, $"/jamsil-kids/admin/members/{km.Id}", "25b-관리-참여자상세");
        await Save(kid, "/jamsil-kids/quiz", "08-퀴즈");
        await Save(kid, "/jamsil-kids/prayer", "09-기도");
        await Save(kid, "/jamsil-kids/recite", "10-암송");
        await Save(kid, "/jamsil-kids/points", "11-내점수");
        await Save(kid, "/jamsil-kids/ranking", "11c-순위");
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
            raw.AuditLogs.Add(new AuditLog { OccurredUtc = new DateTime(2027, 1, 4, 1, 0, 0, DateTimeKind.Utc), ActorUserName = Acc("eunhye"), Action = BrandingService.LogAction,
                TenantId = kids.Id, Detail = "앱 이름 기본 → 유년부 말씀 챌린지, 짧은 이름 기본 → 유년부, 대표 색 파랑 → 초록" });
            raw.SaveChanges();
        }
        await Save(admin, "/jamsil-kids/admin/branding", "12c-관리-이름색");
        await Save(kid, "/jamsil-kids", "12d-부서홈-대표색초록");
        Brand(null, null, "blue");
        await Save(admin, "/jamsil-kids/admin/season?D=2027-01-04", "12e-관리-시즌");   // 1월 4일로 늦추면 빠지는 점수
        await Save(admin, "/jamsil-kids/admin/content", "12f-관리-콘텐츠달력");
        await Save(admin, "/jamsil-kids/admin/content/prayer?date=2027-01-05", "12h-관리-부서기도입력");

        // 보호자 김영희 → 김철수 연결
        var mom = await SignUp("younghee", "김영희");
        var mm = InMemoryDb.SeedMembership(db, kids.Id, Acc("younghee"), MembershipStatus.Approved, Roles.Guardian);
        using (var raw = InMemoryDb.Raw(db))
        {
            raw.GuardianLinks.Add(new GuardianLink { TenantId = kids.Id, GuardianUserName = mm.UserName, StudentUserName = km.UserName, CreatedUtc = DateTime.UtcNow, CreatedByUserName = Acc("eunhye") });
            raw.SaveChanges();
        }
        await Save(admin, $"/jamsil-kids/admin/members?Tab={MembershipStatus.Approved}", "12b-소속관리-승인됨",
            h => Regex.Replace(h, "<details>(?=\\s*<summary class=\"line\">\\s*<span class=\"av\">철수)", "<details open>"));   // 김철수 줄 펼침
        // 반(확정 시안 A-반목록-단순 · B-미배정반 · 반안-사람넣기, 0093 반 관리 및 자동 생성 규칙)
        int clsId, termId;
        using (var raw = InMemoryDb.Raw(db))
        {
            var now = DateTime.UtcNow;
            var term = new ClassTerm { TenantId = kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 3), CreatedUtc = now };
            raw.ClassTerms.Add(term);
            raw.SaveChanges();
            var nc = new SchoolClass { TenantId = kids.Id, TermId = term.Id, Name = "새친구반", IsNewcomer = true, CreatedUtc = now };
            var c1 = new SchoolClass { TenantId = kids.Id, TermId = term.Id, Name = "1학년 1반", CreatedUtc = now };
            raw.Classes.AddRange(nc, c1, new SchoolClass { TenantId = kids.Id, TermId = term.Id, Name = "1학년 2반", CreatedUtc = now });
            raw.People.Add(new Person { TenantId = kids.Id, Name = "박하린", RoleCode = "T1001", BirthMonth = 3, BirthDay = 5, CreatedUtc = now });
            raw.People.Add(new Person { TenantId = kids.Id, Name = "한겨울", RoleCode = "T1020", CreatedUtc = now });
            raw.SaveChanges();
            (clsId, termId) = (c1.Id, term.Id);
        }
        await Save(admin, "/jamsil-kids/admin/members?Tab=" + MembershipStatus.Approved, "12x-명부-반준비");   // 명부 줄을 만들어 둔다
        using (var raw = InMemoryDb.Raw(db))
        {
            var kidPid = raw.People.Single(p => p.UserId == km.UserId).Id;
            raw.ClassMembers.Add(new ClassMember { TenantId = kids.Id, TermId = termId, ClassId = clsId, PersonId = kidPid, CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        await Save(admin, "/jamsil-kids/admin/classes", "12n-반목록");
        await Save(admin, $"/jamsil-kids/admin/classes/{clsId}", "12o-반안-사람넣기");
        await Save(admin, "/jamsil-kids/admin/classes/unassigned", "12p-미배정반");

        // 출석(확정 시안 A-출석부-오늘 · B-새친구 · C-새친구-등록, 0093 출석): 한겨울은 새친구로 1학년 1반, 김철수는 이번 주일 출석
        using (var raw = InMemoryDb.Raw(db))
        {
            var sun = new DateOnly(2027, 1, 3);
            var win = raw.People.Single(p => p.Name == "한겨울");
            (win.RoleCode, win.NewcomerFirstVisit, win.NewcomerVoluntary, win.NewcomerEvangelist) = ("T1001", sun.AddDays(-14), false, "김철수");
            raw.ClassMembers.Add(new ClassMember { TenantId = kids.Id, TermId = termId, ClassId = clsId, PersonId = win.Id, CreatedUtc = DateTime.UtcNow });
            var kidPid = raw.People.Single(p => p.UserId == km.UserId).Id;
            raw.AttendanceChecks.AddRange(
                new AttendanceCheck { TenantId = kids.Id, PersonId = kidPid, Sunday = sun },
                new AttendanceCheck { TenantId = kids.Id, PersonId = kidPid, Sunday = sun.AddDays(-7) },
                new AttendanceCheck { TenantId = kids.Id, PersonId = win.Id, Sunday = sun.AddDays(-14) });
            raw.SaveChanges();
        }
        await Save(admin, "/jamsil-kids/attendance", "12q-출석부-오늘");
        await Save(admin, "/jamsil-kids/attendance/newcomers", "12r-새친구");
        await Save(admin, "/jamsil-kids/attendance/newcomers/new", "12s-새친구-등록");
        // 새친구 홈 · 오늘 출석 체크 · 기존 출석 변경(확정 시안 K1 · K2 · K5 · K6 · K7)
        await Save(admin, "/jamsil-kids/attendance/newcomers/today", "12s2-오늘출석체크");
        await Save(admin, "/jamsil-kids/attendance/more", "12y1-출석-더보기");   // 유년부 "더보기" 그대로
        await Save(admin, "/jamsil-kids/attendance/care", "12y2-돌봄");   // 유년부 "돌봄" 그대로
        await Save(admin, "/jamsil-kids/attendance/report", "12y3-주일보고서");
        await Save(admin, "/jamsil-kids/attendance/sunday", "12y4-주일명단");
        await Save(admin, "/jamsil-kids/attendance/trend", "12y5-출석현황3주");
        await Save(admin, "/jamsil-kids/attendance/byclass", "12y6-반별출석");
        await Save(admin, "/jamsil-kids/attendance/rank", "12y7-출석등수");
        await Save(admin, "/jamsil-kids/attendance/matrix?weeks=13", "12y8-주별출석표");
        await Save(admin, "/jamsil-kids/attendance/dashboard/all", "12y9-전체반현황");
        int dashClass;
        using (var raw = InMemoryDb.Raw(db)) dashClass = raw.ClassMembers.Where(c => c.TenantId == kids.Id && !c.IsTeacher).OrderBy(c => c.Id).Select(c => c.ClassId).FirstOrDefault();
        await Save(admin, $"/jamsil-kids/attendance/dashboard?classId={dashClass}", "12y10-반대시보드");
        await Save(admin, "/jamsil-kids/attendance/newcomers/past", "12s3-기존출석-달");
        await Save(admin, "/jamsil-kids/attendance/newcomers/past?M=1", "12s4-기존출석-주일");
        await Save(admin, "/jamsil-kids/attendance/newcomers/past?D=2027-01-03", "12s5-기존출석-바꾸기");

        // 반 이동(확정 시안 A-반이동 · B-미배정반-꼬리표): 박하린은 지난 반 꼬리표가 붙은 미배정 아이
        using (var raw = InMemoryDb.Raw(db))
        {
            var p = raw.People.Single(x => x.Name == "박하린");
            (p.PrevDeptName, p.PrevClassName, p.PrevTeacherName, p.PrevTeacherPhone) = ("잠실새내교회 유년부", "3학년5반", "정교사", "010-1234-5678");
            raw.SaveChanges();
        }
        await Save(admin, "/jamsil-kids/admin/classes/move", "12t-반이동-1반");
        await Save(admin, $"/jamsil-kids/admin/classes/move?S=2&F={clsId}", "12t-반이동-2아이");
        int kidP;
        using (var raw = InMemoryDb.Raw(db)) kidP = raw.People.Single(p => p.UserId == km.UserId).Id;
        await Save(admin, $"/jamsil-kids/admin/classes/move?S=3&F={clsId}&P={kidP}", "12t-반이동-3보낼곳");
        await Save(admin, $"/jamsil-kids/admin/classes/move?S=4&F={clsId}&P={kidP}&D=Unassigned", "12t-반이동-4확인");
        await Save(admin, "/jamsil-kids/admin/classes/unassigned", "12u-미배정반-꼬리표");
        await Save(mom, "/jamsil-kids/guardian", "16-보호자");
        await Save(mom, "/jamsil-kids/guardian?date=2027-01-04", "17-보호자-지난날");

        // 가족팀(확정 시안 관리-가족팀): 김영희가 이 부서의 참여자가 되어 김철수와 한 팀
        int teamId;
        var momAcc = Acc("younghee");
        using (var raw = InMemoryDb.Raw(db))
        {
            raw.SaveChanges();
            var team = new FamilyTeam { TenantId = kids.Id, Name = "철수네 가족팀", InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow, Members =
            [
                new FamilyMember { TenantId = kids.Id, UserName = km.UserName, Relation = FamilyRelation.Student, SortOrder = 0 },
                new FamilyMember { TenantId = kids.Id, UserName = mm.UserName, Relation = FamilyRelation.Mother, SortOrder = 1 },
            ] };
            raw.FamilyTeams.Add(team);
            raw.SaveChanges();
            teamId = team.Id;
        }
        await Save(admin, $"/jamsil-kids/admin/teams?Open={teamId}", "12i-관리-가족팀");
        await Save(admin, "/jamsil-kids/admin/teams/stages", "12j-관리-시상기준-기본값");
        await Save(admin, "/jamsil-kids/admin/teams/stages?Y=2028", "12j2-관리-시상기준-내년");
        int locked;
        using (var raw = InMemoryDb.Raw(db))   // 확정 시안 ③ · ④: 우리 부서 기준, 3,000점은 1팀이 받음
        {
            var at = DateTime.UtcNow;
            FamilyMilestoneConfig C(int p, string t, string x, string e, bool on, int o) => new() { TenantId = kids.Id, Year = 2027, PointThreshold = p, MilestoneType = t, DisplayTitle = x, DisplayEmoji = e, IsActive = on, SortOrder = o, CreatedAt = at };
            var three = C(3000, MilestoneTypes.Award, "3,000점! 가족이 함께 해냈어요", "🎉", true, 2);
            raw.FamilyMilestoneConfigs.AddRange(C(500, MilestoneTypes.Applause, "500점 돌파, 박수!", "👏", true, 0), C(1500, MilestoneTypes.Award, "1,500점! 가족이 함께 해냈어요", "🎉", true, 1),
                three, C(5000, MilestoneTypes.Award, "5,000점! 최고의 가족", "🏆", false, 3));
            raw.TeamAwards.Add(new TeamAward { TenantId = kids.Id, Kind = TeamTypes.Family, AwardKey = "철수네 가족팀", Threshold = 3000, AwardedAt = new DateTime(2027, 1, 4) });
            raw.SaveChanges();
            locked = three.Id;
        }
        await Save(admin, "/jamsil-kids/admin/teams/awards", "12m-관리-시상기록");
        await Save(admin, "/jamsil-kids/admin/teams/stages", "12k-관리-시상기준-우리");
        await Save(admin, $"/jamsil-kids/admin/teams/stages?Edit={locked}", "12l-관리-시상기준-잠김고치기");
        await Save(kid, "/jamsil-kids", "05b-부서홈-우리가족팀");
        await Save(kid, "/jamsil-kids/family-team", "33a-가족팀-우리");
        await Save(kid, "/jamsil-kids/word-mission", "33b-미션홈-가족팀입구");
        await Save(kid, "/jamsil-kids/ranking?t=team", "11d-순위-가족팀");
        using (var raw = InMemoryDb.Raw(db))   // 확정 시안 관리-시상기록 ①: 1,500점 "축하 + 보상"을 넘은 팀
        {
            raw.MissionActivities.Add(new MissionActivity { TenantId = kids.Id, UserName = mm.UserName, MissionDate = new DateOnly(2027, 1, 4), MissionItem = "Word.Me", Points = 1600, PerformedAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        await Save(admin, "/jamsil-kids/admin/teams/awards", "12m2-관리-시상기록-줄차례");

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
        string NewKid(string email, string name) => InMemoryDb.SeedAccount(db, email + "@example.com", name).UserName!;
        var jiwoo = NewKid("jiwoo", "한지우");
        InMemoryDb.SeedMembership(db, pre.Id, jiwoo, MembershipStatus.Approved, Roles.Participant);
        var daon = NewKid("daon", "오다온");
        var seoa = NewKid("seoa", "진서아");
        using (var raw = InMemoryDb.Raw(db))
        {
            var preTeacher = Acc("eunhye");
            raw.PromotionRequests.AddRange(
                new PromotionRequest { UserName = jiwoo, SourceTenantId = pre.Id, TargetTenantId = kids.Id, RequestedUtc = jan1.AddDays(2), RequestedByUserName = preTeacher },
                new PromotionRequest { UserName = daon, SourceTenantId = pre.Id, TargetTenantId = kids.Id, RequestedUtc = jan1.AddDays(-3), RequestedByUserName = preTeacher,
                    Status = PromotionStatus.Approved, DecidedUtc = jan1.AddDays(-2), DecidedByUserName = Acc("eunhye") },
                new PromotionRequest { UserName = seoa, SourceTenantId = kids.Id, TargetTenantId = elem.Id, RequestedUtc = jan1.AddDays(-4), RequestedByUserName = Acc("eunhye"),
                    Status = PromotionStatus.Approved, DecidedUtc = jan1.AddDays(-3), DecidedByUserName = Acc("eunhye") },
                new PromotionRequest { UserName = Acc("cheolsu"), SourceTenantId = kids.Id, TargetTenantId = elem.Id, RequestedUtc = jan1.AddDays(1), RequestedByUserName = Acc("eunhye") });
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

        // 가족과 함께할 부서(확정 시안 가족부서고르기): 김영희는 초등부에서도 딸 김민지의 보호자
        var minji = NewKid("minji2", "김민지");
        var momElem = InMemoryDb.SeedMembership(db, elem.Id, momAcc, MembershipStatus.Approved, Roles.Guardian);
        var minjiM = InMemoryDb.SeedMembership(db, elem.Id, minji, MembershipStatus.Approved, Roles.Participant);
        using (var raw = InMemoryDb.Raw(db))
        {
            raw.SaveChanges();
            raw.GuardianLinks.Add(new GuardianLink { TenantId = elem.Id, GuardianUserName = momElem.UserName, StudentUserName = minjiM.UserName, CreatedByUserName = momAcc });
            raw.SaveChanges();
        }
        await Save(mom, "/account", "34-내계정", h => h.Replace("<details class=\"ma-ed\">", "<details class=\"ma-ed\" open>"));   // 확정 시안 내계정 — 이름 · 비밀번호 칸을 펼친 모습

        // 참여 신청 v2(0059): 목록에 보이기를 켠 부서에서 고르기
        using (var raw = InMemoryDb.Raw(db))
        {
            foreach (var t in raw.Tenants) t.JoinListed = true;
            raw.SaveChanges();
        }
        await Save(newbie, "/join", "30-참여신청-목록");
        await Save(newbie, "/join?code=JSELEM", "31-참여신청-주소로");

        // 임시 부서 "처음 오신 분"(0064, 확정 시안 임시부서-내부서찾기 ㄱ안): 가입하면 바로 여기, 맨 위 "내 부서 찾기" 띠
        var lobby = InMemoryDb.SeedTenant(db, "welcome", "WELCOM");
        using (var raw = InMemoryDb.Raw(db))
        {
            var t = raw.Tenants.Single(x => x.Id == lobby.Id);
            (t.Name, t.IsLobby, t.ReadingBoardPublic, t.JoinListed) = ("처음 오신 분", true, false, false);
            raw.SaveChanges();
        }
        var fresh = await SignUp("jiho", "박지호");
        await Save(fresh, "/welcome", "05c-임시부서-홈");
        await Save(fresh, "/welcome/today", "05e-임시부서-미션화면");
        InMemoryDb.SeedMembership(db, kids.Id, Acc("jiho"), MembershipStatus.Requested, Roles.Participant);
        await Save(fresh, "/welcome", "05d-임시부서-승인기다림");
        await Save(fresh, "/welcome/today", "05f-임시부서-미션화면-승인기다림");

        // 운영자: 가입 → 플랫폼 관리자 → 다시 로그인
        var op = await SignUp("operator", "운영자");
        using (var raw = InMemoryDb.Raw(db)) { InMemoryDb.AddRole(db, raw.Users.Single(a => a.Email == "operator@example.com").Id, IdentityRoles.SuperAdmin); raw.SaveChanges(); }
        var lh = await op.GetStringAsync("/account/login");
        await op.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(lh).Groups[1].Value, ["Email"] = "operator@example.com", ["Password"] = "1234",
        }));
        await Save(op, "/platform", "13a-플랫폼홈");
        await Save(op, "/platform/tenants", "13-플랫폼부서");
        await Save(op, "/platform/accounts?q=" + Uri.EscapeDataString("김"), "13b-플랫폼-계정찾기");
        // 가장(확정 시안 플랫폼-가장): 들어가기 확인 → 다른 운영자 창에서 이은혜(유년부 관리자)로 들어간 관리 홈(맨 위 보라 띠)
        var askImp = $"/platform/accounts?q=eunhye&id={Acc("eunhye")}&askImp=true";
        await Save(op, askImp, "13d-플랫폼-가장확인");
        var opImp = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var lp = await opImp.GetStringAsync("/account/login");
        await opImp.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(lp).Groups[1].Value, ["Email"] = "operator@example.com", ["Password"] = "1234",
        }));
        await opImp.PostAsync($"/platform/accounts?handler=Impersonate&q=eunhye&id={Acc("eunhye")}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await opImp.GetStringAsync(askImp)).Groups[1].Value,
        }));
        await Save(opImp, "/jamsil-kids/admin", "13e-가장중-관리홈");
        await Save(op, "/platform/rules", "13c-플랫폼-기본규칙");
        await Save(op, "/platform/content/word?month=2027-01&date=2027-01-05", "14-공용콘텐츠-말씀");
        await Save(op, "/platform/content/quiz?month=2027-01&date=2027-01-05", "15-공용콘텐츠-퀴즈");

        await Save(op, "/platform/ops?d=0", "15c-플랫폼-운영점검", x => x.Replace("<li><details>", "<li><details open>"));
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
        foreach (var name2 in new[] { "site", "reading-b2", "reading", "home", "landing", "mission-old" })
        {
            var css = await File.ReadAllTextAsync(Path.Combine(FindWeb(), "wwwroot", "css", name2 + ".css"));
            html = Regex.Replace(html, $"<link rel=\"stylesheet\" href=\"/css/{name2}[^\"]*\\.css[^\"]*\" />", _ => $"<style>{css}</style>");
        }
        html = Regex.Replace(html, "<script src=\"/js/(mission|reading|landing)[^\"]*\"></script>", "");
        await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), html);
    }

    private static string FindWeb()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "ChallengeBible.Web"))) d = d.Parent;
        return Path.Combine(d!.FullName, "src", "ChallengeBible.Web");
    }
}
