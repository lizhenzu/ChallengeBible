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
/// 쪽지(ADR 0112, 확정 시안 쪽지 · 쪽지-전체): R-325 1:1 · 읽음 · 각자 지우기, R-326 개선 · 불편(superadmin, 익명이면 신상정보 없음),
/// R-327 전체 쪽지 한 줄 · 대상 · 열 때만 받은 기록 · 취소 · 답장, R-328 이름 찾기(이름 · 부서 · 역할만).
/// </summary>
public partial class MessageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;
    private readonly Dictionary<string, string> _u = [];

    public MessageTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        _elem = InMemoryDb.SeedTenant(_db, "elem", "ELEM01");
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.Tenants.Single(t => t.Id == _kids.Id).Name = "잠실새내교회 유년부";
            raw.Tenants.Single(t => t.Id == _elem.Id).Name = "잠실새내교회 초등부";
            raw.SaveChanges();
        }
        Add("cheolsu", "김철수", _kids, Roles.Participant);
        Add("eunhye", "이은혜", _kids, Roles.Teacher);
        Add("minji", "박민지", _elem, Roles.Participant);
        var sa = InMemoryDb.SeedAccount(_db, "superadmin@example.com", "운영자");
        using (var raw = InMemoryDb.Raw(_db)) { raw.Users.Single(u => u.Id == sa.Id).UserName = "superadmin"; raw.SaveChanges(); }
        InMemoryDb.AddRole(_db, sa.Id, IdentityRoles.SuperAdmin);
        _u["superadmin"] = "superadmin";
        using var r2 = InMemoryDb.Raw(_db);
        foreach (var u in r2.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        r2.SaveChanges();
    }

    /// <summary>1:1 쪽지 한 통(인명 검색 [쪽지]와 같은 서비스 길).</summary>
    private async Task Dm(string from, string to, string body)
    {
        using var scope = _app.Services.CreateScope();
        Assert.Equal(MsgError.None, await scope.ServiceProvider.GetRequiredService<MessageService>().SendAsync(from, to, body, null, CancellationToken.None));
    }

    private void Add(string key, string name, Tenant t, string role)
    {
        var a = InMemoryDb.SeedAccount(_db, key + "@example.com", name);
        InMemoryDb.SeedMembership(_db, t.Id, a.UserName!, MembershipStatus.Approved, role);
        _u[key] = a.UserName!;
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

    private static async Task<HttpResponseMessage> Post(HttpClient c, string page, string url, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync(page)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private MessageService Svc(IServiceScope s) => s.ServiceProvider.GetRequiredService<MessageService>();

    [Fact]
    public async Task R325_다른_부서끼리_쪽지를_주고받고_읽음_답장_각자_지우기()
    {
        var cs = await Login("cheolsu@example.com");
        await cs.GetAsync("/kids");
        Assert.NotEqual(HttpStatusCode.Redirect, (await Post(cs, "/messages/write", "/messages/write", new() { ["To"] = _u["minji"], ["Body"] = "민지야 안녕!" })).StatusCode);   // R-359: 학생은 쓰기 화면에서 처음 쪽지를 못 보낸다
        await Dm(_u["cheolsu"], _u["minji"], "민지야 안녕!");   // 처음 쪽지는 인명 검색 [쪽지]와 같은 길(서비스) — 1:1 쪽지 자체는 그대로
        long id;
        using (var raw = InMemoryDb.Raw(_db)) { var m = raw.Messages.Single(); id = m.Id; Assert.Equal((MessageKinds.Direct, _u["cheolsu"], _u["minji"], "유년부 · 학생"), (m.Kind, m.SenderUserName, m.RecipientUserName, m.SenderLabel ?? "")); }
        var sent = await cs.GetStringAsync("/messages?box=sent");
        Assert.Contains("안 읽음", sent);

        var mj = await Login("minji@example.com");
        var mjHome = await (await mj.GetAsync("/elem")).Content.ReadAsStringAsync();
        Assert.Contains("새 쪽지 1개가 왔어요", mjHome);   // 로그인할 때 한 번 — 띠 + 팝업(확정 시안 쪽지-팝업)
        Assert.Contains("data-mpop", mjHome);
        Assert.Contains("민지야 안녕!", mjHome);   // 팝업 미리 보기
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(mjHome, "48-부서홈-새쪽지띠-접수대");
        var again = await mj.GetStringAsync("/elem");
        Assert.DoesNotContain("새 쪽지 1개가 왔어요", again);
        Assert.DoesNotContain("data-mpop", again);
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(await mj.GetStringAsync("/messages"), "49-받은쪽지");
        var inbox1 = await mj.GetStringAsync("/messages"); Assert.True(Regex.IsMatch(inbox1, @"받은 쪽지\s*<em class=""num"">1</em>"), inbox1.Substring(inbox1.IndexOf("받은 쪽지"), 160));      // 안 읽은 1(받은 쪽지 탭 — 머리말은 ☰ 메뉴 머리말이라 ✉ 뱃지가 없다, 2026-11-21)
        var read = await mj.GetStringAsync($"/messages/{id}");
        Assert.Contains("민지야 안녕!", read);
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(read, "50-쪽지읽기");
        Assert.Contains("답장", read);
        Assert.Contains("읽음", await cs.GetStringAsync("/messages?box=sent"));
        Assert.Equal(HttpStatusCode.NotFound, (await (await Login("eunhye@example.com")).GetAsync($"/messages/{id}")).StatusCode);   // 남의 쪽지는 못 연다

        Assert.Equal(HttpStatusCode.Redirect, (await Post(mj, "/messages/write?reply=" + id, "/messages/write", new() { ["To"] = _u["cheolsu"], ["Reply"] = id.ToString(), ["Body"] = "철수 안녕~" })).StatusCode);
        await Post(mj, "/messages", "/messages?handler=Delete", new() { ["id"] = id.ToString(), ["box"] = "in" });
        Assert.DoesNotContain("민지야 안녕!", await mj.GetStringAsync("/messages"));
        Assert.Contains("민지야 안녕!", await cs.GetStringAsync("/messages?box=sent"));      // 상대 쪽지함에는 남는다
        Assert.Contains("철수 안녕~", await cs.GetStringAsync("/messages"));

        // 로그인 다시 안 해도 새 쪽지가 오면 홈을 새로 열 때(F5) 팝업 한 번 — 같은 쪽지로는 다시 안 뜬다(2026-11-17 사용자)
        Assert.DoesNotContain("data-mpop", await mj.GetStringAsync("/elem"));
        await Dm(_u["cheolsu"], _u["minji"], "또 왔어요");
        var f5 = await mj.GetStringAsync("/elem");
        Assert.Contains("data-mpop", f5);
        Assert.Contains("또 왔어요", f5);
        Assert.DoesNotContain("data-mpop", await mj.GetStringAsync("/elem"));
        Assert.Contains("data-mpop-snooze=\"3\"", f5);   // [다음에 · 3일 후 · 1주일 후]
        Assert.DoesNotContain(">닫기<", f5);

        // [3일 후]: 그 날까지 새 쪽지가 와도 팝업 · 띠가 안 뜬다(오늘 = 2027-01-05)
        var req = new HttpRequestMessage(HttpMethod.Get, "/elem");
        await Dm(_u["cheolsu"], _u["minji"], "세 번째");
        req.Headers.Add("Cookie", $"{MsgPopup.SnoozeCookie}={Uri.EscapeDataString(_u["minji"])}:2027-01-08");
        var snoozed = await (await mj.SendAsync(req)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("data-mpop", snoozed);
        Assert.DoesNotContain("class=\"ms-band\"", snoozed);
    }

    [Fact]
    public async Task R326_개선_쪽지는_superadmin_에게_익명이면_보낸_사람_정보가_없다()
    {
        var cs = await Login("cheolsu@example.com");
        await cs.GetAsync("/kids");
        var home = await cs.GetStringAsync("/kids");
        Assert.Contains("<div class=\"dh-foot\">", home); Assert.Contains("href=\"/kids/feedback\"", home);   // 홈은 상자 하나(2026-11-17 사용자) — 입력은 따로
        Assert.DoesNotContain("name=\"anon\"", home);
        var fb = await cs.GetStringAsync("/kids/feedback");
        Assert.Contains("개선사항 · 불편사항 접수", fb);
        Assert.Contains("실명과 연락처를 함께", fb);
        Assert.Contains("name=\"back\" value=\"/kids/feedback\"", fb);
        var sent = await Post(cs, "/kids/feedback", "/messages?handler=Feedback", new() { ["tid"] = _kids.Id.ToString(), ["body"] = "퀴즈가 너무 어려워요", ["anon"] = "true", ["back"] = "/kids/feedback" });
        Assert.Equal("/kids/feedback#fbT", sent.Headers.Location?.OriginalString);   // 보낸 뒤 같은 화면 위에 알림
        Assert.Contains("익명으로 보냈어요", await cs.GetStringAsync("/kids/feedback"));
        await Post(cs, "/kids/feedback", "/messages?handler=Feedback", new() { ["tid"] = _elem.Id.ToString(), ["body"] = "남의 부서 이름", ["anon"] = "true", ["back"] = "/kids" });   // 내 부서가 아니면 부서도 안 남김
        await Post(cs, "/kids/feedback", "/messages?handler=Feedback", new() { ["tid"] = _kids.Id.ToString(), ["body"] = "글자가 작아요 010-1234", ["anon"] = "false", ["back"] = "/kids" });
        using (var raw = InMemoryDb.Raw(_db))
        {
            var anon = raw.Messages.Single(m => m.Body == "퀴즈가 너무 어려워요");
            Assert.Equal((MessageKinds.Feedback, (string?)null, "superadmin", "유년부"), (anon.Kind, anon.SenderUserName, anon.RecipientUserName, anon.SenderLabel ?? ""));
            Assert.DoesNotContain("김철수", anon.SenderLabel ?? "");
            var other = raw.Messages.Single(m => m.Body == "남의 부서 이름");
            Assert.Null(other.SenderTenantId);
            Assert.Equal(_u["cheolsu"], raw.Messages.Single(m => m.Body.StartsWith("글자가")).SenderUserName);
        }
        var sa = await Login("superadmin@example.com");
        var first = await sa.GetAsync("/platform");   // 할 일이 있으면 할 일 화면으로 먼저(R-309)
        var plat = first.StatusCode == HttpStatusCode.OK ? await first.Content.ReadAsStringAsync() : await sa.GetStringAsync("/platform");
        Assert.Contains("data-mpop", plat);                 // 플랫폼 홈에서도 로그인 후 한 번 팝업(2026-11-17 사용자)
        Assert.Contains("새 쪽지 3개가 왔어요", plat);
        Assert.DoesNotContain("data-mpop", await sa.GetStringAsync("/platform"));
        var inbox = await sa.GetStringAsync("/messages");
        Assert.Contains("익명", inbox);
        Assert.Contains("김철수", inbox);
        Assert.DoesNotContain(">익명 쪽지라", inbox);
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(inbox, "51-superadmin-받은쪽지");
        Assert.DoesNotContain("퀴즈가 너무 어려워요", await cs.GetStringAsync("/messages?box=sent"));   // 익명은 누가 보냈는지 남지 않아 보낸 쪽지함에도 없다
    }

    [Fact]
    public async Task R327_전체_쪽지는_한_줄이고_열_때만_받은_기록_대상_부서_취소_답장()
    {
        using var scope = _app.Services.CreateScope();
        var svc = Svc(scope);
        Assert.Equal(MsgError.NotAllowed, await svc.BroadcastAsync(_u["cheolsu"], [], "학생은 못 보냄"));
        Assert.Equal(MsgError.None, await svc.BroadcastAsync("superadmin", [_kids.Id], "유년부 성탄 행사 안내"));
        Assert.Equal(MsgError.None, await svc.BroadcastAsync("superadmin", [], "모두에게"));
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal(2, raw.Messages.Count(m => m.Kind == MessageKinds.Broadcast));   // 받는 사람이 몇 명이어도 쪽지는 한 줄
            Assert.Empty(raw.MessageReceipts);                                           // 아무도 안 열면 받은 기록도 없다
        }
        Assert.Equal(2, await svc.UnreadAsync(_u["cheolsu"]));
        Assert.Equal(1, await svc.UnreadAsync(_u["minji"]));                             // 초등부는 유년부 안내를 못 받는다
        var kidsMsg = (await svc.InboxAsync(_u["cheolsu"])).Single(r => r.Body.StartsWith("유년부"));
        Assert.Equal(("유년부", "플랫폼 관리자"), (kidsMsg.Target, kidsMsg.Who));
        Assert.Null(await svc.OpenAsync(_u["minji"], kidsMsg.Id));
        await svc.OpenAsync(_u["cheolsu"], kidsMsg.Id);
        await svc.OpenAsync(_u["cheolsu"], kidsMsg.Id);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Single(raw.MessageReceipts);   // 열 때 한 줄, 다시 열어도 그대로
        Assert.Equal(1, await svc.UnreadAsync(_u["cheolsu"]));
        Assert.Equal(MsgError.None, await svc.SendAsync(_u["cheolsu"], "superadmin", "감사합니다", kidsMsg.Id));   // 전체 쪽지에도 답장
        var sent = (await svc.SentAsync("superadmin")).Single(r => r.Id == kidsMsg.Id);
        Assert.Equal((1, 2, 1), (sent.ReadCount, sent.Total, sent.Replies));              // 유년부 2명 중 1명 읽음 · 답장 1
        var sa = await Login("superadmin@example.com");
        Assert.Contains("보내기 취소", await sa.GetStringAsync("/messages?box=sent"));
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(await sa.GetStringAsync("/messages?box=sent"), "53-보낸쪽지-전체");
        var w = await sa.GetStringAsync("/messages/write");
        Assert.Contains("모든 사용자", w);
        if (Environment.GetEnvironmentVariable("CB_SCREENS_DIR") is not null) await ScreenDump.Write(w, "54-전체쪽지쓰기");

        Assert.Equal(MsgError.NotAllowed, await svc.CancelAsync(_u["cheolsu"], kidsMsg.Id));
        Assert.Equal(MsgError.None, await svc.CancelAsync("superadmin", kidsMsg.Id));
        Assert.DoesNotContain(await svc.InboxAsync(_u["cheolsu"]), r => r.Id == kidsMsg.Id);   // 모두에게서 사라진다
        Assert.Contains(await svc.InboxAsync("superadmin"), r => r.Body == "감사합니다");          // 답장은 남는다

        var all = (await svc.InboxAsync(_u["minji"])).Single();
        await svc.DeleteAsync(_u["minji"], all.Id);
        Assert.Empty(await svc.InboxAsync(_u["minji"]));
        Assert.Contains(await svc.InboxAsync(_u["cheolsu"]), r => r.Id == all.Id);              // 지우기는 나만
        var late = InMemoryDb.SeedAccount(_db, "late@example.com", "늦게옴");
        InMemoryDb.SeedMembership(_db, _elem.Id, late.UserName!, MembershipStatus.Approved, Roles.Participant);
        Assert.Contains(await svc.InboxAsync(late.UserName!), r => r.Id == all.Id);             // 나중에 들어온 사람에게도
    }

    [Fact]
    public async Task R359_쓰기는_단체쪽지_교사만_부서_전체에게_답장은_보낸_선생님에게만()
    {
        var cs = await Login("cheolsu@example.com");
        var student = await cs.GetStringAsync("/messages/write");
        Assert.Contains("단체쪽지는 선생님만 보낼 수 있어요", student);
        Assert.DoesNotContain("id=\"ms-w\"", student);   // 쓰기 칸이 없다
        Assert.DoesNotContain("/messages/write\"", await cs.GetStringAsync("/messages"));   // [단체쪽지 쓰기] 버튼도 없다
        Assert.DoesNotContain("박민지", await cs.GetStringAsync("/messages/write?Q=" + Uri.EscapeDataString("박민")));   // 이름으로 찾기는 없다(인명 검색으로)

        var t = await Login("eunhye@example.com");
        Assert.Contains("단체쪽지 쓰기", await t.GetStringAsync("/messages"));
        var w = System.Net.WebUtility.HtmlDecode(await t.GetStringAsync("/messages/write"));
        Assert.Contains("받는 사람: <b>유년부 전체</b>", w);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(t, "/messages/write", "/messages/write", new() { ["Tid"] = _kids.Id.ToString(), ["Body"] = "이번 주 예배는 9시예요" })).StatusCode);
        long id;
        using (var raw = InMemoryDb.Raw(_db)) { var m = raw.Messages.Single(); id = m.Id; Assert.Equal((MessageKinds.Broadcast, _u["eunhye"]), (m.Kind, m.SenderUserName)); Assert.Equal(_kids.Id, raw.MessageTargets.Single().TenantId); }
        var inbox = await cs.GetStringAsync("/messages");
        Assert.Contains("이번 주 예배는 9시예요", inbox);
        Assert.Contains("<span class=\"bc\">단체</span>", inbox);   // 받은 목록 "단체" 꼬리표
        Assert.DoesNotContain("이번 주 예배는 9시예요", await (await Login("minji@example.com")).GetStringAsync("/messages"));   // 다른 부서에는 안 간다
        Assert.NotEqual(HttpStatusCode.Redirect, (await Post(t, "/messages/write", "/messages/write", new() { ["Tid"] = _elem.Id.ToString(), ["Body"] = "남의 부서" })).StatusCode);   // 내 부서만

        // 답장은 보낸 선생님에게만
        Assert.Equal(HttpStatusCode.Redirect, (await Post(cs, $"/messages/write?reply={id}", "/messages/write", new() { ["Reply"] = id.ToString(), ["To"] = _u["minji"], ["Body"] = "네 선생님!" })).StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(_u["eunhye"], raw.Messages.Single(m => m.Body == "네 선생님!").RecipientUserName);   // To 를 바꿔도 보낸 사람에게

        // 보낸 선생님은 [보내기 취소]
        await Post(t, "/messages?box=sent", "/messages?handler=Cancel", new() { ["id"] = id.ToString() });
        Assert.DoesNotContain("이번 주 예배는 9시예요", await cs.GetStringAsync("/messages"));
    }

    [Fact]
    public async Task R329_R340_회원_가입과_부서_참여는_superadmin_에게_자동_알림_쪽지()
    {
        using (var raw = InMemoryDb.Raw(_db)) { foreach (var t in raw.Tenants) t.JoinListed = true; raw.SaveChanges(); }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var tok = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value;
        var r = await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = tok, ["DisplayName"] = "새친구", ["Email"] = "newbie@example.com", ["Password"] = "abcd1234!", ["PasswordConfirm"] = "abcd1234!" }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        await Post(c, "/join", "/join?handler=Apply", new() { ["TenantId"] = _kids.Id.ToString(), ["RoleKind"] = SignupRoleKind.Student });
        using (var raw = InMemoryDb.Raw(_db))
        {
            var sys = raw.Messages.Where(m => m.Kind == MessageKinds.System).OrderBy(m => m.Id).ToList();
            Assert.Equal(2, sys.Count);
            Assert.All(sys, m => Assert.Equal(("superadmin", (string?)null), (m.RecipientUserName, m.SenderUserName)));
            Assert.Contains("새 회원 가입 · 새친구", sys[0].Body);
            Assert.Contains("새친구님이 유년부에 들어왔어요 · 학생", sys[1].Body);   // R-340 승인 없이 바로 · 통보만
        }
        var sa = await Login("superadmin@example.com");
        var inbox = await sa.GetStringAsync("/messages");
        Assert.Contains("도전!!!성경말씀", inbox);
        var id = InMemoryDb.Raw(_db).Messages.First(m => m.Kind == MessageKinds.System).Id;
        Assert.DoesNotContain(">답장<", await sa.GetStringAsync($"/messages/{id}"));   // 자동 알림은 답장 없음
    }

    /// <summary>R-340(2026-11-20 사용자): 부서 참여는 승인 없이 바로 — 그 부서 관리자와 플랫폼 관리자에게만 통보 쪽지가 가고, 다른 부서 관리자 · 일반 교사는 안 받는다.</summary>
    [Fact]
    public async Task R340_부서_참여는_바로_되고_그_부서_관리자와_플랫폼_관리자에게_통보()
    {
        Add("kadmin", "유년관리", _kids, Roles.Teacher);
        Add("eadmin", "초등관리", _elem, Roles.Teacher);
        using (var raw = InMemoryDb.Raw(_db))
        {
            foreach (var u in raw.Users) u.PasswordHash ??= new PasswordHasher<AppUser>().HashPassword(u, "1234");
            foreach (var t in raw.Tenants) t.JoinListed = true;
            raw.SaveChanges();
        }
        foreach (var k in new[] { "kadmin", "eadmin" }) InMemoryDb.AddRole(_db, InMemoryDb.Raw(_db).Users.Single(u => u.UserName == _u[k]).Id, IdentityRoles.Admin);

        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var tok = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value;
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = tok, ["DisplayName"] = "새친구", ["Email"] = "newbie2@example.com", ["Password"] = "abcd1234!", ["PasswordConfirm"] = "abcd1234!" }));
        var r = await Post(c, "/join", "/join?handler=Apply", new() { ["TenantId"] = _kids.Id.ToString(), ["RoleKind"] = SignupRoleKind.Student });
        Assert.Equal("/kids", r.Headers.Location!.OriginalString);

        using var after = InMemoryDb.Raw(_db);
        var me = after.Users.Single(u => u.Email == "newbie2@example.com");
        Assert.Equal(_kids.Id, after.DeptRoles.Single(d => d.UserId == me.Id).TenantId);   // 승인 없이 바로 소속
        var to = after.Messages.Where(m => m.Kind == MessageKinds.System && m.Body.Contains("유년부에 들어왔어요")).Select(m => m.RecipientUserName).ToList();
        Assert.Contains(_u["kadmin"], to);
        Assert.Contains("superadmin", to);
        Assert.DoesNotContain(_u["eadmin"], to);   // 다른 부서 관리자는 안 받는다
        Assert.DoesNotContain(_u["eunhye"], to);   // 관리자 아닌 교사도 안 받는다
    }

    // R-358: 인명 검색 — 교사 · 관리자만, 유년부 /AddressBook 과 같은 검색 · 쪽지

    [Fact]
    public async Task R358_인명_검색은_교사만_2글자부터_이_부서_사람을_찾고_한_사람에게_쪽지()
    {
        var cs = await Login("cheolsu@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await cs.GetAsync("/kids/people")).StatusCode);   // 학생은 못 본다(개인정보)
        Assert.DoesNotContain("href=\"/kids/people\"", await cs.GetStringAsync("/kids"));

        var t = await Login("eunhye@example.com");
        var page = await t.GetStringAsync("/kids/people");
        Assert.Contains("이름 또는 아이디 (2글자부터)", page);
        Assert.Contains("/js/people.", page);
        Assert.Contains("href=\"/kids/people\"", await t.GetStringAsync("/kids/office"));   // 행정업무 화면(2026-11-25)

        Assert.Contains("\"items\":[]", await t.GetStringAsync("/kids/people?handler=Search&q=" + Uri.EscapeDataString("철")));   // 1글자는 빈 결과
        var hit = Regex.Unescape(await t.GetStringAsync("/kids/people?handler=Search&q=" + Uri.EscapeDataString("김철")));   // JSON 의 \uXXXX 를 풀어서
        Assert.Contains("\"fullName\":\"김철수\"", hit);
        Assert.Contains($"\"userName\":\"{_u["cheolsu"]}\"", hit);
        Assert.Contains("\"student\":true", hit);
        Assert.DoesNotContain("박민지", Regex.Unescape(await t.GetStringAsync("/kids/people?handler=Search&q=" + Uri.EscapeDataString("박민"))));   // 다른 부서 사람은 안 나온다

        var form = new Dictionary<string, string> { ["ToUserName"] = _u["cheolsu"], ["MessageText"] = "철수야 잘 지내지?" };
        var r = await Post(t, "/kids/people", "/kids/people?handler=Send", form);
        Assert.Contains("\"ok\":true", await r.Content.ReadAsStringAsync());
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal((MessageKinds.Direct, _u["eunhye"], _u["cheolsu"]), raw.Messages.Where(m => m.Body == "철수야 잘 지내지?").Select(m => new { m.Kind, m.SenderUserName, m.RecipientUserName }).AsEnumerable().Select(m => (m.Kind, m.SenderUserName!, m.RecipientUserName!)).Single());
        var other = await Post(t, "/kids/people", "/kids/people?handler=Send", new() { ["ToUserName"] = _u["minji"], ["MessageText"] = "다른 부서" });
        Assert.Contains("\"ok\":false", await other.Content.ReadAsStringAsync());   // 이 부서 명부의 계정에게만
        var big = await Post(t, "/kids/people", "/kids/people?handler=Send", new() { ["ToUserName"] = _u["cheolsu"], ["MessageText"] = new string('가', 501) });
        Assert.Contains("\"ok\":false", await big.Content.ReadAsStringAsync());   // 500자까지
    }

    // R-360: 쪽지 화면은 ☰ 메뉴 머리말 · 읽기 버튼 오른쪽 [나만 삭제] · 보낸 사람 [보내기 취소](확인 상자)

    [Fact]
    public async Task R360_쪽지_읽기는_나만_삭제와_보내기_취소_버튼이고_머리말은_메뉴판이다()
    {
        var t = await Login("eunhye@example.com");
        await Post(t, "/messages/write", "/messages/write", new() { ["Tid"] = _kids.Id.ToString(), ["Body"] = "취소할 단체쪽지" });
        long id; using (var raw = InMemoryDb.Raw(_db)) id = raw.Messages.Single().Id;

        var mine = await t.GetStringAsync($"/messages/{id}");
        Assert.Contains("data-pnav-open", mine);   // ☰ 메뉴 머리말(부서 메뉴판)
        Assert.Contains("id=\"pnav\"", mine);
        Assert.Contains("class=\"cur\">단체쪽지</b>", mine);   // 머리줄 표준(2026-11-24): ☰ + 부서 › 단체쪽지
        Assert.Contains(">나만 삭제</button>", mine);
        Assert.DoesNotContain(">지우기</button>", mine);
        Assert.Contains($"href=\"/messages/{id}?ask=cancel\"", mine);   // [보내기 취소]
        Assert.DoesNotContain("모든 사람의 쪽지함에서 사라져요", mine);   // 누르기 전에는 확인 상자가 없다

        var ask = await t.GetStringAsync($"/messages/{id}?ask=cancel");
        Assert.Contains("이 단체쪽지를 취소할까요?", ask);
        Assert.Contains("모든 사람의 쪽지함에서 사라져요", ask);

        var cs = await Login("cheolsu@example.com");   // 받은 학생: [답장] · [나만 삭제]만
        var got = await cs.GetStringAsync($"/messages/{id}");
        Assert.Contains(">나만 삭제</button>", got);
        Assert.DoesNotContain("ask=cancel", got);
        Assert.DoesNotContain("이 단체쪽지를 취소할까요?", await cs.GetStringAsync($"/messages/{id}?ask=cancel"));   // 남의 쪽지는 취소 상자가 없다

        await Post(t, $"/messages/{id}?ask=cancel", "/messages?handler=Cancel", new() { ["id"] = id.ToString() });
        var after = await t.GetStringAsync($"/messages/{id}");
        Assert.Contains("보내기를 취소했어요", after);
        Assert.DoesNotContain("ask=cancel", after);   // 취소한 뒤에는 버튼이 없다
        Assert.Contains(">나만 삭제</button>", after);
        Assert.DoesNotContain("취소할 단체쪽지", await cs.GetStringAsync("/messages"));
    }
}
