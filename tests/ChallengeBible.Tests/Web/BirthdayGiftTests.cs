using System.Net;
using System.Net;
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
/// 생일 선물(확정 시안 생일-선물, ADR 0094 D · 정한 것 14, R-288) + 로그인 없는 선물 링크(R-287). 오늘 = 2027-01-05(서울).
/// 김하준 = 계정 있는 학생(2월 1일 생일 → 1월 2일 ~ 25일), 박서윤 = 계정 없는 새친구(2월 3일), 윤동호 = 부서 관리자(교사, 2월 1일 — 안 받음).
/// </summary>
public partial class BirthdayGiftTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids;
    private readonly string _hajun;

    public BirthdayGiftTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(_time);
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var kid = InMemoryDb.SeedAccount(_db, "hajun@example.com", "김하준");
        _hajun = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "김하준", UserId = kid.Id, BirthMonth = 2, BirthDay = 1, CreatedUtc = DateTime.UtcNow });
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "박서윤", RoleCode = "T1020", BirthMonth = 2, BirthDay = 3, CreatedUtc = DateTime.UtcNow });
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "윤동호", UserId = adm.Id, BirthMonth = 2, BirthDay = 1, CreatedUtc = DateTime.UtcNow });
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "이도윤", RoleCode = "T1001", BirthMonth = 3, BirthDay = 20, CreatedUtc = DateTime.UtcNow });   // 아직 멀다
        raw.SaveChanges();
        // R-350: 생일 선물은 반에 든 학생 · 새친구만 — 학생 김하준 · 이도윤은 1반
        var term = new ClassTerm { TenantId = _kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 1), CreatedUtc = DateTime.UtcNow };
        raw.ClassTerms.Add(term); raw.SaveChanges();
        var cls = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1반", CreatedUtc = DateTime.UtcNow };
        raw.Classes.Add(cls); raw.SaveChanges();
        _class = (term.Id, cls.Id);
        foreach (var n in new[] { "김하준", "이도윤" })
            raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = cls.Id, PersonId = raw.People.Single(x => x.Name == n).Id, CreatedUtc = DateTime.UtcNow });
        foreach (var (n, p) in new[] { ("싼 연필", 500), ("곰돌이 열쇠고리", 3000), ("스티커북", 5000), ("비싼 레고", 6000) })
            raw.GiftItems.Add(new GiftItem { Name = n, Price = p, CoupangUrl = "https://www.coupang.com/vp/products/1", Status = GiftItemStatus.Active });
        raw.SaveChanges();
    }

    private (int Term, int Class) _class;

    /// <summary>그 사람을 1반에 넣는다(R-350: 반에 든 학생만 생일 선물).</summary>
    private void Place(string name)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = _class.Term, ClassId = _class.Class, PersonId = raw.People.Single(x => x.Name == name).Id, CreatedUtc = DateTime.UtcNow });
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();
    [GeneratedRegex("/gift/([A-Za-z0-9]{32})")]
    private static partial Regex LinkRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string tokenPage, string url, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync(tokenPage)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private Gift GiftOf(string name)
    {
        using var raw = InMemoryDb.Raw(_db);
        var pid = raw.People.Single(p => p.Name == name).Id;
        return raw.Gifts.Single(g => g.PersonId == pid);
    }

    private int ItemId(string name) { using var raw = InMemoryDb.Raw(_db); return raw.GiftItems.Single(i => i.Name == name).Id; }

    [Fact]
    public void R288_고르는_기간은_생일_30일_전부터_7일_전까지_2월29일생은_평년_28일()
    {
        var today = new DateOnly(2027, 1, 5);
        Assert.Equal(new DateOnly(2027, 2, 1), BirthdayGiftService.WindowBirthday(2, 1, today, 30, 7));
        Assert.Null(BirthdayGiftService.WindowBirthday(1, 10, today, 30, 7));   // 1월 3일에 끝남
        Assert.Null(BirthdayGiftService.WindowBirthday(3, 20, today, 30, 7));   // 아직 멀다
        Assert.Equal(new DateOnly(2027, 1, 20), BirthdayGiftService.WindowBirthday(1, 20, new DateOnly(2026, 12, 25), 30, 7));   // 1월 생일은 전해 12월부터
        Assert.Equal(new DateOnly(2027, 2, 28), BirthdayGiftService.WindowBirthday(2, 29, new DateOnly(2027, 2, 1), 30, 7));
        Assert.Equal("B2027-0201", BirthdayGiftService.SourceKey(new DateOnly(2027, 2, 1)));
        Assert.Equal(new DateOnly(2027, 2, 1), BirthdayGiftService.BirthdayOf("B2027-0201-r270105000000"));
    }

    [Fact]
    public async Task R288_생일_30일_전이면_학생_새친구_본인에게_생기고_교사는_안_받는다_부서_홈_카드()
    {
        var c = await Login("hajun@example.com");
        var home = await c.GetStringAsync("/kids");
        Assert.Contains("생일 선물을 골라요", home);
        Assert.Contains("2월 1일 생일", home);
        Assert.Contains("1월 25일까지", home);   // 생일 7일 전

        using var raw = InMemoryDb.Raw(_db);
        var gifts = raw.Gifts.Where(g => g.Kind == GiftKinds.Birthday).ToList();
        Assert.Equal(2, gifts.Count);   // 김하준 · 박서윤(새친구, 계정 없음) — 윤동호(교사) · 이도윤(아직 멀다) 없음
        var hajun = GiftOf("김하준");
        Assert.Equal((_hajun, GiftStates.Open, 1000, 5000, new DateOnly(2027, 1, 2), new DateOnly(2027, 1, 25), "B2027-0201"),
            (hajun.UserName, hajun.State, hajun.MinAmount, hajun.MaxAmount, hajun.OpenFrom!.Value, hajun.OpenUntil!.Value, hajun.SourceKey));
        Assert.Null(GiftOf("박서윤").UserName);

        await c.GetStringAsync("/kids");   // 다시 열어도 한 해 한 번
        using var raw2 = InMemoryDb.Raw(_db);
        Assert.Equal(2, raw2.Gifts.Count(g => g.Kind == GiftKinds.Birthday));
    }

    [Fact]
    public async Task R288_금액_안의_상품만_가격_없이_고르고_사기_전까지_바꾼다()
    {
        var c = await Login("hajun@example.com");
        await c.GetStringAsync("/kids");
        var id = GiftOf("김하준").Id;
        var page = await c.GetStringAsync($"/kids/gifts/birthday/{id}");
        Assert.Contains("곰돌이 열쇠고리", page);
        Assert.Contains("스티커북", page);
        Assert.DoesNotContain("싼 연필", page);   // 1,000원 아래
        Assert.DoesNotContain("비싼 레고", page);   // 5,000원 위
        Assert.DoesNotContain("3,000", page);

        await Post(c, $"/kids/gifts/birthday/{id}", $"/kids/gifts/birthday/{id}", new() { ["item"] = ItemId("곰돌이 열쇠고리").ToString() });
        Assert.Equal((GiftStates.Picked, "곰돌이 열쇠고리"), (GiftOf("김하준").State, GiftOf("김하준").ItemName));
        await Post(c, $"/kids/gifts/birthday/{id}?change=true", $"/kids/gifts/birthday/{id}", new() { ["item"] = ItemId("스티커북").ToString() });
        Assert.Equal("스티커북", GiftOf("김하준").ItemName);
        Assert.Contains("준비 중", await c.GetStringAsync("/kids"));

        Assert.Equal(HttpStatusCode.NotFound, (await (await Login("admin@example.com")).GetAsync($"/kids/gifts/birthday/{id}")).StatusCode);   // 남의 생일 선물
    }

    [Fact]
    public async Task R287_관리자가_링크를_만들면_로그인_없이_고르고_틀린_링크는_막힌다()
    {
        var admin = await Login("admin@example.com");
        var pick = await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Contains("생일 2.3", pick);
        Assert.Contains("계정 없음", pick);
        var id = GiftOf("박서윤").Id;
        var res = await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Link", new() { ["id"] = id.ToString(), ["s"] = "pick" });
        var box = await admin.GetStringAsync(res.Headers.Location!.OriginalString);
        Assert.Contains("박서윤 생일 선물 링크", box);
        var token = LinkRx().Match(box).Groups[1].Value;
        Assert.Equal(token, GiftOf("박서윤").LinkToken);

        var parent = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });   // 로그인 없음
        var link = await parent.GetStringAsync($"/gift/{token}");
        Assert.Contains("박서윤 어린이 생일 선물", link);
        Assert.Contains("스티커북", link);
        Assert.DoesNotContain("비싼 레고", link);
        await Post(parent, $"/gift/{token}", $"/gift/{token}", new() { ["item"] = ItemId("스티커북").ToString() });
        var g = GiftOf("박서윤");
        Assert.Equal((GiftStates.Picked, "스티커북"), (g.State, g.ItemName));
        Assert.Contains("준비 중이에요", await parent.GetStringAsync($"/gift/{token}"));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(BirthdayGiftService.LinkActor, raw.GiftLogs.Where(l => l.GiftId == id && l.Action == GiftActions.Pick).Single().ActorUserName);

        await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Link", new() { ["id"] = id.ToString(), ["fresh"] = "true", ["s"] = "buy" });   // [새 링크]는 없앴다 — fresh 를 보내도 링크는 그대로
        Assert.Equal(token, GiftOf("박서윤").LinkToken);
        Assert.Contains("data-share=", await admin.GetStringAsync($"/kids/admin/gifts?s=buy&link={id}"));   // 보내기 창에 [카톡으로 보내기](고른 뒤라 살 것)
        Assert.DoesNotContain("새 링크", await admin.GetStringAsync("/kids/admin/gifts?s=pick"));
        Assert.Contains("열 수 없는 링크예요", await parent.GetStringAsync("/gift/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

        await Post(admin, "/kids/admin/gifts?s=buy", "/kids/admin/gifts?handler=Move", new() { ["id"] = id.ToString(), ["act"] = "Purchase", ["s"] = "buy" });
        var fresh = GiftOf("박서윤").LinkToken!;
        var after = await parent.GetStringAsync($"/gift/{fresh}");
        Assert.Contains("선생님이 준비했어요", after);
        Assert.DoesNotContain("다른 선물로 바꾸기", after);   // 산 뒤에는 바꿀 수 없다
    }

    [Fact]
    public async Task R288_기한이_지나면_다시_열기_7일_안_주기는_없다()
    {
        var admin = await Login("admin@example.com");
        var pick = await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.DoesNotContain("handler=Skip", pick);   // 2026-11-21 사용자: [선물 안 주기] · [다시 주기] 없앰
        var id = GiftOf("김하준").Id;

        _time.Now = new DateTimeOffset(2027, 1, 26, 0, 0, 0, TimeSpan.Zero);   // 기한(1월 25일) 지남
        Assert.Contains("기한 지남", await admin.GetStringAsync("/kids/admin/gifts?s=pick"));
        Assert.Equal(GiftStates.Expired, GiftOf("김하준").State);
        await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Reopen", new() { ["id"] = id.ToString() });
        Assert.Equal((GiftStates.Open, new DateOnly(2027, 2, 2)), (GiftOf("김하준").State, GiftOf("김하준").OpenUntil!.Value));
    }

    [Fact]
    public async Task R297_부서_카드에는_생일_선물을_생일_주에만_보인다()
    {
        var c = await Login("hajun@example.com");
        await c.GetStringAsync("/kids");
        var id = GiftOf("김하준").Id;
        await Post(c, $"/kids/gifts/birthday/{id}", $"/kids/gifts/birthday/{id}", new() { ["item"] = ItemId("스티커북").ToString() });
        Assert.DoesNotContain("스티커북", await c.GetStringAsync("/kids/gift-board"));   // 1월 5일: 생일(2월 1일) 주가 아님

        _time.Now = new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero);   // 1월 31일(일) ~ 2월 6일(토) 주
        Assert.Contains("스티커북", await c.GetStringAsync("/kids/gift-board"));
    }

    /// <summary>
    /// 담임(관리자 아님)도 오늘 할 일 카드에서 부모님 안내글을 보낸다(확정 시안 생일링크-부모안내 ①, 2026-11-17 "조진찬 선생님 링크가 없다").
    /// 계정 없는 아이 = 링크로, 계정 연결된 아이 = "아이 계정에서 직접 고를 수 있어요"도. 다른 반 선생님은 못 받는다. 부모님 화면은 3단계 · 기한 · 다 됐어요.
    /// </summary>
    [Fact]
    public async Task R321_담임은_할_일에서_부모님_안내글을_보내고_부모님은_3단계로_고른다()
    {
        var admin = await Login("admin@example.com");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");   // 생일 선물 생김
        var t = InMemoryDb.SeedAccount(_db, "kim@example.com", "조진찬 선생님");
        InMemoryDb.SeedMembership(_db, _kids.Id, t.UserName!, MembershipStatus.Approved, Roles.Teacher);
        var o = InMemoryDb.SeedAccount(_db, "other@example.com", "다른반");
        InMemoryDb.SeedMembership(_db, _kids.Id, o.UserName!, MembershipStatus.Approved, Roles.Teacher);
        using (var raw = InMemoryDb.Raw(_db))
        {
            foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
            var term = new ClassTerm { TenantId = _kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 1), CreatedUtc = DateTime.UtcNow };
            raw.ClassTerms.Add(term); raw.SaveChanges();
            var cls = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1반", CreatedUtc = DateTime.UtcNow };
            var cls2 = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "2반", CreatedUtc = DateTime.UtcNow };
            raw.Classes.AddRange(cls, cls2); raw.SaveChanges();
            var tp = new Person { TenantId = _kids.Id, Name = "조진찬 선생님", UserId = t.Id, CreatedUtc = DateTime.UtcNow };
            var op = new Person { TenantId = _kids.Id, Name = "다른반", UserId = o.Id, CreatedUtc = DateTime.UtcNow };
            raw.People.AddRange(tp, op); raw.SaveChanges();
            raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = cls.Id, PersonId = tp.Id, IsTeacher = true, CreatedUtc = DateTime.UtcNow });
            raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = cls2.Id, PersonId = op.Id, IsTeacher = true, CreatedUtc = DateTime.UtcNow });
            foreach (var n in new[] { "박서윤", "김하준" })
                raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = cls.Id, PersonId = raw.People.Single(p => p.Name == n).Id, CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var seoyun = GiftOf("박서윤").Id;
        var kim = await Login("kim@example.com");
        await kim.GetAsync("/kids");
        var todo = await kim.GetStringAsync("/kids/todo");
        Assert.Contains("생일 선물 링크 · 박서윤", todo);
        Assert.Contains("부모님께 보내기", todo);
        Assert.Contains("안내글 복사", todo);
        Assert.Contains("아이 계정에서 직접 고를 수 있어요", todo);   // 김하준은 계정 연결됨
        Assert.DoesNotContain("선생님님", todo);
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(todo, "41-할일-부모님께보내기");

        var r = await Post(kim, "/kids/todo", "/kids/todo?handler=Share", new() { ["id"] = seoyun.ToString() });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var text = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("text").GetString()!;
        Assert.Contains("박서윤 어린이의 생일(2월 3일)을 축하해요", text);
        Assert.Contains("로그인 없이 바로 열려요", text);
        Assert.Contains("까지 골라 주세요", text);
        Assert.DoesNotContain("아이 계정으로", text);
        var token = LinkRx().Match(text).Groups[1].Value;
        Assert.Equal(token, GiftOf("박서윤").LinkToken);
        var hajun = await Post(kim, "/kids/todo", "/kids/todo?handler=Share", new() { ["id"] = GiftOf("김하준").Id.ToString() });
        Assert.Contains("아이 계정으로 로그인해서 직접 골라도 돼요", System.Text.Json.JsonDocument.Parse(await hajun.Content.ReadAsStringAsync()).RootElement.GetProperty("text").GetString());

        var other = await Login("other@example.com");
        await other.GetAsync("/kids");
        Assert.Equal(HttpStatusCode.NotFound, (await Post(other, "/kids/todo", "/kids/todo?handler=Share", new() { ["id"] = seoyun.ToString() })).StatusCode);

        var parent = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var link = await parent.GetStringAsync($"/gift/{token}");
        Assert.Contains("박서윤 어린이<br />생일 축하 선물이에요", link);
        Assert.Contains("부모님이 하실 일", link);
        Assert.Contains("고르는 기한", link);
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(link, "42-부모님-선물고르기");
        await Post(parent, $"/gift/{token}", $"/gift/{token}", new() { ["item"] = ItemId("스티커북").ToString() });
        var done = await parent.GetStringAsync($"/gift/{token}");
        Assert.Contains("다 됐어요", done);
        Assert.Contains("부모님이 더 하실 일은 없어요", done);
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(done, "43-부모님-다됐어요");
        Assert.DoesNotContain("생일 선물 링크 · 박서윤", await kim.GetStringAsync("/kids/todo"));   // 고르면 사라진다
    }

    // ── R-322 생일 선물은 명부를 따라간다(ADR 0109, 2026-11-17 사용자 검증 요청): 한 해 한 아이 1건 · 연결 변경 · 생일 고침 · 명단 다시 올리기 ──

    private int BirthdayCount(string name)
    {
        using var raw = InMemoryDb.Raw(_db);
        var pid = raw.People.Single(p => p.Name == name).Id;
        return raw.Gifts.Count(g => g.Kind == GiftKinds.Birthday && g.PersonId == pid);
    }

    private void SetBirthday(string name, byte m, byte d)
    {
        using var raw = InMemoryDb.Raw(_db);
        var p = raw.People.Single(x => x.Name == name);
        (p.BirthMonth, p.BirthDay) = (m, d);
        raw.SaveChanges();
    }

    [Fact]
    public async Task R322_명부와_계정_연결_여부별로_한_아이_한_건이고_고른_뒤에는_다시_고르라고_하지_않는다()
    {
        // 계정만(명부에 생일 없음): 선물 없음 · 오류 없음
        var solo = InMemoryDb.SeedAccount(_db, "solo@example.com", "혼자");
        InMemoryDb.SeedMembership(_db, _kids.Id, solo.UserName!, MembershipStatus.Approved, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db)) { raw.Users.Single(u => u.Id == solo.Id).PasswordHash = new PasswordHasher<AppUser>().HashPassword(raw.Users.Single(u => u.Id == solo.Id), "1234"); raw.SaveChanges(); }
        var s = await Login("solo@example.com");
        Assert.DoesNotContain("생일 선물을 골라요", await s.GetStringAsync("/kids"));

        // 명부 + 계정: 아이가 미션 홈에서 · 다른 기기에서 다시 열어도 1건
        var c = await Login("hajun@example.com");
        Assert.Contains("생일 선물을 골라요", await c.GetStringAsync("/kids"));
        var c2 = await Login("hajun@example.com");
        await c2.GetStringAsync("/kids");
        // 명부만: 관리자 화면을 열면 생기고, 계정 없음
        var admin = await Login("admin@example.com");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Equal((1, 1), (BirthdayCount("김하준"), BirthdayCount("박서윤")));
        Assert.Null(GiftOf("박서윤").UserName);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(0, raw.Gifts.Count(g => g.UserName == solo.UserName));

        // 고르고 나면 두 기기 모두 "준비 중", 다시 고르라고 하지 않고 여전히 1건
        var id = GiftOf("김하준").Id;
        await Post(c, $"/kids/gifts/birthday/{id}", $"/kids/gifts/birthday/{id}", new() { ["item"] = ItemId("스티커북").ToString() });
        var again = await c2.GetStringAsync("/kids");
        Assert.DoesNotContain("생일 선물을 골라요", again);
        Assert.Contains("준비 중", again);
        Assert.Equal(1, BirthdayCount("김하준"));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Single(raw.GiftLogs.Where(l => l.GiftId == id && l.Action == GiftActions.Pick));
    }

    [Fact]
    public async Task R322_연결을_풀면_옛_계정은_못_보고_새_계정이_같은_선물을_본다()
    {
        var c = await Login("hajun@example.com");
        await c.GetStringAsync("/kids");
        var id = GiftOf("김하준").Id;
        var neo = InMemoryDb.SeedAccount(_db, "neo@example.com", "새계정");
        InMemoryDb.SeedMembership(_db, _kids.Id, neo.UserName!, MembershipStatus.Approved, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var u = raw.Users.Single(x => x.Id == neo.Id); u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
            raw.People.RemoveRange(raw.People.Where(p => p.UserId == neo.Id));   // 새 계정의 빈 명부 줄(있으면)
            var p = raw.People.Single(x => x.Name == "김하준");
            p.UserId = null;   // 연결 풀기
            await BirthdayGiftService.FollowPersonAsync(raw, p.Id, null, CancellationToken.None);
            raw.SaveChanges();
        }
        Assert.Null(GiftOf("김하준").UserName);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/kids/gifts/birthday/{id}")).StatusCode);   // 옛 계정
        Assert.DoesNotContain("생일 선물을 골라요", await c.GetStringAsync("/kids"));

        using (var raw = InMemoryDb.Raw(_db)) { raw.People.Single(x => x.Name == "김하준").UserId = neo.Id; raw.SaveChanges(); }   // 새 계정과 연결
        var n = await Login("neo@example.com");
        Assert.Contains("생일 선물을 골라요", await n.GetStringAsync("/kids"));
        Assert.Equal(HttpStatusCode.OK, (await n.GetAsync($"/kids/gifts/birthday/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/kids/gifts/birthday/{id}")).StatusCode);
        using (var raw = InMemoryDb.Raw(_db))   // 옛 계정이 부서 홈을 열면 자기 빈 명부 줄이 생기므로(이름이 같다) 선물 번호로 본다
        {
            var pid = raw.People.Single(p => p.UserId == neo.Id).Id;
            Assert.Equal((neo.UserName, 1), (raw.Gifts.Single(g => g.Id == id).UserName, raw.Gifts.Count(g => g.Kind == GiftKinds.Birthday && g.PersonId == pid)));
        }   // 계정은 명부를 따라 바뀌고, 선물은 그대로 1건
    }

    [Fact]
    public async Task R322_생일을_고치면_새로_만들지_않고_그_선물의_생일과_기한을_고친다()
    {
        var admin = await Login("admin@example.com");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        var id = GiftOf("박서윤").Id;

        SetBirthday("박서윤", 2, 1);   // 기간 안에서 고침(오늘 1월 5일 — 2월 1일생은 1월 2일 ~ 25일)
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        var g = GiftOf("박서윤");
        Assert.Equal((1, id, "B2027-0201", GiftStates.Open, new DateOnly(2027, 1, 25)), (BirthdayCount("박서윤"), g.Id, g.SourceKey, g.State, g.OpenUntil!.Value));

        SetBirthday("박서윤", 1, 6);   // 이미 기간이 지난 날로 고침 → 기한 지남(다시 열 수 있음), 새로 안 생김
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        g = GiftOf("박서윤");
        Assert.Equal((1, id, "B2027-0106", GiftStates.Expired), (BirthdayCount("박서윤"), g.Id, g.SourceKey, g.State));

        SetBirthday("박서윤", 3, 20);   // 아직 기간이 안 온 날로 고침 → 지움(기간이 되면 다시 생긴다)
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Equal(0, BirthdayCount("박서윤"));

        SetBirthday("박서윤", 2, 3);
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Equal(1, BirthdayCount("박서윤"));
        var c = await Login("hajun@example.com");   // 이미 고른 선물은 생일을 고쳐도 그대로
        await c.GetStringAsync("/kids");
        var hid = GiftOf("김하준").Id;
        await Post(c, $"/kids/gifts/birthday/{hid}", $"/kids/gifts/birthday/{hid}", new() { ["item"] = ItemId("스티커북").ToString() });
        SetBirthday("김하준", 2, 5);
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Equal((1, "B2027-0201", GiftStates.Picked), (BirthdayCount("김하준"), GiftOf("김하준").SourceKey, GiftOf("김하준").State));
    }

    [Fact]
    public async Task R322_명단을_다시_올려도_선물은_이어지고_고른_아이는_새_명단에_없어도_남긴다()
    {
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.People.Add(new Person { TenantId = _kids.Id, Name = "최지우", RoleCode = "T1001", BirthMonth = 2, BirthDay = 4, CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        Place("최지우");   // R-350: 반에 든 학생
        var admin = await Login("admin@example.com");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        var seoyun = GiftOf("박서윤");
        var jiwoo = GiftOf("최지우");
        var res = await Post(admin, "/kids/admin/gifts?s=pick", "/kids/admin/gifts?handler=Link", new() { ["id"] = seoyun.Id.ToString(), ["s"] = "pick" });
        var token = LinkRx().Match(await admin.GetStringAsync(res.Headers.Location!.OriginalString)).Groups[1].Value;
        var parent = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Post(parent, $"/gift/{token}", $"/gift/{token}", new() { ["item"] = ItemId("스티커북").ToString() });   // 박서윤 = 고름
        int seoyunPid, jiwooPid;
        using (var raw = InMemoryDb.Raw(_db)) { seoyunPid = raw.People.Single(p => p.Name == "박서윤").Id; jiwooPid = raw.People.Single(p => p.Name == "최지우").Id; }

        RosterPlan Plan(params string[] names) => new(_kids.Id, "kids", "kids.xlsx",
            names.Select((n, i) => new RosterLine(i + 2, n, null, n == "박서윤" ? (byte)2 : (byte)2, n == "박서윤" ? (byte)3 : (byte)4, null, null, null)).ToList(), [], [], 0, 0, 0, []);
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RosterUploadService>().ApplyAsync(op.UserName!, Plan("박서윤", "최지우"), CancellationToken.None);
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal(seoyunPid, raw.People.Single(p => p.Name == "박서윤").Id);   // 같은 줄을 다시 씀 → 선물 · 링크가 이어진다
            Assert.Equal(jiwooPid, raw.People.Single(p => p.Name == "최지우").Id);
        }
        Assert.Equal((1, 1), (BirthdayCount("박서윤"), BirthdayCount("최지우")));
        Assert.Contains("다 됐어요", await parent.GetStringAsync($"/gift/{token}"));
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Equal((1, 1), (BirthdayCount("박서윤"), BirthdayCount("최지우")));   // 다시 열어도 새로 안 생김

        RosterResult r;
        using (var scope = _app.Services.CreateScope())
            r = await scope.ServiceProvider.GetRequiredService<RosterUploadService>().ApplyAsync(op.UserName!, Plan("이도윤"), CancellationToken.None);
        Assert.Equal(1, r.KeptForGifts);
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.True(raw.People.Any(p => p.Id == seoyunPid));   // 고른 아이는 남김(반 없음)
            Assert.False(raw.ClassMembers.Any(m => m.PersonId == seoyunPid));
            Assert.False(raw.People.Any(p => p.Id == jiwooPid));   // 안 고른 아이는 명부 · 선물 정리
            Assert.False(raw.Gifts.Any(g => g.Id == jiwoo.Id));
            Assert.Equal((GiftStates.Picked, "스티커북"), (raw.Gifts.Single(g => g.Id == seoyun.Id).State, raw.Gifts.Single(g => g.Id == seoyun.Id).ItemName));
        }
    }

    // R-346(ADR 0123): 생일 선물 카드는 부서 홈 예배 안내 아래 · 홈을 열면 팝업(하루 한 번) · 다음에 할래요 1 · 3 · 5일 · 고르면 안 뜸 · 기한 하루 전엔 미뤄도 뜸

    [Fact]
    public void R346_팝업은_미룬_날이_지나거나_기한_하루_전부터_뜬다()
    {
        var today = new DateOnly(2027, 1, 5);
        Assert.True(BirthdayGiftService.PopupDue(null, new DateOnly(2027, 1, 25), today));
        Assert.False(BirthdayGiftService.PopupDue(today.AddDays(3), new DateOnly(2027, 1, 25), today));
        Assert.True(BirthdayGiftService.PopupDue(today, new DateOnly(2027, 1, 25), today));   // 미룬 날이 오늘이면 다시
        Assert.True(BirthdayGiftService.PopupDue(today.AddDays(5), today.AddDays(1), today));   // 기한 하루 전 — 미뤄도 뜬다
        Assert.True(BirthdayGiftService.PopupDue(today.AddDays(5), today, today));   // 기한 날
        Assert.False(BirthdayGiftService.PopupDue(today.AddDays(5), today.AddDays(2), today));
    }

    [Fact]
    public async Task R346_부서_홈에_카드와_팝업이_열_때마다_뜨고_성경미션_홈에서는_뺀다()
    {
        var c = await Login("hajun@example.com");
        var home = await c.GetStringAsync("/kids");
        Assert.Contains("bdh-card", home);
        Assert.Contains("data-bdh-pop", home);
        Assert.Contains("김하준님, 생일 축하해요!", home);
        Assert.Contains("1월 25일까지 고를 수 있어요", home);
        Assert.Contains($"/kids/gifts/birthday/{GiftOf("김하준").Id}", home);
        Assert.Contains("data-bdh-pop", await c.GetStringAsync("/kids"));   // 고르거나 미루기 전까지는 새로 열 때마다(F5) 뜬다
        Assert.DoesNotContain("생일 선물을 골라요", await c.GetStringAsync("/kids/word-mission"));
        var a = await Login("admin@example.com");
        await a.GetAsync("/kids");   // 로그인 직후 한 번은 할 일 화면으로 간다
        Assert.DoesNotContain("data-bdh-pop", await a.GetStringAsync("/kids"));   // 교사는 생일 선물이 없다
    }

    [Fact]
    public async Task R346_다음에_할래요는_1_3_5일만_미루고_기한_하루_전에는_다시_뜬다()
    {
        var c = await Login("hajun@example.com");
        var r = await Post(c, "/kids", "/kids?handler=SnoozeBirthday", new() { ["days"] = "3" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(new DateOnly(2027, 1, 8), raw.UserSettings.Single(x => x.UserName == _hajun).BirthdayPopupSnoozedUntil);
        var home = await (await Login("hajun@example.com")).GetStringAsync("/kids");
        Assert.DoesNotContain("data-bdh-pop", home);
        Assert.Contains("bdh-card", home);   // 카드는 그대로

        await Post(c, "/kids", "/kids?handler=SnoozeBirthday", new() { ["days"] = "99" });   // 정한 값이 아니면 하루
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(new DateOnly(2027, 1, 6), raw.UserSettings.Single(x => x.UserName == _hajun).BirthdayPopupSnoozedUntil);

        await Post(c, "/kids", "/kids?handler=SnoozeBirthday", new() { ["days"] = "5" });
        using (var raw = InMemoryDb.Raw(_db)) { raw.Gifts.Single(g => g.UserName == _hajun && g.Kind == GiftKinds.Birthday).OpenUntil = new DateOnly(2027, 1, 6); raw.SaveChanges(); }
        Assert.Contains("data-bdh-pop", await (await Login("hajun@example.com")).GetStringAsync("/kids"));   // 내일이 기한 → 미뤄도 뜬다
    }

    [Fact]
    public async Task R346_고른_뒤에는_팝업이_안_뜨고_카드는_준비_중_선물_화면은_부서_홈으로()
    {
        var c = await Login("hajun@example.com");
        await c.GetStringAsync("/kids");   // 선물이 생긴다
        using (var raw = InMemoryDb.Raw(_db))
        {
            var g = raw.Gifts.Single(x => x.UserName == _hajun && x.Kind == GiftKinds.Birthday);
            g.State = GiftStates.Picked; g.ItemName = "스티커북"; g.GiftItemId = ItemId("스티커북"); g.ItemHasImage = true; g.PickedUtc = DateTime.UtcNow;
            raw.SaveChanges();
        }
        var home = await (await Login("hajun@example.com")).GetStringAsync("/kids");
        Assert.DoesNotContain("data-bdh-pop", home);
        Assert.Contains("준비 중", home);
        Assert.Contains("스티커북", home);
        Assert.Contains("hj-card bdh-got", home);   // 다른 카드와 같은 테두리(확정 시안 고른선물 ①)
        Assert.Contains("<b>스티커북</b>", home);   // 설명이 없는 상품은 이름을 그대로
        using (var raw = InMemoryDb.Raw(_db)) { raw.GiftItems.Single(i => i.Name == "스티커북").Description = "알록달록 스티커로 꾸미는 책"; raw.SaveChanges(); }
        var withDesc = await (await Login("hajun@example.com")).GetStringAsync("/kids");
        Assert.Contains("<b>알록달록 스티커로 꾸미는 책</b>", withDesc);   // R-349: 카드 제목 자리에 선물 설명 글
        var page = await (await Login("hajun@example.com")).GetStringAsync($"/kids/gifts/birthday/{GiftOf("김하준").Id}");
        Assert.Contains("← 부서 홈", page);   // 생일 선물은 부서 홈으로 돌아간다
        Assert.Contains("gkd-pic", page);
        Assert.Contains($"/images/AwardItem/{ItemId("스티커북")}.jpg", page);
        Assert.Contains("준비 중이에요", page);
    }

    // R-347: 고른 선물 취소 — 사기 전만, 기한 안이면 다시 고르기, 지났으면 기한 지남, 기록 Cancel. 아이 화면 · 부모님 링크 모두

    [Fact]
    public async Task R347_선물_취소는_고른_것을_지우고_다시_고르게_하고_기록을_남긴다()
    {
        var c = await Login("hajun@example.com");
        await c.GetStringAsync("/kids");
        var id = GiftOf("김하준").Id;
        var url = $"/kids/gifts/birthday/{id}";
        await Post(c, url, url, new() { ["item"] = ItemId("스티커북").ToString() });
        var picked = await c.GetStringAsync(url);
        Assert.Contains("선물 취소", picked);
        Assert.Contains("고른 선물을 취소할까요?", picked);
        Assert.Contains("1월 25일까지 다시 고를 수 있어요", picked);

        var r = await Post(c, url, url + "?handler=Cancel", []);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var g = GiftOf("김하준");
        Assert.Equal((GiftStates.Open, (int?)null, (string?)null), (g.State, g.GiftItemId, g.ItemName));
        var after = await c.GetStringAsync(url);
        Assert.Contains("선물을 취소했어요. 다시 골라 주세요.", after);
        Assert.Contains("data-gk-pick", after);   // 다시 고르기 화면
        Assert.Contains("data-bdh-pop", await c.GetStringAsync("/kids"));   // 팝업도 다시
        using (var raw = InMemoryDb.Raw(_db))
            Assert.Contains(raw.GiftLogs.Where(l => l.GiftId == id), l => l.Action == GiftActions.Cancel && l.ItemName == "스티커북" && l.ActorUserName == _hajun);

        await Post(c, url, url + "?handler=Cancel", []);   // 고른 것이 없으면 아무 일도 없다
        Assert.Equal(GiftStates.Open, GiftOf("김하준").State);
    }

    [Fact]
    public async Task R347_기한이_지났으면_취소하면_기한_지남이고_산_뒤에는_취소할_수_없다()
    {
        var c = await Login("hajun@example.com");
        await c.GetStringAsync("/kids");
        var id = GiftOf("김하준").Id;
        var url = $"/kids/gifts/birthday/{id}";
        await Post(c, url, url, new() { ["item"] = ItemId("스티커북").ToString() });
        using (var raw = InMemoryDb.Raw(_db)) { raw.Gifts.Single(x => x.Id == id).OpenUntil = new DateOnly(2027, 1, 4); raw.SaveChanges(); }
        Assert.Contains("다시 고를 수 없어요", await c.GetStringAsync(url));
        await Post(c, url, url + "?handler=Cancel", []);
        Assert.Equal(GiftStates.Expired, GiftOf("김하준").State);

        using (var raw = InMemoryDb.Raw(_db)) { var g = raw.Gifts.Single(x => x.Id == id); g.State = GiftStates.Purchased; g.ItemName = "스티커북"; g.GiftItemId = ItemId("스티커북"); raw.SaveChanges(); }
        Assert.DoesNotContain("선물 취소", await c.GetStringAsync(url));
        await Post(c, "/kids", url + "?handler=Cancel", []);
        Assert.Equal(GiftStates.Purchased, GiftOf("김하준").State);
    }

    [Fact]
    public async Task R347_부모님_링크에서도_취소한다()
    {
        var admin = await Login("admin@example.com");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        var gid = GiftOf("박서윤").Id;
        string token;
        using (var raw = InMemoryDb.Raw(_db)) token = raw.Gifts.Single(x => x.Id == gid).LinkToken ?? "";
        if (token.Length == 0)
        {
            using var raw = InMemoryDb.Raw(_db);
            var g = raw.Gifts.Single(x => x.Id == gid); g.LinkToken = token = new string('A', 32); raw.SaveChanges();
        }
        var p = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Post(p, $"/gift/{token}", $"/gift/{token}", new() { ["item"] = ItemId("스티커북").ToString() });
        Assert.Equal(GiftStates.Picked, GiftOf("박서윤").State);
        Assert.Contains("선물 취소", await p.GetStringAsync($"/gift/{token}"));
        await Post(p, $"/gift/{token}", $"/gift/{token}?handler=Cancel", []);
        Assert.Equal(GiftStates.Open, GiftOf("박서윤").State);
        Assert.Contains("선물을 취소했어요", await p.GetStringAsync($"/gift/{token}"));
    }

    // R-350: 생일 선물은 반에 든 학생 · 새친구만. 반이 없는 학생은 생기지 않고 생일 목록에 "선물 대상 아님 · 반 없음"

    [Fact]
    public void R350_대상은_반에_든_학생과_새친구()
    {
        Assert.True(BirthdayGiftService.BirthdayEligible("T1001", inClass: true));
        Assert.False(BirthdayGiftService.BirthdayEligible("T1001", inClass: false));
        Assert.True(BirthdayGiftService.BirthdayEligible("T1020", inClass: false));   // 새친구는 반이 없어도
        Assert.False(BirthdayGiftService.BirthdayEligible("T1002", inClass: true));   // 교사
        Assert.False(BirthdayGiftService.BirthdayEligible(null, inClass: true));   // 학부모
    }

    [Fact]
    public async Task R350_반이_없는_학생은_선물이_안_생기고_반에_넣으면_생긴다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.People.Add(new Person { TenantId = _kids.Id, Name = "반없음", RoleCode = "T1001", BirthMonth = 2, BirthDay = 2, CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var admin = await Login("admin@example.com");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Equal(0, BirthdayCount("반없음"));
        Assert.Equal(1, BirthdayCount("박서윤"));   // 새친구는 반이 없어도 생긴다
        Assert.DoesNotContain("반없음", await admin.GetStringAsync("/kids/birthdays"));   // R-351: 생일 목록에도 안 보인다

        Place("반없음");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        Assert.Equal(1, BirthdayCount("반없음"));
        Assert.Contains("반없음", await admin.GetStringAsync("/kids/birthdays"));
    }
}
