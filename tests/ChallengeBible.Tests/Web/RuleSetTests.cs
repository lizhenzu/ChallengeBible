using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>기본 규칙 버전(ADR 0060, 0032). 오늘 = 2027-01-05 화요일(서울).</summary>
public partial class RuleSetTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly int _op, _admin;

    public RuleSetTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자").Id;
        _admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜").Id;
        using var raw = InMemoryDb.Raw(_db);
        var op = raw.Accounts.Single(a => a.Id == _op);
        op.IsPlatformAdmin = true;
        op.PasswordHash = new PasswordHasher<Account>().HashPassword(op, "1234");
        raw.SaveChanges();
    }

    private PlatformRuleService Platform(IMemoryCache cache) =>
        new(new InMemorySystemDb(_db), new RuleService(new InMemoryDb(_db, new RequestScope()), cache), new TenantClock(_time), _time, cache);

    private void Insert(int no, DateOnly from, RuleSet r)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = no, EffectiveFrom = from, Values = RuleService.ToJson(r), ChangedUtc = DateTime.UtcNow, ChangedByAccountId = _op });
        raw.SaveChanges();
    }

    private static RuleSet WordMe(int points) => RuleSet.Default with
    {
        Items = RuleSet.Default.Items.Select(i => i.Item == MissionItem.WordMe ? i with { Points = points } : i).ToList(),
    };

    [Fact]
    public async Task 표가_비면_기본값_v1_JSON으로_그대로_돌아온다()
    {
        var rules = InMemoryDb.Rules(_db);
        var v = Assert.Single(await rules.VersionsAsync());
        Assert.Equal(1, v.VersionNo);
        var back = RuleService.FromJson(RuleService.ToJson(RuleSet.Default));
        Assert.Equal(RuleSet.Default.Items, back.Items);
        Assert.Equal(RuleSet.Default.Milestones, back.Milestones);
        Assert.Equal(RuleSet.Default.FamilyMilestones, back.FamilyMilestones);
        Assert.Equal(RuleSet.Default.Features, back.Features);
    }

    [Fact]
    public async Task R0060_그날의_규칙은_적용_시작일이_지난_버전_중_번호가_가장_큰_것()
    {
        Insert(2, new DateOnly(2027, 1, 10), WordMe(2));
        Insert(3, new DateOnly(2027, 1, 6), WordMe(3));            // 나중에 정한 것이 이긴다
        var rules = InMemoryDb.Rules(_db);
        Assert.Equal(1, (await rules.ForDateAsync(Jan5)).Item(MissionItem.WordMe).Points);
        Assert.Equal(3, (await rules.ForDateAsync(new DateOnly(2027, 1, 7))).Item(MissionItem.WordMe).Points);
        Assert.Equal(3, (await rules.ForDateAsync(new DateOnly(2027, 1, 12))).Item(MissionItem.WordMe).Points);
        Assert.Equal(3, (await rules.ForDateAsync(new DateOnly(2027, 1, 12))).Version);
    }

    [Fact]
    public async Task 미션_점수는_그_미션_날짜에_적용된_버전을_쓴다_이미_쌓인_점수는_그대로()
    {
        var kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var kid = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").Id;
        var m = InMemoryDb.SeedMembership(_db, kids.Id, kid, MembershipStatus.Approved, Roles.Participant);
        int pid;
        using (var raw = InMemoryDb.Raw(_db)) { var p = new Participant { TenantId = kids.Id, MembershipId = m.Id }; raw.Participants.Add(p); raw.SaveChanges(); pid = p.Id; }
        ContentSeed.Word(_db, Jan5.AddDays(-1));
        ContentSeed.Word(_db, Jan5);
        var scope = new RequestScope();
        scope.SetTenant(kids.Id);
        MissionService Svc()
        {
            var tc = new TenantContext();
            using var raw = InMemoryDb.Raw(_db);
            tc.Set(raw.Tenants.Single(t => t.Id == kids.Id), m);
            return new MissionService(new InMemoryDb(_db, scope), tc, new TenantClock(_time), _time, InMemoryDb.Rules(_db));
        }

        Assert.Equal(1, (await Svc().PressAsync(pid, MissionItem.WordMe, Jan5.AddDays(-1))).Added);   // 어제(인정 기간) = v1
        Insert(2, Jan5, WordMe(3));
        Assert.Equal(3, (await Svc().PressAsync(pid, MissionItem.WordMe, Jan5)).Added);               // 오늘 = v2
        using var check = InMemoryDb.Raw(_db);
        Assert.Equal([1, 3], check.PointLedgers.OrderBy(l => l.MissionDate).Select(l => l.Points).ToArray());
    }

    [Fact]
    public async Task 새_버전은_내일이나_다음_주부터_저장되고_기록이_남는다()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = Platform(cache);
        var next = RuleSet.Default with { GraceDays = 4 };
        var (saved, errors) = await svc.SaveAsync(_op, next, new DateOnly(2027, 1, 6));
        Assert.Empty(errors);
        Assert.Equal((2, new DateOnly(2027, 1, 6)), (saved!.VersionNo, saved.EffectiveFrom));
        var rules = new RuleService(new InMemoryDb(_db, new RequestScope()), cache);
        Assert.Equal(3, (await rules.ForDateAsync(Jan5)).GraceDays);                        // 오늘은 그대로
        Assert.Equal(4, (await rules.ForDateAsync(new DateOnly(2027, 1, 6))).GraceDays);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal("v2 · 1월 6일부터 · 인정 기간 3 → 4일", raw.AuditLogs.Single().Detail);

        Assert.Equal((new DateOnly(2027, 1, 6), new DateOnly(2027, 1, 10)), svc.StartChoices(RuleSet.Default));
    }

    [Fact]
    public async Task R0032_틀린_값은_저장하지_않는다()
    {
        var svc = Platform(new MemoryCache(new MemoryCacheOptions()));
        var tomorrow = new DateOnly(2027, 1, 6);
        async Task<List<string>> Err(RuleSet r, DateOnly? from = null) => (await svc.SaveAsync(_op, r, from ?? tomorrow)).Errors;

        Assert.Contains("바뀐 값이 없어요.", await Err(RuleSet.Default));
        Assert.Contains(await Err(RuleSet.Default with { GraceDays = 8 }), e => e.Contains("인정 기간"));
        Assert.Contains(await Err(RuleSet.Default with { WeekStart = DayOfWeek.Monday }), e => e.Contains("주 시작"));
        var noWord = RuleSet.Default with { Items = RuleSet.Default.Items.Select(i => i.Item.Mission() == Mission.Word ? i with { Enabled = false } : i).ToList() };
        Assert.Contains(await Err(noWord), e => e.Contains("Word"));
        Assert.Contains(await Err(RuleSet.Default with { GraceDays = 4 }, Jan5), e => e.Contains("적용 시작"));   // 오늘부터는 안 된다
        Assert.Contains(await Err(RuleSet.Default with { Milestones = [3000, 3000] }), e => e.Contains("축하 단계"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.SaveAsync(_admin, RuleSet.Default with { GraceDays = 4 }, tomorrow));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.PlatformRuleSetVersions);
    }

    // ---- 화면 흐름 ----

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    [Fact]
    public async Task 운영자는_바뀐_점을_보고_새_버전으로_저장한다()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(login).Groups[1].Value, ["Email"] = "operator@example.com", ["Password"] = "1234",
        }));
        var page = await c.GetStringAsync("/platform/rules");
        Assert.Contains("aria-current=\"page\">기본 규칙", page);
        Assert.Contains("v1", page);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value,
            ["Form.GraceDays"] = "4", ["Form.QuizMaxAttempts"] = "2", ["Form.Milestones"] = "3000, 5000, 8000", ["Form.From"] = "tomorrow",
            ["Form.Ranking"] = "true", ["Form.FamilyTeam"] = "true", ["Form.TranslationCompare"] = "true",
        };
        var i = 0;
        foreach (var it in RuleSet.Default.Items)
        {
            form[$"Form.Items[{i}].Code"] = it.Item.Code(); form[$"Form.Items[{i}].Enabled"] = "true";
            form[$"Form.Items[{i}].Name"] = it.DisplayName; form[$"Form.Items[{i}].Points"] = it.Points.ToString(); i++;
        }
        i = 0;
        foreach (var f in RuleSet.Default.FamilyMilestones)
        {
            form[$"Form.Family[{i}].Points"] = f.Points.ToString(); form[$"Form.Family[{i}].Kind"] = f.Kind.ToString();
            form[$"Form.Family[{i}].Message"] = f.Message; form[$"Form.Family[{i}].Emoji"] = f.Emoji; form[$"Form.Family[{i}].Enabled"] = "true"; i++;
        }
        var preview = await (await c.PostAsync("/platform/rules?handler=Preview", new FormUrlEncodedContent(form))).Content.ReadAsStringAsync();
        Assert.Contains("인정 기간 3 → 4일", preview);
        var save = await c.PostAsync("/platform/rules?handler=Save", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var after = await c.GetStringAsync(save.Headers.Location!.OriginalString);
        Assert.Contains("v2로 저장했어요", after);
        Assert.Contains("v2은 1월 6일부터 적용돼요", after);
    }
}
