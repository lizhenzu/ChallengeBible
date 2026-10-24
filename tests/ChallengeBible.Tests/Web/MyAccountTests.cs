using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>내 계정(확정 시안: 내계정): 이름 바꾸기 · 지금 비밀번호 확인 · 내 부서. 임시 비밀번호 강제 변경(0035)은 지금 비밀번호를 묻지 않는다.</summary>
public partial class MyAccountTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;

    public MyAccountTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
    }

    private AccountService Accounts() => new(new InMemoryDb(_db, new RequestScope()), TimeProvider.System);

    private int Account(string email, string name, string password = "1234", bool mustChange = false)
    {
        var a = InMemoryDb.SeedAccount(_db, email, name);
        using var raw = InMemoryDb.Raw(_db);
        var row = raw.Accounts.Single(x => x.Id == a.Id);
        row.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Account>().HashPassword(row, password);
        row.MustChangePassword = mustChange;
        raw.SaveChanges();
        return a.Id;
    }

    [Fact]
    public async Task 이름은_1_50자_비밀번호는_지금_비밀번호가_맞아야_바뀐다()
    {
        var id = Account("cheolsu@example.com", "김철수");
        Assert.Equal("DisplayName", (await Accounts().RenameAsync(id, "  ")).Errors.Keys.Single());
        Assert.Equal("DisplayName", (await Accounts().RenameAsync(id, new string('가', 51))).Errors.Keys.Single());
        Assert.Equal("김철수2", (await Accounts().RenameAsync(id, " 김철수2 ")).Name);

        Assert.Equal("Current", (await Accounts().ChangeOwnPasswordAsync(id, "틀림", "5678", "5678")).Keys.Single());
        Assert.Equal("PasswordConfirm", (await Accounts().ChangeOwnPasswordAsync(id, "1234", "5678", "9999")).Keys.Single());
        Assert.True((await Accounts().ChangeOwnPasswordAsync(id, "1234", "5678", "5678")).Ok);
        Assert.Null((await Accounts().LoginAsync("cheolsu@example.com", "5678")).Error);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string email, string password = "1234")
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var h = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(h).Groups[1].Value, ["Email"] = email, ["Password"] = password,
        }));
        return c;
    }

    private static Task<HttpResponseMessage> Post(HttpClient c, string url, string token, params (string, string)[] form) =>
        c.PostAsync(url, new FormUrlEncodedContent(form.Append(("__RequestVerificationToken", token)).Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))));

    [Fact]
    public async Task 화면_흐름_내_부서와_역할을_보고_이름을_바꾸면_머리말도_바뀐다()
    {
        var id = Account("younghee@example.com", "김영희");
        InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, Roles.Participant, Roles.Guardian);
        InMemoryDb.SeedMembership(_db, _elem.Id, id, MembershipStatus.Approved, Roles.Guardian);
        using (var raw = InMemoryDb.Raw(_db)) { raw.Accounts.Single(a => a.Id == id).FamilyTenantId = _kids.Id; raw.SaveChanges(); }

        var c = await LoggedIn("younghee@example.com");
        var page = await c.GetStringAsync("/account");
        Assert.Contains("younghee@example.com", page);
        Assert.Contains("참여자 · 보호자", page);
        Assert.Contains("가족과 함께", page);
        Assert.Contains("href=\"/account/family\"", page);

        var r = await Post(c, "/account?handler=Name", TokenRx().Match(page).Groups[1].Value, ("DisplayName", "김영희 엄마"));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var after = await c.GetStringAsync("/account?Ok=name");
        Assert.Contains("이름을 바꿨어요", after);
        Assert.Contains("<b>김영희 엄마</b>님", after);   // 쿠키 이름도 새로

        var bad = await (await Post(c, "/account?handler=Password", TokenRx().Match(after).Groups[1].Value, ("Current", "틀림"), ("Password", "5678"), ("PasswordConfirm", "5678"))).Content.ReadAsStringAsync();
        Assert.Contains("지금 비밀번호가 맞지 않아요", bad);
    }

    [Fact]
    public async Task 비밀번호_바꾸기_화면은_스스로_바꿀_때만_지금_비밀번호를_묻는다()
    {
        Account("cheolsu@example.com", "김철수");
        var c = await LoggedIn("cheolsu@example.com");
        var page = await c.GetStringAsync("/account/change-password");
        Assert.Contains("지금 비밀번호", page);
        var r = await Post(c, "/account/change-password", TokenRx().Match(page).Groups[1].Value, ("Current", ""), ("Password", "5678"), ("PasswordConfirm", "5678"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);   // 지금 비밀번호 없이는 안 바뀜

        Account("temp@example.com", "임시", mustChange: true);   // 0035 임시 비밀번호
        var t = await LoggedIn("temp@example.com");
        var forced = await t.GetStringAsync("/account/change-password");
        Assert.DoesNotContain("지금 비밀번호", forced);
        var ok = await Post(t, "/account/change-password", TokenRx().Match(forced).Groups[1].Value, ("Password", "5678"), ("PasswordConfirm", "5678"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
    }
}
