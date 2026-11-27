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

        Assert.Contains("<span>도전<i class=\"dh-e1\">!</i><i class=\"dh-e2\">!</i><i class=\"dh-e3\">!</i></span>", home);   // 로고(느낌표 3색)
        Assert.Contains("<span>성경말씀</span>", home);
        Assert.Contains("하루 5분, 말씀과 함께 자라요.", home);                          // 태그라인
        Assert.Contains("<form class=\"dh-search\" method=\"get\" action=\"/jamsil-kids/reading/search\"", home);   // 검색
        Assert.Contains("name=\"q\"", home);
        Assert.Contains("<button type=\"submit\">검색</button>", home);
        Assert.Contains("<a class=\"dh-item dh-k1\" href=\"/jamsil-kids/word-mission\"", home);      // 프로그램 목록 중 미션
        Assert.Contains("<a class=\"dh-item dh-k2\" href=\"/jamsil-kids/reading\"", home);           // 통독
        Assert.Contains("<a class=\"dh-item dh-k3\" href=\"/jamsil-kids/copy\"", home);              // 필사
        Assert.DoesNotContain("href=\"/jamsil-kids/prayer365\"", home);                          // 학생에게는 365축복기도 없음(ADR 0106)
        Assert.DoesNotContain("aria-label=\"오늘의 말씀\"", home);                              // 말씀 카드는 2026-11-26 사용자 요청으로 영구 삭제
        Assert.DoesNotContain("나의 도전 현황", home);                                   // 카드는 「나의 도전」 화면으로 옮겼다(ADR 0136)
        Assert.Contains("href=\"/jamsil-kids/challenge\"", home);                        // 홈 상단 포인트 링(ADR 0136)
        Assert.Contains("class=\"dh-ring\"", home);
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
        Assert.Contains("개선사항 · 불편사항 접수", home);                                    // 맨 아래 의견 접수 카드(2026-11-27 새 퍼블리시본)
        Assert.DoesNotContain("나의 점수</small>", home);
        Assert.DoesNotContain("class=\"hj-card", home);
        Assert.DoesNotContain("class=\"dz-card", home);

        Assert.Contains("href=\"/jamsil-kids/feedback\"", home);                                // 의견은 작은 링크로
        Assert.DoesNotContain("data-pnav-open", home);                                          // 홈 왼쪽 위 ☰ 는 삭제(2026-11-26 사용자, 나중에 다시 검토)
        Assert.Contains("<small>대한예수교장로회</small><span>잠실새내교회</span>", home);               // 왼쪽 위 문구는 첫 화면과 같다
        Assert.Contains("class=\"mailb\" href=\"/messages\"", home);                          // 우측 편지 = 받은 쪽지
        Assert.DoesNotContain("<a class=\"hmx-ico\" href=\"/jamsil-kids/reading/search\"", home);   // 우측 검색 아이콘은 뺐다(검색은 아래 입력+버튼)
        Assert.Contains("class=\"tdr2-prof\"", home);   // 로그아웃 줄은 ☰ 에서 뺐다(내 계정 화면에서, 2026-11-25)
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
    public async Task 교사_홈은_365축복기도_아이콘과_관리_메뉴를_보인다()
    {
        var (c, _) = await Member("eunhye", Roles.Teacher, Roles.TenantAdmin);
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("<a class=\"dh-item dh-k4\" href=\"/jamsil-kids/prayer365\"", home);
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
        Assert.Contains("class=\"vb-chip k-word wait\"", page);               // 아직 안 한 칸 = 대기중(그 프로그램 색의 옅은 바탕, 2026-11-24 시안 나의도전-정리)
        Assert.Contains("vb-chip k-quiz wait", page);
        Assert.Contains("vb-chip k-prayer", page);   // 콘텐츠가 없으면 없음 칸이지만 종류 클래스는 붙는다
        Assert.Contains("vb-chip k-recite", page);
        Assert.Contains("<span>대기중</span>", page);                          // 체크 표시 대신 글자
        Assert.Contains("class=\"vb-prog\"", page);                           // 진행 줄
        Assert.Contains("성경통독", page);                                     // 통독 줄
        Assert.Contains("성경필사", page);                                     // 필사 줄
        Assert.Contains("시작 전", page);                                      // 아직 시작하지 않음
        Assert.Contains("<a class=\"go\" href=\"/jamsil-kids/word-mission\" aria-label=\"성경미션 들어가기\">들어가기 ›</a>", page);   // 오늘 미션 카드 제목 줄 오른쪽(2026-11-24 A안)
        Assert.DoesNotContain("시작하기 ›", page);                              // 2026-11-24 사용자: 「시작하기」가 아니라 항상 「들어가기」
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(page, "<b class=\"go\">들어가기 ›</b>").Count);   // 학생: 통독 · 필사 두 줄 모두(시작 전이든 아니든)
        Assert.DoesNotContain("365축복기도</span>", page);                         // 학생에게는 365축복기도 줄 없음(ADR 0106)
        Assert.Contains("class=\"vb-ch-hero\"", page);                        // A안 링 히어로(ADR 0136)
        Assert.Contains("class=\"vb-ch-week empty\"", page);                  // B안 「이번 주」 막대 — 이번 주 기록이 없으면 낮은 모양(2026-11-24)
        Assert.Matches("<i class=\"[^\"]*\" style=\"--h:[0-9.]+\"></i>", page);   // 막대 높이는 --h 로(예전 height:N% 는 격자 안에서 0px 가 돼 막대가 안 그려졌다 · 2026-11-24)
        Assert.DoesNotContain("style=\"height:", page);
        Assert.Contains("다음 단계(새싹)까지", page);                          // 「400점까지 400점」이 아니라 다음 단계 이름으로
        Assert.DoesNotContain("class=\"start\"", page);                       // 시작 전 줄도 다른 줄과 같은 모양(막대 + 숫자 줄)
        Assert.DoesNotContain("vb-cardfoot", page);                            // 순위 · 연속은 뺐다(ADR 0135)
        Assert.DoesNotContain("선물 대상자와 상태", page);                       // 선물은 이 화면에 없다
        Assert.DoesNotContain("우리 가족팀", page);                             // 가족팀도 이 화면에 없다
    }

    [Fact]   // ADR 0133 — 교사는 365축복기도 줄이 함께 보인다
    public async Task 교사_나의_도전_현황_카드에는_365축복기도_줄도_있다()
    {
        ContentSeed.Prayer(_db, Jan5);
        var (c, _) = await Member("eunhye", Roles.Teacher);
        var page = await c.GetStringAsync("/jamsil-kids/challenge");
        Assert.Contains(">365축복기도</span>", page);
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
    public async Task 가족팀_만들기_카드는_홈이_아니라_성경미션_화면에_있다()
    {
        var (kid, _) = await Member("cheolsu", Roles.Participant);
        var home = await kid.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("가족팀 · 친구팀을 만들어 보세요", home);   // 가족팀 카드는 홈에서 뺐다(2026-11-27 사용자) — 성경미션 화면(/word-mission)으로 옮긴다
        Assert.Contains("우리 가족팀 만들기", await kid.GetStringAsync("/jamsil-kids/word-mission"));

        var (mom, _) = await Member("mom", Roles.Guardian);
        var mh = await mom.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("가족팀 · 친구팀을 만들어 보세요", mh);

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
        Assert.Contains("<span>성경말씀</span>", html);
        Assert.Contains("class=\"dh-ring\"", html);                             // 손님에게도 포인트 링을 0점으로 보여 준다(2026-11-27 사용자)
        Assert.Contains("나의 포인트 0점", html);                                   // 링의 안내 문구도 0점(2026-11-27 사용자)
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
        // 2026-11-25: 「섬김 · 교사」 그룹은 「행정업무」 한 줄(href=/office)로 합쳤다 — 순서: 행정업무 줄 → 생일 → 함께하기 → 관리
        string[] Order(string home) => new[] { "<a class=\"tdr2-r\" href=\"/jamsil-kids/office\"", "<h2 class=\"tdr2-g\">이벤트</h2>", "<h2 class=\"tdr2-g\">함께하기</h2>", "<h2 class=\"tdr2-g\">관리</h2>" }
            .Select(g => home.IndexOf(g, StringComparison.Ordinal).ToString()).ToArray();
        var (admin, _) = await Member("eunhye", Roles.Teacher, Roles.TenantAdmin);
        var html = await admin.GetStringAsync("/jamsil-kids");
        var o = Order(System.Net.WebUtility.HtmlDecode(html)).Select(int.Parse).ToArray();
        Assert.True(o.All(x => x >= 0), string.Join(",", o));
        Assert.True(o.SequenceEqual(o.Order()), string.Join(",", o));   // 행정업무 → 생일 → 함께하기 → 관리
        Assert.DoesNotContain("<h2 class=\"tdr2-g\">프로그램</h2>", html);
        Assert.DoesNotContain("<h2 class=\"tdr2-g\">바로가기</h2>", html);

        var (kid, _) = await Member("cheolsu", Roles.Participant);
        var k = Order(System.Net.WebUtility.HtmlDecode(await kid.GetStringAsync("/jamsil-kids"))).Select(int.Parse).ToArray();
        Assert.Equal(-1, k[0]);   // 학생은 섬김 · 교사 없음
        Assert.Equal(-1, k[3]);   // 관리 없음
        Assert.Contains("href=\"/jamsil-kids/family-team\"", await kid.GetStringAsync("/jamsil-kids"));   // 가족팀은 메뉴로 이동
    }

    [Fact]   // R-372 — 손님의 부서 홈: 인사말 · 포인트 링 · 편지 없이 사람 아이콘(로그인), 검색과 4가지 도전은 로그인 창으로
    public async Task R372_손님의_부서_홈은_사람_아이콘이고_검색과_도전은_로그인으로_간다()
    {
        var anon = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var home = await anon.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("class=\"dh-hi\"", home);                         // 「이은혜님」 인사말 없음
        Assert.Contains("class=\"dh-ring\"", home);                              // 포인트 링은 「0점 · 첫 단계」로(2026-11-27 사용자)
        Assert.Contains("0<i>점</i>", home);
        Assert.Contains("href=\"/account/login?returnUrl=%2Fjamsil-kids%2Fchallenge\"", home);   // 누르면 로그인
        Assert.DoesNotContain("class=\"mailb\"", home);                         // 편지 아이콘 없음
        Assert.Contains("class=\"dh-circ\" href=\"/account/login", home);     // 사람 아이콘 = 로그인
        Assert.Contains("data-guest=\"true\"", home);                                     // 검색은 로그인 창으로(스크립트)
        Assert.Contains("href=\"/account/login?returnUrl=%2Fjamsil-kids%2Fword-mission\"", home);   // 4가지 도전도 로그인 창으로
        Assert.Contains("4가지 도전, 하나의 말씀.", home);                          // 설명 두 줄
    }

    [Fact]   // ADR 0136 — 첫 화면(/) 상단에도 포인트 링
    public async Task 첫화면_상단에_포인트_링이_보이고_나의_도전으로_간다()
    {
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var root = await c.GetStringAsync("/?home=1");
        Assert.Contains("class=\"vb-ring\"", root);                          // 첫 화면 상단 포인트 링
        Assert.Contains("href=\"/jamsil-kids/challenge\"", root);            // 누르면 「나의 도전」
        Assert.Contains("<span class=\"rb-stg\">", root);   // site.css .stg 와 겹치지 않게 rb- 접두사                       // 링 안 성장 단계
        var anon = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.DoesNotContain("class=\"vb-ring\"", await anon.GetStringAsync("/"));   // 손님에게는 없다
    }

    [Fact]   // 머리줄 표준(2026-11-24 확정 · 기준표 5판): 오른쪽 「쪽지 | 이름님」 한 덩어리 알약 · 날짜는 본문 위 · 첫 화면은 교단 문구 유지
    public async Task 머리줄은_한_덩어리_알약이고_날짜는_본문_위이며_첫_화면은_교단_문구가_남는다()
    {
        var (c, _) = await Member("cheolsu", Roles.Participant);
        var root = await c.GetStringAsync("/?home=1");
        Assert.Contains("<small>대한예수교장로회</small>", root);            // 이단이 많아 첫 화면에는 교단 문구를 둔다(2026-11-24 사용자)
        Assert.Contains("class=\"vb-dc\"", root);                           // 오른쪽 위: 날짜 + 검은 동그라미(2026-11-25 사용자)
        Assert.Contains("class=\"mailb\"", root);                           // 동그라미 안의 편지(누르면 쪽지)
        Assert.Contains("class=\"vb-hi\"", root);                           // 인사말 「안녕하세요. 000님,」
        Assert.DoesNotContain("class=\"vb-today\"", root);                  // 제목 위 날짜는 지움
        Assert.DoesNotContain("class=\"vb-date\"", root);

        var dept = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("class=\"dh-top-r\"", dept);                          // 오른쪽 위: 날짜 + 편지 동그라미(2026-11-25 사용자)
        Assert.Contains("class=\"dh-hi\"", dept);                          // 인사말 「안녕하세요. 000님,」
        Assert.DoesNotContain("class=\"vb-today\"", dept);
        Assert.DoesNotContain("class=\"vb-date\"", dept);
        Assert.Contains("<small>대한예수교장로회</small>", dept);              // 부서 홈 왼쪽 위도 첫 화면과 같은 「대한예수교장로회 / 잠실새내교회」(2026-11-26 사용자)
        Assert.Contains("href=\"https://www.saenae.org/\" target=\"_blank\" rel=\"noopener\"", dept);   // 교회 이름은 퍼블리시본처럼 교회 홈페이지(새 창) 링크(2026-11-27 사용자)
        Assert.Contains("class=\"dh-foot\"", dept);                         // 맨 아래 문구는 첫 화면과 같은 모양(가운데 · 앱으로 설치)
        Assert.Contains("href=\"/install\">설치</a>", dept);
        Assert.DoesNotContain("class=\"foot-copy\"", dept);
    }
}
