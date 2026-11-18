using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>부서 홈 메인(확정 시안: 홈메인, 사용자 확정 8가지). 오늘 = 2027-01-05 (서울), 말씀 · 퀴즈만 있는 날.</summary>
public partial class HomeMainTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public HomeMainTests(WebApplicationFactory<Program> app)
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

    private async Task<(HttpClient C, string UserName)> Member(string email, params string[] roles)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = "김철수", ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        var id = raw.Users.Single(a => a.Email == email + "@example.com").UserName!;
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, roles);
        return (c, id);
    }

    [Fact]
    public async Task 참여자_홈은_말씀띠_미션카드_다음할일_계정줄을_보인다()
    {
        ContentSeed.Prayer(_db, Jan5);
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("<div class=\"hm sky\">", home);                              // 임시 부서 미션 화면과 같은 모양(2026-10-31): 하늘색 머리말
        Assert.DoesNotContain("class=\"tenant\"", home);                              // 소속 하나 · 관리자 아님 → 부서 이름 줄 없음(2026-11-03)
        Assert.DoesNotContain("님, 안녕하세요", home);                               // 인사 · 점수 칩 없음
        Assert.DoesNotContain("<small>오늘의 기도</small>", home);                    // 말씀 카드는 말씀만
        Assert.DoesNotContain("class=\"hj-rib\"", home);                             // 말씀 한 줄 띠는 지움(2026-11-03)
        Assert.Contains("<b class=\"num\">0/3</b>오늘 미션", home);                  // 오늘 있는 미션은 말씀 · 퀴즈 · 기도 세 가지
        Assert.DoesNotContain("class=\"hj-m4\"", home);                                  // 미션 네 칸은 지웠다 — 미션 홈에서 한다(2026-11-18 사용자)
        Assert.Contains("<a class=\"hj-box o\" href=\"/jamsil-kids/word-mission\">", home);
        Assert.Matches("<b class=\"num\">\\d+<u>명</u></b><small>전체</small>", home);              // 아래 칸 셋(ADR 0085, 2026-11-07): 전체 · 참여자 · 오늘 참여
        Assert.Matches("<b class=\"num\">\\d+<u>명</u></b><small>참여자</small>", home);
        Assert.Matches("<b class=\"num\">\\d+<u>명</u></b><small>오늘 참여</small>", home);
        Assert.DoesNotContain("우리 부서 참여자", home);
        Assert.DoesNotContain("나는 오늘", home);
        Assert.Contains("href=\"/jamsil-kids/roster\"", home);
        Assert.Contains("href=\"/jamsil-kids/roster?t=participant\"", home);
        Assert.Contains("김철수 <u>님</u>", home);                                    // 0076 인사 카드
        Assert.Contains("<small>나의 점수</small>", home);                            // R-190 성장: 0점 = 씨앗 0%, 새싹까지 400점
        Assert.Contains("<em class=\"nm\">씨앗 </em>0%", home);                       // 휴대폰에서는 이름을 숨기고 %만(2026-11-18)
        Assert.Contains("씨앗으로 자라는 중", home);
        Assert.Contains("<svg class=\"pea\"", home);                                  // 씨앗 그림 = 완두콩 꼬투리(2026-11-03), 🌰 이모지는 안 씀
        Assert.DoesNotContain("🌰", home);
        Assert.Contains("<small>새싹까지</small><b class=\"num\">400<u>점</u></b>", home);   // 오른쪽 "다음 단계까지 N점" 다시(2026-11-17 사용자, 유년부 홈처럼)
        Assert.DoesNotContain("class=\"hj-stg\"", home);                            // 씨앗 · 새싹 · 나무 · 숲 단계 띠는 지움(2026-11-17)
        Assert.DoesNotContain("도토리", home);
        Assert.Contains("href=\"/jamsil-kids/points\"", home);                      // 성장일지
        Assert.Contains("<a class=\"sx\" href=\"/jamsil-kids/reading/search\"", home);            // 말씀 찾기
        Assert.DoesNotContain("<nav class=\"tabs\"", home);                          // 0076: 아래 탭 · 이어서 하기 버튼 없음
        Assert.DoesNotContain("td-go", home);
        var word = await c.GetStringAsync("/jamsil-kids/word");                     // 0075 완료 창 "다음: 퀴즈 →"
        Assert.Contains("data-today=\"1\" data-next=\"/jamsil-kids/quiz\" data-next-label=\"퀴즈\"", word);
        Assert.Contains("href=\"/account\"", home);                                 // 확정 7: 계정 줄 3개(비밀번호 바꾸기는 내 계정 안으로)
        Assert.DoesNotContain("부서 바꾸기", home);                                  // 홈 맨 아래 "부서 바꾸기"는 뺌(2026-11-05)
        Assert.Contains("로그아웃</button>", home);
        Assert.DoesNotContain("▼", home);                                            // 확정 6: 소속이 하나면 ▼ 없음
        Assert.DoesNotContain(">관리</a>", home);                                    // 확정 1: 관리 버튼은 관리자만
    }

    [Fact]
    public async Task 통독을_시작하지_않았으면_홈은_회차를_열지_않는다()
    {
        var (c, me) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("<small>이어 읽을 곳</small><b>창세기</b><span>1장 1절 ›</span>", home);   // 확정 시안 홈-통독필사-0펼침: 시작 전에도 0 으로 펼침
        Assert.Contains("<b class=\"num\">0</b><small>읽은 장</small>", home);
        Assert.Contains("href=\"/jamsil-kids/reading\"", home);
        using var raw = InMemoryDb.Raw(_db);
        Assert.False(raw.ReadingRounds.Any(r => r.UserName == me));               // R-153: 회차는 통독 화면에서 처음 열린다
    }

    [Fact]
    public async Task 통독을_시작했으면_진도와_이어읽기가_보인다()
    {
        var (c, me) = await Member("cheolsu", Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var at = new DateTime(2027, 1, 4, 1, 0, 0, DateTimeKind.Utc);
            raw.ReadingRounds.Add(new ReadingRound { UserName = me, RoundNo = 1, StartedAt = at });
            for (short ch = 1; ch <= 3; ch++) raw.ReadingChapterReads.Add(new ReadingChapterRead { UserName = me, RoundNo = 1, BookId = 1, Chapter = ch, ReadAt = at.AddMinutes(ch) });
            raw.SaveChanges();
        }
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("1독째", home);
        Assert.Contains("<b class=\"num\">3</b><small>읽은 장</small>", home);
        Assert.Contains("<b class=\"num\">1,186</b><small>남은 장</small>", home);
        Assert.Contains("<a class=\"hj-box r\" href=\"/jamsil-kids/reading/read/1/4\">", home);   // 이어 읽을 곳 = 창세기 4장
        Assert.Contains("<b>창세기</b><span>4장", home);
        Assert.Contains("href=\"/jamsil-kids/reading\">📖 도전!!! 성경통독", home);   // 카드 제목 = 통독 홈
    }

    [Fact]
    public async Task 관리자는_머리말에_관리_링크가_있다()
    {
        var (c, _) = await Member("eunhye", Roles.Teacher, Roles.TenantAdmin);
        var home = await c.GetStringAsync("/jamsil-kids");   // 왼쪽 위 ☰ 메뉴판에 관리(맨 아래 계정 줄은 지웠다, 2026-11-15)
        Assert.Contains("class=\"tdr-r\" href=\"/jamsil-kids/admin\"", home); Assert.Contains("<b>관리</b>", home);
        Assert.DoesNotContain("class=\"adm\"", home);
    }

    [Fact]
    public async Task 말씀이_없는_날은_띠를_숨기고_카드에_알린다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.DailyWords.RemoveRange(raw.DailyWords);
            raw.DailyQuizzes.RemoveRange(raw.DailyQuizzes);
            raw.SaveChanges();
        }
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("aria-label=\"오늘의 말씀\"", home);                   // 확정 8
        Assert.Contains("오늘은 말씀이 없어요", home);
    }

    [Fact]
    public async Task 꺼진_프로그램_카드는_보이지_않는다()
    {
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(t => t.Id == _kids.Id).MissionEnabled = false; raw.SaveChanges(); }
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("🎯 도전!!! 성경미션", home);                          // 확정 5
        Assert.Contains("📖 도전!!! 성경통독", home);
    }

    [Fact]
    public async Task 소속이_둘이면_내_계정에서_부서를_고르고_홈_맨_아래에는_부서_바꾸기가_없다()
    {
        var (c, me) = await Member("cheolsu", Roles.Participant);
        var other = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        InMemoryDb.SeedMembership(_db, other.Id, me, MembershipStatus.Approved, Roles.Participant);
        Assert.DoesNotContain("부서 바꾸기", await c.GetStringAsync("/jamsil-kids"));                          // 홈 맨 아래 버튼은 뺌(2026-11-05)
        Assert.Contains("<a href=\"/account/tenants\">부서 고르기 →</a>", await c.GetStringAsync("/account"));   // 길은 내 계정에
    }

    [Fact]
    public async Task 오늘_참여가_카드에_반영되고_나는_오늘_순서는_더_이상_보이지_않는다()
    {
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/jamsil-kids/missions/word")
            { Content = System.Net.Http.Json.JsonContent.Create(new { date = "2027-01-05", items = new[] { "Word.Me" } }) };
        req.Headers.Add("RequestVerificationToken", TokenRx().Match(html).Groups[1].Value);
        (await c.SendAsync(req)).EnsureSuccessStatusCode();
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("<b class=\"num\">1<u>명</u></b><small>오늘 참여</small>", home);   // ADR 0085: "나는 오늘 N번째" 칸은 뺐다
        Assert.DoesNotContain("번째", home);
        Assert.Contains("<b class=\"num\">1/2</b>오늘 미션", home);
        Assert.DoesNotContain("class=\"hj-m4\"", home);                                // 미션 네 칸은 지웠다(2026-11-18) — 미션 홈에서 한다
    }

    [Fact]   // R-336
    public async Task R336_로그인_안_한_사람은_개인_정보_없는_부서_홈_둘러보기를_본다()
    {
        await Member("cheolsu", Roles.Participant);                                    // 부서에 "김철수"가 있다
        var anon = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await anon.GetAsync("/jamsil-kids");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);                                 // 로그인으로 보내지 않는다
        var html = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("gs-band", html);                                         // 위 띠는 지웠다(2026-11-18 사용자)
        Assert.Contains("손님", html);
        Assert.Contains("src=\"/images/guest.webp\"", html);                          // 사진 자리: 유년부 사람 그림(2026-11-18)
        Assert.Contains("class=\"hj-card hj-pg m\"", html);                           // 같은 카드
        Assert.Contains(">0/", html);                                                   // 오늘 미션 0/N
        Assert.DoesNotContain("김철수", html);                                          // 이름 · 점수 · 선물 받는 아이 이름 없음
        Assert.Contains("받은 친구 이름은 로그인하면 보여요", html);                   // 선물 받는 아이 이름은 숨김
        Assert.DoesNotContain("data-pnav-open", html);                                 // ☰ 메뉴 없음
        Assert.Contains("/jamsil-kids/feedback", html);                                // 개선 · 불편 접수(2026-11-18 사용자) → 누르면 로그인
        var fb = await anon.GetAsync("/jamsil-kids/feedback");
        Assert.Contains("/account/login", fb.Headers.Location!.OriginalString);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await anon.GetAsync("/jamsil-kids/word-mission")).StatusCode);   // 다른 부서 화면은 그대로 로그인
        Assert.NotEqual(System.Net.HttpStatusCode.OK, (await anon.PostAsync("/jamsil-kids?handler=SnoozeInvites", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await anon.GetAsync("/no-such-dept")).StatusCode);
    }

    [Fact]   // R-337
    public async Task R337_관리자가_예배_안내를_넣으면_부서_홈_말씀_찾기_위에_보이고_손님도_본다()
    {
        var (admin, _) = await Member("eunhye", Roles.Teacher, Roles.TenantAdmin);
        Assert.DoesNotContain("aria-label=\"예배 안내\"", await admin.GetStringAsync("/jamsil-kids"));   // 없으면 카드 없음
        var page = await admin.GetStringAsync("/jamsil-kids/admin/worship");
        Assert.Contains("+ 예배 안내 더하기", page);
        var r = await admin.PostAsync("/jamsil-kids/admin/worship?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value,
            ["dayOfWeek"] = "0", ["startTime"] = "10:00", ["place"] = "비전센터 믿음홀(3F)", ["address"] = "서울특별시 송파구 석촌호수로 102", ["sortOrder"] = "1", ["active"] = "true",
        }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, r.StatusCode);
        var home = await admin.GetStringAsync("/jamsil-kids");
        Assert.Contains("매주 주일 오전 10시", home);
        Assert.Contains("비전센터 믿음홀(3F)", home);
        Assert.True(home.IndexOf("aria-label=\"예배 안내\"") < home.IndexOf("aria-label=\"말씀 찾기\""));   // 말씀 찾기 바로 위
        Assert.DoesNotContain("주보 보기", home);                                                  // 주보 링크가 없으면 버튼 없음
        var guest = await _app.CreateClient().GetStringAsync("/jamsil-kids");
        Assert.Contains("비전센터 믿음홀(3F)", guest);                                             // 로그인 안 한 사람도 본다

        var (kid, _) = await Member("cheolsu", Roles.Participant);
        Assert.NotEqual(System.Net.HttpStatusCode.OK, (await kid.GetAsync("/jamsil-kids/admin/worship")).StatusCode);   // 관리자만 고친다
    }
}
