using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>참여 신청 v2: 목록에서 부서 고르기(0059). 목록은 플랫폼이 켠 운영 중 부서만, 숨긴 부서는 코드가 있어야 신청된다.</summary>
public partial class JoinListTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem, _hidden, _stopped;

    public JoinListTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _hidden = InMemoryDb.SeedTenant(_db, "hanbit-mid", "HBMID1");
        _stopped = InMemoryDb.SeedTenant(_db, "old-dept", "OLDDPT", TenantStatus.Suspended);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var t in raw.Tenants.Where(t => t.Id != _hidden.Id)) t.JoinListed = true;
        raw.SaveChanges();
    }

    /// <summary>요청마다 하나: 서비스가 정하는 영역을 DB 연결도 같이 쓴다.</summary>
    private JoinService Service()
    {
        var scope = new RequestScope();
        return new(new InMemoryDb(_db, scope), scope, TimeProvider.System);
    }

    [Fact]
    public async Task 목록은_보이기를_켠_운영_중_부서만()
    {
        var listed = await Service().ListedAsync();
        Assert.Equal(new[] { _elem.Id, _kids.Id }.Order(), listed.Select(t => t.TenantId).Order());
    }

    /// <summary>2026-11-17 사용자: 나이순(영아부 → … → 고등부) · 세 프로그램(필사 포함)이 카드에 보인다.</summary>
    [Fact]
    public async Task 목록은_나이순이고_필사도_보인다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.Tenants.Single(t => t.Id == _kids.Id).Name = "잠실새내교회 고등부";
            raw.Tenants.Single(t => t.Id == _elem.Id).Name = "잠실새내교회 영아부";
            raw.SaveChanges();
        }
        var listed = await Service().ListedAsync();
        Assert.Equal(new[] { _elem.Id, _kids.Id }, listed.Select(t => t.TenantId));   // 영아부가 고등부보다 먼저
        Assert.All(listed, t => Assert.True(t.CopyEnabled));
    }

    [Fact]
    public async Task 숨긴_부서는_Id_만으로는_신청할_수_없고_코드가_맞으면_된다()
    {
        var a = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수");
        var b = InMemoryDb.SeedAccount(_db, "minjun@example.com", "최민준");
        Assert.Equal(JoinError.NotFound, (await Service().ApplyAsync(a.UserName!, _hidden.Id, null, SignupRoleKind.Student)).Error);
        Assert.Equal(JoinError.NotFound, (await Service().ApplyAsync(a.UserName!, _hidden.Id, "JSKIDS", SignupRoleKind.Student)).Error);   // 다른 부서 코드
        Assert.Equal(JoinError.NotFound, (await Service().ApplyAsync(a.UserName!, _stopped.Id, "OLDDPT", SignupRoleKind.Student)).Error);  // 정지된 부서
        Assert.Equal(JoinError.None, (await Service().ApplyAsync(a.UserName!, _hidden.Id, "hbmid1", SignupRoleKind.Student)).Error);
        Assert.Equal(JoinError.None, (await Service().ApplyAsync(b.UserName!, _kids.Id, null, SignupRoleKind.Parent)).Error);              // 목록 부서는 Id 로
        Assert.Equal(JoinError.AlreadyMember, (await Service().ApplyAsync(b.UserName!, _kids.Id, null, SignupRoleKind.Student)).Error);
        Assert.Equal(JoinError.BadRole, (await Service().ApplyAsync(b.UserName!, _elem.Id, null, "X")).Error);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    [Fact]
    public async Task 화면에서_목록으로_골라_신청한다()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value;
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["DisplayName"] = "김철수", ["Email"] = "cheolsu@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));

        var html = await c.GetStringAsync("/join");
        Assert.Contains($"name=\"TenantId\" value=\"{_kids.Id}\"", html);
        Assert.DoesNotContain($"name=\"TenantId\" value=\"{_hidden.Id}\"", html);     // 숨긴 부서는 목록에 없다
        Assert.DoesNotContain("OLDDPT", html);
        Assert.Contains("잠실새내교회", html);

        // 이름으로 거르기
        Assert.DoesNotContain($"name=\"TenantId\" value=\"{_kids.Id}\"", await c.GetStringAsync("/join?Q=elem"));

        // 부서 주소(코드)로 들어오면 숨긴 부서도 골라진 채로 보인다
        var byCode = await c.GetStringAsync("/join?code=HBMID1");
        Assert.Contains($"value=\"{_hidden.Id}\" checked=\"checked\"", byCode);

        // v3 · v4(확정 시안 부서참여신청-v4): 찾기 칸 · 코드 칸 · 역할 고르기 · 안내 상자가 없고, 아래에 늘 두 버튼
        Assert.DoesNotContain("name=\"Q\"", html);
        Assert.DoesNotContain("부서 코드로 찾기", html);
        Assert.DoesNotContain("name=\"SelectedRoles\"", html);
        Assert.DoesNotContain("내 부서가 안 보이나요", html);
        Assert.DoesNotContain("내 부서로 가기", html);
        Assert.Contains("부서 관리자가 승인하면 시작해요", html);
        Assert.Contains("어느 부서에서 함께할까요?", html);
        Assert.Contains("도전!!!성경말씀", html);
        Assert.Contains(">돌아가기</a>", html);
        Assert.Contains(">부서 참여하기</button>", html);

        var r = await c.PostAsync("/join?handler=Apply", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", TokenRx().Match(html).Groups[1].Value),
            new("TenantId", _kids.Id.ToString()), new("RoleKind", SignupRoleKind.Parent),
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var after = await c.GetStringAsync("/join");
        Assert.Contains("승인을 기다리고 있어요", after);
        Assert.Contains("승인 기다리는 중", after);
        using var raw = InMemoryDb.Raw(_db);
        var m = raw.SignupApplications.Single(x => x.TenantId == _kids.Id && x.Status == SignupStatus.Pending);
        Assert.Equal(SignupRoleKind.Parent, m.RoleKind);
        Assert.DoesNotContain("어느 부서에서", after);                                // 기다리는 동안엔 목록 대신 기다림 카드
    }

    /// <summary>2026-11-18 사용자: 이미 다른 부서 소속(한 사람 한 부서, ADR 0090)이면 목록 · [부서 참여하기] 대신 "이미 함께하고 있어요" + [○○로 가기].</summary>
    [Fact]
    public async Task 이미_다른_부서_소속이면_목록_대신_내_부서_안내가_나온다()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value;
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["DisplayName"] = "김경결", ["Email"] = "gk@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        string userName;
        using (var raw = InMemoryDb.Raw(_db)) userName = raw.Users.Single(u => u.Email == "gk@example.com").UserName!;
        InMemoryDb.SeedMembership(_db, _elem.Id, userName, MembershipStatus.Approved, Roles.Participant);

        var html = await c.GetStringAsync("/join");
        Assert.Contains("이미 부서에 함께하고 있어요", html);
        Assert.Contains("이미 소속", html);
        Assert.Contains("<span class=\"st\">학생</span>", html);                       // T1001
        Assert.Contains($"href=\"/{_elem.Slug}\"", html);                               // [○○로 가기]
        Assert.DoesNotContain(">부서 참여하기</button>", html);
        Assert.DoesNotContain("name=\"TenantId\"", html);

        // 그래도 다른 부서로 보내면: "이 부서에"가 아니라 실제 소속 부서 이름으로 알린다
        var r = await c.PostAsync("/join?handler=Apply", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", TokenRx().Match(html).Groups[1].Value),
            new("TenantId", _kids.Id.ToString()), new("RoleKind", SignupRoleKind.Student),
        }));
        var body = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("이미 이 부서에", body);
    }
}
