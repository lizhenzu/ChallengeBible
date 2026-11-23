using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 부서 홈(2026-11-22 사용자 확정, ADR 0127) — dodan 화면 하나:
/// 머리말 · 로고 · 검색 · 프로그램 4칸 · 오늘의 말씀 · 나의 도전 현황.
/// 오늘 = 2027-01-05 (서울), 말씀 · 퀴즈만 있는 날.
/// </summary>
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
        InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, roles);
        return (c, id);
    }

    [Fact]
    public async Task 참여자_홈은_여섯_블록만_보이고_옛_카드는_없다()
    {
        ContentSeed.Prayer(_db, Jan5);
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");

        Assert.Contains("<span class=\"bt-1\">도전<span class=\"bt-ex\">", home);   // 로고(느낌표 3색)
        Assert.Contains("<span class=\"bt-2\">성경말씀</span>", home);
        Assert.Contains("하루 5분, 말씀과 함께 자라요.", home);                          // 태그라인
        Assert.Contains("<form class=\"hmx-search\" method=\"get\" action=\"/jamsil-kids/reading/search\"", home);   // 검색
        Assert.Contains("name=\"q\"", home);
        Assert.Contains("<button type=\"submit\">검색</button>", home);
        Assert.Contains("<a class=\"vb-item\" href=\"/jamsil-kids/word-mission\"", home);      // 프로그램 목록 중 미션
        Assert.Contains("<a class=\"vb-item\" href=\"/jamsil-kids/reading\"", home);           // 통독
        Assert.Contains("<a class=\"vb-item\" href=\"/jamsil-kids/copy\"", home);              // 필사
        Assert.DoesNotContain("href=\"/jamsil-kids/prayer365\"", home);                          // 학생에게는 365 기도 없음(ADR 0106)
        Assert.Contains("aria-label=\"오늘의 말씀\"", home);                                     // 말씀 카드
        Assert.DoesNotContain("나의 도전 현황", home);                                   // 카드는 「나의 도전」 화면으로 옮겼다(ADR 0136)
        Assert.Contains("href=\"/jamsil-kids/challenge\"", home);                        // 홈 상단 포인트 링(ADR 0136)
        Assert.Contains("class=\"vb-ring\"", home);
        var ch = await c.GetStringAsync("/jamsil-kids/challenge");
        Assert.Contains("나의 도전", ch);
        Assert.Contains(">오늘 미션<", ch);
        Assert.Contains("<b class=\"num\">0/3</b>", ch);                                // 말씀 · 퀴즈 · 기도 세 가지
        Assert.Contains("<span>전체 참여자</span>", ch);

        // 옛 홈 블록 — 2026-11-22 사용자 확정으로 뺐다.
        Assert.DoesNotContain("dz-hero", home);
        Assert.DoesNotContain("aria-label=\"예배 안내\"", home);
        Assert.DoesNotContain("누가 선물 받나요", home);
        Assert.DoesNotContain("가족팀 초대가 왔어요", home);
        Assert.DoesNotContain("개선사항 · 불편사항 접수", home);
        Assert.DoesNotContain("나의 점수</small>", home);
        Assert.DoesNotContain("class=\"hj-card", home);
        Assert.DoesNotContain("class=\"dz-card", home);

        Assert.Contains("href=\"/jamsil-kids/feedback\"", home);                                // 의견은 작은 링크로
        Assert.Contains("data-pnav-open", home);                                                // ☰
        Assert.Contains("class=\"mailb\" href=\"/messages\"", home);                          // 우측 편지 = 받은 쪽지
        Assert.DoesNotContain("<a class=\"hmx-ico\" href=\"/jamsil-kids/reading/search\"", home);   // 우측 검색 아이콘은 뺐다(검색은 아래 입력+버튼)
        Assert.Contains("로그아웃", home);
    }

    [Fact]
    public async Task 통독을_시작하지_않았으면_홈은_회차를_열지_않는다()
    {
        var (c, me) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("href=\"/jamsil-kids/reading\"", home);
        Assert.DoesNotContain("이어 읽을 곳", home);                       // 진행 카드는 홈에서 뺐다
        using var raw = InMemoryDb.Raw(_db);
        Assert.False(raw.ReadingRounds.Any(r => r.UserName == me));       // R-153: 회차는 통독 화면에서 처음 열린다
    }

    [Fact]
    public async Task 교사_홈은_365기도_아이콘과_관리_메뉴를_보인다()
    {
        var (c, _) = await Member("eunhye", Roles.Teacher, Roles.TenantAdmin);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("<a class=\"vb-item\" href=\"/jamsil-kids/prayer365\"", home);
        Assert.Contains("<h2 class=\"tdr2-g\">관리</h2>", home);
        Assert.Contains("class=\"tdr2-r\" href=\"/jamsil-kids/admin\"", home);
        Assert.Contains("<em class=\"tdr2-k\">관리</em>", home);
    }

    [Fact]
    public async Task 말씀이_없는_날은_말씀_카드를_숨긴다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.DailyWords.RemoveRange(raw.DailyWords);
            raw.DailyQuizzes.RemoveRange(raw.DailyQuizzes);
            raw.SaveChanges();
        }
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("aria-label=\"오늘의 말씀\"", home);
        Assert.DoesNotContain("오늘은 말씀이 없어요", home);      // 빈 카드도 두지 않는다
        Assert.Contains("href=\"/jamsil-kids/challenge\"", home);   // 나머지 화면은 그대로
    }

    [Fact]
    public async Task 꺼진_프로그램_아이콘은_보이지_않는다()
    {
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(t => t.Id == _kids.Id).MissionEnabled = false; raw.SaveChanges(); }
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("href=\"/jamsil-kids/word-mission\"", home);   // 미션 아이콘 · 오늘의 말씀 메뉴 모두 숨김
        Assert.Contains("href=\"/jamsil-kids/reading\"", home);
        Assert.DoesNotContain("오늘 미션</div>", home);                       // 현황에서도 뺀다
    }

    [Fact]   // ADR 0133 — 통합 카드: 프로그램 진행 줄
    public async Task 나의_도전_현황_카드에_통독_필사_진행_줄이_보인다()
    {
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var page = await c.GetStringAsync("/jamsil-kids/challenge");            // 카드는 이 화면(ADR 0136)
        Assert.Contains("class=\"vb-chips\"", page);                          // 오늘 미션 칸
        Assert.Contains("class=\"vb-chip wait\"", page);                      // 아직 안 한 칸 = 대기중(옅은 초록 바탕)
        Assert.Contains("<span>대기중</span>", page);                          // 체크 표시 대신 글자
        Assert.Contains("class=\"vb-prog\"", page);                           // 진행 줄
        Assert.Contains("성경통독", page);                                     // 통독 줄
        Assert.Contains("성경필사", page);                                     // 필사 줄
        Assert.Contains("시작 전", page);                                      // 아직 시작하지 않음
        Assert.Contains("시작하기 ›", page);
        Assert.DoesNotContain("기도365</span>", page);                         // 학생에게는 365 기도 줄 없음(ADR 0106)
        Assert.Contains("class=\"vb-ch-hero\"", page);                        // A안 링 히어로(ADR 0136)
        Assert.Contains("class=\"vb-ch-week\"", page);                        // B안 「이번 주」 막대
        Assert.DoesNotContain("vb-cardfoot", page);                            // 순위 · 연속은 뺐다(ADR 0135)
        Assert.DoesNotContain("선물 대상자와 상태", page);                       // 선물은 이 화면에 없다
        Assert.DoesNotContain("우리 가족팀", page);                             // 가족팀도 이 화면에 없다
    }

    [Fact]   // ADR 0133 — 교사는 365 기도 줄이 함께 보인다
    public async Task 교사_나의_도전_현황_카드에는_기도365_줄도_있다()
    {
        ContentSeed.Prayer(_db, Jan5);
        var (c, _) = await Member("eunhye", Roles.Teacher);
        var page = await c.GetStringAsync("/jamsil-kids/challenge");
        Assert.Contains(">기도365</span>", page);
        Assert.Contains(" / 366일</span>", page);
    }

    [Fact]
    public async Task 소속이_둘이면_내_계정에서_부서를_고르고_홈_맨_아래에는_부서_바꾸기가_없다()
    {
        var (c, me) = await Member("cheolsu", Roles.Participant);
        var other = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        InMemoryDb.SeedMembership(_db, other.Id, me, MembershipStatus.Approved, Roles.Participant);
        Assert.DoesNotContain("부서 바꾸기", await c.GetStringAsync("/jamsil-kids"));
        Assert.Contains("<a href=\"/account/tenants\">부서 고르기 →</a>", await c.GetStringAsync("/account"));
    }

    [Fact]
    public async Task 오늘_미션과_이번주_참여가_현황_카드에_반영된다()
    {
        ContentSeed.Prayer(_db, Jan5);
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var html = await c.GetStringAsync("/jamsil-kids/word");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/jamsil-kids/missions/word")
            { Content = System.Net.Http.Json.JsonContent.Create(new { date = "2027-01-05", items = new[] { "Word.Me" } }) };
        req.Headers.Add("RequestVerificationToken", TokenRx().Match(html).Groups[1].Value);
        (await c.SendAsync(req)).EnsureSuccessStatusCode();
        var home = await c.GetStringAsync("/jamsil-kids/challenge");
        Assert.Contains("<b class=\"num\">1/3</b>", home);
        Assert.Contains("<b class=\"num\">1회</b>", home);
        Assert.DoesNotContain("번째", home);
    }

    [Fact]
    public async Task 머리말_편지는_받은_쪽지로_가고_안_읽은_수가_배지로_보인다()
    {
        var (kid, kidId) = await Member("cheolsu", Roles.Participant);
        var before = await kid.GetStringAsync("/jamsil-kids");
        Assert.Contains("class=\"mailb\" href=\"/messages\"", before);
        Assert.DoesNotContain("<i class=\"num\">", before);      // 안 읽은 쪽지 0 — 숫자 없음

        var (_, momId) = await Member("mom", Roles.Guardian);
        using (var scope = _app.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ChallengeBible.Web.Services.MessageService>();
            Assert.Equal(ChallengeBible.Web.Services.MsgError.None, await svc.SendAsync(momId, kidId, "안녕", null, CancellationToken.None));
        }
        var after = await kid.GetStringAsync("/jamsil-kids");
        Assert.Contains("<i class=\"num\">1</i>", after);        // 안 읽은 1개
        Assert.Contains("href=\"/messages\"", after);            // 받은 쪽지 탭으로
    }

    [Fact]
    public async Task 가족팀도_초대도_없으면_만들기_카드가_보인다()
    {
        var (kid, _) = await Member("cheolsu", Roles.Participant);
        var home = await kid.GetStringAsync("/jamsil-kids");
        Assert.Contains("가족팀 · 친구팀을 만들어 보세요", home);
        Assert.Contains(">가족팀 만들기</a>", home);
        Assert.Contains(">친구팀 만들기</a>", home);        // 학생

        var (mom, _) = await Member("mom", Roles.Guardian);
        var mh = await mom.GetStringAsync("/jamsil-kids");
        Assert.Contains(">가족팀 만들기</a>", mh);
        Assert.DoesNotContain(">친구팀 만들기</a>", mh);   // 학부모에게는 친구팀 없음

        var (teacher, _) = await Member("teacher", Roles.Teacher);
        Assert.DoesNotContain("가족팀 · 친구팀을 만들어 보세요", await teacher.GetStringAsync("/jamsil-kids"));   // 교사는 가족팀 대상 아님
    }

    [Fact]   // R-336
    public async Task R336_로그인_안_한_사람은_로그인_안내가_있는_부서_홈을_본다()
    {
        await Member("cheolsu", Roles.Participant);
        var anon = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await anon.GetAsync("/jamsil-kids");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);              // 로그인으로 보내지 않는다
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("<span class=\"bt-2\">성경말씀</span>", html);
        Assert.DoesNotContain("class=\"vb-ring\"", html);                      // 손님에게는 포인트 링이 없다
        Assert.DoesNotContain("나의 포인트", html);
        Assert.DoesNotContain("김철수", html);
        Assert.DoesNotContain("data-pnav-open", html);                          // ☰ 메뉴 없음
        Assert.DoesNotContain("class=\"mailb\"", html);                        // 로그인 전에는 쪽지함이 없다
        Assert.Contains("href=\"/jamsil-kids/feedback\"", html);
        var fb = await anon.GetAsync("/jamsil-kids/feedback");
        Assert.Contains("/account/login", fb.Headers.Location!.OriginalString);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await anon.GetAsync("/jamsil-kids/word-mission")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await anon.GetAsync("/no-such-dept")).StatusCode);
    }

    [Fact]   // R-337 — 화면은 남지만 홈 카드는 2026-11-22 확정으로 뺐다
    public async Task R337_예배_안내는_홈에_보이지_않는다()
    {
        var (admin, _) = await Member("eunhye", Roles.Teacher, Roles.TenantAdmin);
        var page = await admin.GetStringAsync("/jamsil-kids/admin/worship");
        var r = await admin.PostAsync("/jamsil-kids/admin/worship?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value,
            ["dayOfWeek"] = "0", ["startTime"] = "10:00", ["place"] = "비전센터 믿음홀(3F)", ["address"] = "서울특별시 송파구 석촌호수로 102", ["sortOrder"] = "1", ["active"] = "true",
        }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, r.StatusCode);
        var home = admin.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("비전센터 믿음홀(3F)", await home);       // 홈에는 더 이상 예배 카드가 없다
        Assert.DoesNotContain("주보 보기", await home);

        var (kid, _) = await Member("cheolsu", Roles.Participant);
        Assert.NotEqual(System.Net.HttpStatusCode.OK, (await kid.GetAsync("/jamsil-kids/admin/worship")).StatusCode);   // 관리자만 고친다
    }

    [Fact]
    public async Task 부서_메뉴판은_섬김교사_생일_함께하기_관리_순서다()
    {
        // 2026-11-22 2차 확정: 프로그램 · 바로가기 그룹은 뺐다(부서 홈 아이콘 · 홈 카드로 간다).
        string[] Order(string home) => new[] { "섬김 · 교사", "생일", "함께하기", "관리" }
            .Select(g => home.IndexOf($"<h2 class=\"tdr2-g\">{g}</h2>", StringComparison.Ordinal).ToString()).ToArray();
        var (admin, _) = await Member("eunhye", Roles.Teacher, Roles.TenantAdmin);
        var html = await admin.GetStringAsync("/jamsil-kids");
        var o = Order(System.Net.WebUtility.HtmlDecode(html)).Select(int.Parse).ToArray();
        Assert.True(o.All(x => x >= 0), string.Join(",", o));
        Assert.True(o.SequenceEqual(o.Order()), string.Join(",", o));   // 섬김 · 교사 → 생일 → 함께하기 → 관리
        Assert.DoesNotContain("<h2 class=\"tdr2-g\">프로그램</h2>", html);
        Assert.DoesNotContain("<h2 class=\"tdr2-g\">바로가기</h2>", html);

        var (kid, _) = await Member("cheolsu", Roles.Participant);
        var k = Order(System.Net.WebUtility.HtmlDecode(await kid.GetStringAsync("/jamsil-kids"))).Select(int.Parse).ToArray();
        Assert.Equal(-1, k[0]);   // 학생은 섬김 · 교사 없음
        Assert.Equal(-1, k[3]);   // 관리 없음
        Assert.Contains("href=\"/jamsil-kids/family-team\"", await kid.GetStringAsync("/jamsil-kids"));   // 가족팀은 메뉴로 이동
    }

    [Fact]   // ADR 0136 — 첫 화면(/) 상단에도 포인트 링
    public async Task 첫화면_상단에_포인트_링이_보이고_나의_도전으로_간다()
    {
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var root = await c.GetStringAsync("/");
        Assert.Contains("class=\"vb-ring\"", root);                          // 첫 화면 상단 포인트 링
        Assert.Contains("href=\"/jamsil-kids/challenge\"", root);            // 누르면 「나의 도전」
        Assert.Contains("<span class=\"rb-stg\">", root);   // site.css .stg 와 겹치지 않게 rb- 접두사                       // 링 안 성장 단계
        var anon = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.DoesNotContain("class=\"vb-ring\"", await anon.GetStringAsync("/"));   // 손님에게는 없다
    }
}
