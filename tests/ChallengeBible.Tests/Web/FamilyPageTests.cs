using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>플랫폼 온가족 상세 `/platform/families/{id}`(확정 시안: 플랫폼-온가족 "온가족 상세", R-175 · R-179). 플랫폼 관리자만.</summary>
public partial class FamilyPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;
    private readonly int _mom, _cheolsu, _minji, _family;

    public FamilyPageTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _mom = InMemoryDb.SeedAccount(_db, "younghee@example.com", "김영희").Id;
        _cheolsu = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").Id;
        _minji = InMemoryDb.SeedAccount(_db, "minji@example.com", "김민지").Id;
        InMemoryDb.SeedMembership(_db, _kids.Id, _mom, MembershipStatus.Approved, Roles.Guardian);
        InMemoryDb.SeedMembership(_db, _elem.Id, _mom, MembershipStatus.Approved, Roles.Guardian);
        InMemoryDb.SeedMembership(_db, _kids.Id, _cheolsu, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _elem.Id, _minji, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        var f = new Family { CreatedUtc = DateTime.UtcNow, CreatedByAccountId = _mom };
        f.Links.Add(new FamilyLink { AccountId = _mom, Relation = FamilyRelation.Mother, SortOrder = 0, AddedUtc = DateTime.UtcNow, AddedByAccountId = _mom });
        f.Links.Add(new FamilyLink { AccountId = _cheolsu, Relation = FamilyRelation.Student, SortOrder = 1, AddedUtc = DateTime.UtcNow, AddedByAccountId = _mom });
        raw.Families.Add(f);
        raw.SaveChanges();
        _family = f.Id;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string email, bool platformAdmin)
    {
        var a = InMemoryDb.SeedAccount(_db, email, email[..email.IndexOf('@')]);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var row = raw.Accounts.Single(x => x.Id == a.Id);
            row.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Account>().HashPassword(row, "1234");
            row.IsPlatformAdmin = platformAdmin;
            raw.SaveChanges();
        }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var h = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(h).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private string Url => $"/platform/families/{_family}";

    private async Task<HttpResponseMessage> Post(HttpClient c, string handler, Dictionary<string, string> form)
    {
        var h = await c.GetStringAsync(Url);
        form["__RequestVerificationToken"] = TokenRx().Match(h).Groups[1].Value;
        return await c.PostAsync($"{Url}?handler={handler}", new FormUrlEncodedContent(form));
    }

    private List<FamilyLink> Links()
    {
        using var raw = InMemoryDb.Raw(_db);
        return raw.FamilyLinks.Where(l => l.FamilyId == _family).OrderBy(l => l.SortOrder).ToList();
    }

    [Fact]
    public async Task 상세는_플랫폼_관리자만_보고_구성원_모든_부서와_보호자의_시상_부서_고르기를_보인다()
    {
        var op = await LoggedIn("operator@example.com", platformAdmin: true);
        var html = await op.GetStringAsync(Url);
        Assert.Contains("김철수네 온가족", html);                          // 이름이 비면 첫 학생 이름
        Assert.Contains("처음부터 모은 점수", html);
        Assert.Contains("시상 부서 — 아직 안 정함", html);                 // 보호자 · 아직 안 정함(R-179)
        Assert.Contains("정하기 전에는 어느 부서에서도", html);
        Assert.Single(Regex.Matches(html, "class=\"pf-aw"));               // 철수(참여자)는 시상 부서 칸 없음
        Assert.Contains("온가족 해체", html);

        Assert.Equal(404, (int)(await op.GetAsync("/platform/families/9999")).StatusCode);
        var user = await LoggedIn("someone@example.com", platformAdmin: false);
        Assert.StartsWith("/account/denied", (await user.GetAsync(Url)).Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task 저장은_이름과_관계를_바꾸고_화살표는_순서만_바꾼다()
    {
        var op = await LoggedIn("operator@example.com", platformAdmin: true);
        var r = await Post(op, "Save", new() { ["name"] = "  우리집  ", [$"rel_{_cheolsu}"] = FamilyRelation.Other, [$"rel_{_mom}"] = "nope" });
        Assert.Equal($"{Url}?ok=saved", r.Headers.Location!.OriginalString);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal("우리집", raw.Families.Single(f => f.Id == _family).Name);
        Assert.Equal([(_mom, FamilyRelation.Mother), (_cheolsu, FamilyRelation.Other)], Links().Select(l => (l.AccountId, l.Relation)));   // 모르는 관계는 그대로

        await Post(op, "Move", new() { ["account"] = _cheolsu.ToString(), ["up"] = "true" });
        Assert.Equal([_cheolsu, _mom], Links().Select(l => l.AccountId));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal("우리집", raw.Families.Single(f => f.Id == _family).Name);   // 이름은 그대로
        await Post(op, "Move", new() { ["account"] = _cheolsu.ToString(), ["up"] = "true" });   // 맨 위에서 더 위로 → 그대로
        Assert.Equal([_cheolsu, _mom], Links().Select(l => l.AccountId));
    }

    [Fact]
    public async Task 더_넣기와_빼기_마지막_사람을_빼면_해체된다()
    {
        var op = await LoggedIn("operator@example.com", platformAdmin: true);
        var found = await op.GetStringAsync($"{Url}?q=minji");
        Assert.Contains("김민지", found);
        var r = await Post(op, "Add", new() { ["account"] = _minji.ToString() });
        Assert.Equal($"{Url}?ok=added", r.Headers.Location!.OriginalString);
        Assert.Equal([_mom, _cheolsu, _minji], Links().Select(l => l.AccountId));

        await Post(op, "Remove", new() { ["account"] = _minji.ToString() });
        await Post(op, "Remove", new() { ["account"] = _cheolsu.ToString() });
        r = await Post(op, "Remove", new() { ["account"] = _mom.ToString() });
        Assert.Equal("/platform/families?ok=dissolved", r.Headers.Location!.OriginalString);
        using var raw = InMemoryDb.Raw(_db);
        Assert.False(raw.Families.Any(f => f.Id == _family));
    }

    [Fact]
    public async Task 이미_다른_온가족인_사람은_넣지_못한다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var other = new Family { CreatedUtc = DateTime.UtcNow, CreatedByAccountId = _minji };
            other.Links.Add(new FamilyLink { AccountId = _minji, Relation = FamilyRelation.Student, AddedUtc = DateTime.UtcNow, AddedByAccountId = _minji });
            raw.Families.Add(other);
            raw.SaveChanges();
        }
        var op = await LoggedIn("operator@example.com", platformAdmin: true);
        Assert.Contains("다른 온가족에 있음", await op.GetStringAsync($"{Url}?q=minji"));
        var r = await Post(op, "Add", new() { ["account"] = _minji.ToString() });
        Assert.Contains("이미 다른 온가족에 든 사람이에요", await r.Content.ReadAsStringAsync());
        Assert.Equal(2, Links().Count);
    }

    [Fact]
    public async Task 시상_부서는_보호자가_승인된_부서만_정할_수_있다()
    {
        var op = await LoggedIn("operator@example.com", platformAdmin: true);
        var bad = await Post(op, "Award", new() { ["account"] = _cheolsu.ToString(), ["tenant"] = _kids.Id.ToString() });   // 보호자 아님
        Assert.Contains("고를 수 있는 부서가 아니에요", await bad.Content.ReadAsStringAsync());
        bad = await Post(op, "Award", new() { ["account"] = _mom.ToString(), ["tenant"] = "0" });
        Assert.Contains("고를 수 있는 부서가 아니에요", await bad.Content.ReadAsStringAsync());

        var r = await Post(op, "Award", new() { ["account"] = _mom.ToString(), ["tenant"] = _elem.Id.ToString() });
        Assert.Equal($"{Url}?ok=award", r.Headers.Location!.OriginalString);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(_elem.Id, raw.Accounts.Single(a => a.Id == _mom).FamilyTenantId);
        Assert.Contains("시상 부서(이 부서 가족팀에서만 축하 · 선물)", await op.GetStringAsync(Url));
    }

    [Fact]
    public async Task 해체는_묶음만_풀고_목록으로_간다()
    {
        var op = await LoggedIn("operator@example.com", platformAdmin: true);
        var r = await Post(op, "Dissolve", []);
        Assert.Equal("/platform/families?ok=dissolved", r.Headers.Location!.OriginalString);
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.False(raw.Families.Any(f => f.Id == _family));
            Assert.False(raw.FamilyLinks.Any());
            Assert.Equal(4, raw.Memberships.Count());   // 부서 소속은 그대로
        }
        Assert.Contains("온가족을 해체했어요", await op.GetStringAsync("/platform/families?ok=dissolved"));
    }
}
