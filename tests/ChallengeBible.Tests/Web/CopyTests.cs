using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 도전!!!성경필사(ADR 0099, 확정 시안 성경필사, R-305 ~ R-308): 부서가 켜면 생기는 세 번째 프로그램. 본문을 따라 쳐서 한 절씩 채우고(글자만 비교),
/// 같은 절은 번역이 달라도 한 번, 학생이 신약 · 구약을 다 쓰면 완필 선물. 시험 성경: 창세기 1장 2절(구약) · 요한복음 3장 2절 · 요한계시록 22장 1절(신약). 오늘 = 2027-01-05(서울).
/// </summary>
public partial class CopyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly string _kid, _teacher;

    public CopyTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        _kid = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "dongseok@example.com", "권동석").UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        _teacher = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호").UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin).UserName;
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        void Ch(byte book, short ch, params string[] krv)
        {
            raw.BibleChapters.Add(new BibleChapter { BookId = book, Chapter = ch, VerseCount = (short)krv.Length });
            for (short v = 1; v <= krv.Length; v++)
            {
                raw.BibleVerses.Add(new BibleVerse { TranslationId = 1, BookId = book, Chapter = ch, Verse = v, Text = krv[v - 1] });
                raw.BibleVerses.Add(new BibleVerse { TranslationId = 2, BookId = book, Chapter = ch, Verse = v, Text = $"쉬운 글 {book} {ch} {v}" });
                raw.BibleVerses.Add(new BibleVerse { TranslationId = 3, BookId = book, Chapter = ch, Verse = v, Text = $"Easy Text, {book}:{ch}:{v}!" });
            }
        }
        Ch(1, 1, "태초에 시험이 있으니라.", "둘째 절이라");
        Ch(43, 3, "하나님이 세상을 이처럼 사랑하사, 시험하시니라.", "\"믿는 자\"는 기뻐하리라!");
        Ch(66, 22, "아멘 주 예수여 오시옵소서.");
        raw.GiftItems.Add(new GiftItem { Name = "필사 노트", Price = 9000, CoupangUrl = "https://www.coupang.com/vp/products/1", Status = GiftItemStatus.Active });
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private void TurnOn(bool on = true)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.Tenants.Single(t => t.Id == _kids.Id).CopyEnabled = on;
        raw.SaveChanges();
    }

    private static async Task<JsonElement> Save(HttpClient c, byte book, short ch, short v, string tr, string typed)
    {
        var page = await c.GetStringAsync($"/kids/copy/write/{book}/{ch}");
        var res = await c.PostAsync($"/kids/copy/write/{book}/{ch}?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["book"] = book.ToString(), ["chapter"] = ch.ToString(), ["verse"] = v.ToString(),
            ["translation"] = tr, ["typed"] = typed,
        }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    private List<CopyVerse> Verses(string user) { using var raw = InMemoryDb.Raw(_db); return raw.CopyVerses.Where(v => v.UserName == user).ToList(); }

    [Fact]
    public async Task R308_필사는_기본으로_켜져_있고_끄면_화면_카드_메뉴가_없다()
    {
        using (var raw = InMemoryDb.Raw(_db)) Assert.True(raw.Tenants.Single(t => t.Id == _kids.Id).CopyEnabled);   // 기본 켬(2026-11-16 사용자)
        TurnOn(false);                                                                                              // 만일을 위해 끌 수 있다
        var c = await Login("dongseok@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/kids/copy")).StatusCode);
        Assert.DoesNotContain("/kids/copy", await c.GetStringAsync("/kids"));

        TurnOn();
        var home = await c.GetStringAsync("/kids");
        Assert.Contains("도전!!! 성경필사", home);
        Assert.Contains("<a href=\"/kids/copy\"><em class=\"pd-pg\">도전!!!</em> 성경필사</a>", home);   // ☰ 메뉴판
        Assert.Contains("<a href=\"/kids/word-mission\"><em class=\"pd-pg\">도전!!!</em> 성경미션</a>", home);   // 세 프로그램 모두(2026-11-16)
        Assert.Contains("<a href=\"/kids/reading\"><em class=\"pd-pg\">도전!!!</em> 성경통독</a>", home);
        Assert.Contains("<small>이어 쓸 곳</small><b>창세기</b><span>1장 1절부터 ›</span>", home);   // 확정 시안 홈-통독필사-0펼침: 시작 전에도 0 으로 펼침
        Assert.Contains("<b class=\"num\">0</b><small>쓴 절</small>", home);
        var copy = await c.GetStringAsync("/kids/copy");
        Assert.Contains("필사 시작", copy);
        Assert.Contains("0 / 5절", copy);
        // 성경통독과 같은 틀(ADR 0105): 필사 목차(대시보드 · 구약 쓰기 · 신약 쓰기 · 참여자 현황) · 필사 테마 · ← 부서 홈으로
        Assert.Contains("aria-label=\"성경 필사 메뉴\"", copy);
        Assert.Contains("dataset.theme = 'copy'", copy);
        Assert.Contains("<a href=\"/kids/copy\" class=\"b2it on\" aria-current=\"page\">대시보드</a>", copy);
        Assert.Contains(">구약 쓰기</a>", copy);
        Assert.Contains(">신약 쓰기</a>", copy);
        Assert.Contains("← 부서 홈으로", copy);
        Assert.Contains("최근 8주", copy);
        Assert.Contains("구약 0 / 2절", copy);
        Assert.Contains("신약 0 / 3절", copy);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/kids/copy/books?t=nt&b=43")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/kids/copy/participants")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/kids/copy/write/43/9")).StatusCode);   // 없는 장
    }

    [Fact]
    public async Task R306_글자만_맞으면_저장하고_띄어쓰기_문장부호_대소문자는_보지_않는다()
    {
        TurnOn();
        var c = await Login("dongseok@example.com");
        Assert.False((await Save(c, 43, 3, 1, "KRV", "하나님이 세상을 이처럼 사랑하사")).GetProperty("ok").GetBoolean());   // 덜 씀
        Assert.False((await Save(c, 43, 3, 1, "KRV", "하나님이세상을이처럼사랑하사시험하시니다")).GetProperty("ok").GetBoolean());   // 틀린 글자
        Assert.Empty(Verses(_kid));

        var ok = await Save(c, 43, 3, 1, "KRV", "하나님이세상을이처럼사랑하사 시험하시니라");   // 쉼표 · 마침표 · 띄어쓰기 없이
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Equal((1, 2), (ok.GetProperty("chapterDone").GetInt32(), ok.GetProperty("chapterVerses").GetInt32()));
        Assert.True((await Save(c, 43, 3, 2, "NIV", "easy text 4332")).GetProperty("ok").GetBoolean());   // 대소문자 · 문장 부호 무시
        Assert.Equal("하나님이세상을이처럼사랑하사시험하시니라", CopyService.Letters("하나님이 세상을 이처럼 사랑하사, 시험하시니라."));
        Assert.Equal("믿는자는기뻐하리라", CopyService.Letters("\"믿는 자\"는 기뻐하리라!"));
    }

    [Fact]
    public async Task R305_같은_절은_번역이_달라도_한_번이고_진도와_이어_쓸_곳이_따라간다()
    {
        TurnOn();
        var c = await Login("dongseok@example.com");
        await Save(c, 43, 3, 1, "KRV", "하나님이 세상을 이처럼 사랑하사 시험하시니라");
        await Save(c, 43, 3, 1, "KLB", "쉬운 글 43 3 1");   // 같은 절을 다른 번역으로 → 한 줄, 번역만 바뀜
        var mine = Assert.Single(Verses(_kid));
        Assert.Equal("KLB", mine.Translation);

        var home = await c.GetStringAsync("/kids/copy");
        Assert.Contains("1 / 5절", home);
        Assert.Contains("요한복음 3장 2절", home);   // 이어 쓰기 = 같은 장의 안 쓴 첫 절
        Assert.Contains("신약 1 / 3절", home);
        var dept = await c.GetStringAsync("/kids");   // 부서 홈 카드(확정 시안 홈-필사카드): 원 · 이어 쓸 곳 · 신약 · 구약 · 쓴 절 · 오늘 N절
        Assert.Contains("hj-card hj-pg c", dept);
        Assert.Contains("<small>이어 쓸 곳</small><b>요한복음</b><span>3장 2절부터 ›</span>", dept);
        Assert.Contains("<small>쓴 절</small>", dept);
        Assert.Contains("오늘 1절", dept);
        var write = await c.GetStringAsync("/kids/copy/write/43/3");   // 쓰는 화면도 같은 틀(ADR 0105)
        Assert.Contains("aria-label=\"성경 필사 메뉴\"", write);
        Assert.Contains("class=\"b2it on\" aria-current=\"page\">신약 쓰기</a>", write);
        Assert.Contains("이어 쓰기<small>요한복음 3장</small>", write);   // 빠른 이동
        Assert.Contains("class=\"b2rh\"", write);                     // 통독 본문 화면과 같은 색 띠 머리(확정 시안 필사-통독흐름 ③)
        Assert.Contains("신약 · 성경 필사", write);
        Assert.Contains("<nav class=\"b2chs\" aria-label=\"장 번호\">", write);
        Assert.Contains("<div class=\"brd-lbl\">번역</div>", write);
        var books = await c.GetStringAsync("/kids/copy/books?t=nt&b=43");   // 통독 "신약 읽기"와 같은 화면(②)
        Assert.Contains("class=\"rp-panel\"", books);
        Assert.Contains("이어 쓰기 3장 →", books);
        Assert.Matches("<a class=\"ch here( cur)?\" href=\"/kids/copy/write/43/3\"", books);   // 쓰는 중인 장
        Assert.DoesNotContain("다 읽음 표시", books);
        Assert.Contains("책별 진도", await c.GetStringAsync("/kids/copy"));   // 대시보드 ①
        var js = await c.GetStringAsync("/js/copy.js");   // 복사 · 붙여넣기 막기(2026-11-16 사용자)
        Assert.Contains("input.addEventListener('paste', noPaste);", js);
        Assert.Contains("insertFromPaste", js);
        // 번역 칸 · 다음 장 링크는 따라 쓰기 영역(data-bc-write) 밖이라 문서 전체에서 찾는다 — 영역 안에서 찾으면 스크립트가 멈춰 화면이 빈다(2026-11-16 버그)
        Assert.Contains("trEl = document.querySelector('[data-bc-tr]')", js);
        Assert.DoesNotContain("root.querySelector('[data-bc-nextch]')", js);
        Assert.True(write.IndexOf("data-bc-tr", StringComparison.Ordinal) < write.IndexOf("data-bc-write", StringComparison.Ordinal));
        var board = await c.GetStringAsync("/kids/copy/participants");
        Assert.Contains("권동석 (나)", board);
        Assert.Contains("요한복음 3장 · 1/5", board);
    }

    [Fact]
    public async Task R307_학생이_신약을_다_쓰면_완필_선물이_생기고_어른은_없다()
    {
        TurnOn();
        var kid = await Login("dongseok@example.com");
        await Save(kid, 43, 3, 1, "KRV", "하나님이 세상을 이처럼 사랑하사 시험하시니라");
        await Save(kid, 43, 3, 2, "KRV", "믿는 자는 기뻐하리라");
        var last = await Save(kid, 66, 22, 1, "KRV", "아멘 주 예수여 오시옵소서");
        Assert.True(last.GetProperty("newlyDone").GetBoolean());

        var t = await Login("admin@example.com");   // 관리자(어른)도 신약을 다 씀 → 선물 없음
        await Save(t, 43, 3, 1, "KRV", "하나님이 세상을 이처럼 사랑하사 시험하시니라");
        await Save(t, 43, 3, 2, "KRV", "믿는 자는 기뻐하리라");
        await Save(t, 66, 22, 1, "KRV", "아멘 주 예수여 오시옵소서");

        using (var raw = InMemoryDb.Raw(_db))
        {
            var g = Assert.Single(raw.Gifts.Where(x => x.Kind == GiftKinds.Copy));
            Assert.Equal((_kid, CopyGiftService.NewKey, 10000, GiftStates.Open), (g.UserName, g.SourceKey, g.MaxAmount, g.State));
            Assert.Equal((new DateOnly(2027, 1, 5), new DateOnly(2027, 2, 4)), (g.OpenFrom!.Value, g.OpenUntil!.Value));
        }
        var home = await kid.GetStringAsync("/kids/copy");
        Assert.Contains("완필 선물을 골라요", home);
        Assert.Contains("신약 완필", home);
        Assert.Contains("필사를 다 썼어요", (await kid.GetStringAsync($"/kids/gifts/birthday/{Gift().Id}")));
        Assert.Contains("신약 완필", await t.GetStringAsync("/kids/admin/gifts?s=pick"));   // 관리자 선물 화면 "고르는 중"에도
    }

    private Gift Gift() { using var raw = InMemoryDb.Raw(_db); return raw.Gifts.Single(x => x.Kind == GiftKinds.Copy); }

    [Fact]
    public async Task R308_플랫폼에서_켜고_끄고_프로그램은_하나는_켜야_한다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
            InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        }
        using (var raw = InMemoryDb.Raw(_db)) { foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234"); raw.SaveChanges(); }
        using var scope = _app.Services.CreateScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformTenantService>();
        Assert.Equal("프로그램을 하나는 켜 주세요(R-150).", await platform.SetProgramsAsync("operator", _kids.Id, false, false, default, copy: false));
        Assert.Null(await platform.SetProgramsAsync("operator", _kids.Id, false, false, default, copy: true));   // 필사만 켠 부서도 된다
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal((false, false, true), (raw.Tenants.Single(t => t.Id == _kids.Id).MissionEnabled, raw.Tenants.Single(t => t.Id == _kids.Id).ReadingEnabled, raw.Tenants.Single(t => t.Id == _kids.Id).CopyEnabled));
        Assert.Null(await platform.SetProgramsAsync("operator", _kids.Id, true, true, default));   // 필사를 넘기지 않으면 그대로
        using (var raw = InMemoryDb.Raw(_db)) Assert.True(raw.Tenants.Single(t => t.Id == _kids.Id).CopyEnabled);
        var (made, errors) = await platform.CreateAsync("operator", new NewTenant("새 부서", "new-dept", "Asia/Seoul", "blue", true, true, null));   // 새 부서는 세 프로그램 모두 켬
        Assert.True(errors.Ok);
        Assert.True(made!.MissionEnabled && made.ReadingEnabled && made.CopyEnabled);
    }
}
