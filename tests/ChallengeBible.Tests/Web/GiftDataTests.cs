using ChallengeBible.Domain.Rules;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ChallengeBible.Tests.Web;

/// <summary>선물 2단계 데이터(ADR 0094, 선물-데이터-설계.md): 규칙 기본값 · 부서 격리 · 마이그레이션 보안 조건 · 운영 점검 마이그레이션 적용.</summary>
public class GiftDataTests
{
    [Fact]
    public void 선물_규칙_기본값은_정한_값이고_예전_규칙_JSON_에_없어도_기본값()
    {
        var g = RuleSet.Default.Gifts;
        Assert.Equal((5000, 100, 1000, 30000), (g.FirstCredits, g.StepPoints, g.StepCredits, g.PersonalCap));      // R-280
        Assert.Equal(new AmountRange(1000, 15000), g.Newcomer);                                                    // R-286
        Assert.Equal(new AmountRange(1000, 15000), g.Invite);
        Assert.Equal(new AmountRange(1000, 5000), g.Birthday);                                                     // R-288
        Assert.Equal((30, 7, 30, 7), (g.BirthdayOpenDays, g.BirthdayCloseDays, g.PickDays, g.ReopenDays));
        Assert.Equal([10000, 15000, 20000], g.ReadingAmounts);                                                    // R-289
        Assert.Equal(20000, GiftRules.Pick(g.ReadingAmounts, 5));                                                  // 표에 없는 회차는 마지막 값
        Assert.Equal(30000, GiftRules.Pick(g.FamilyAmounts, 7));                                                   // R-290: 3,000점부터 3만
        Assert.Equal(2, g.FriendShare);

        // 선물 묶음이 없던 예전 규칙 JSON 도 읽힌다
        var old = RuleService.ToJson(RuleSet.Default).Replace("\"Gifts\"", "\"OldGifts\"");
        Assert.Equal(GiftRules.Default, RuleService.FromJson(old).Gifts);
    }

    [Fact]
    public void 선물_표는_부서마다_보이고_공통_상품은_모든_부서가_본다()
    {
        var name = InMemoryDb.NewName();
        var a = InMemoryDb.SeedTenant(name, "aa", "AA0001");
        var b = InMemoryDb.SeedTenant(name, "bb", "BB0001");
        using (var raw = InMemoryDb.Raw(name))
        {
            raw.GiftItems.AddRange(
                new GiftItem { Id = 1000, Name = "공통 연필", Price = 1000 },
                new GiftItem { Id = 1001, TenantId = a.Id, Name = "A 부서 공책", Price = 2000 },
                new GiftItem { Id = 1002, TenantId = b.Id, Name = "B 부서 지우개", Price = 500 });
            raw.Gifts.AddRange(
                new Gift { TenantId = a.Id, Kind = GiftKinds.Birthday, Year = 2026, UserName = "a1", SourceKey = "B2026", MinAmount = 1000, MaxAmount = 5000 },
                new Gift { TenantId = b.Id, Kind = GiftKinds.Birthday, Year = 2026, UserName = "b1", SourceKey = "B2026", MinAmount = 1000, MaxAmount = 5000 });
            raw.SaveChanges();
        }
        var scope = new RequestScope();
        scope.SetTenant(a.Id);
        using var ctx = new InMemoryDb(name, scope).Create();
        Assert.Equal(["A 부서 공책", "공통 연필"], ctx.GiftItems.Select(i => i.Name).OrderBy(n => n).ToList());
        Assert.Equal(["a1"], ctx.Gifts.Select(x => x.UserName!).ToList());
    }

    [Fact]
    public void Gift_마이그레이션은_선물_표_9개에_부서_격리_조건을_건다()
    {
        var m = new Gift_ForTest();
        var sql = string.Join("\n", m.Up().OfType<SqlOperation>().Select(o => o.Sql));
        Assert.Contains("sec.fn_SharedRead(TenantId) ON app.GiftItem", sql);
        Assert.Contains("sec.fn_SharedWrite(TenantId) ON app.GiftItem AFTER INSERT", sql);
        foreach (var t in new[] { "GiftItemHidden", "GiftSetting", "GiftCreditRate", "GiftReadingAmount", "Gift", "GiftLog", "GiftPointUse", "TeamAwardArchive" })
        {
            Assert.Contains($"ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.{t},", sql);
            Assert.Contains($"ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.{t} AFTER INSERT", sql);
            Assert.Contains($"ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.{t} AFTER UPDATE", sql);
        }
        Assert.DoesNotContain(m.Up().OfType<CreateTableOperation>(), o => o.Name == "GiftYearClose" && o.Columns.Any(c => c.Name == "TenantId"));   // 플랫폼 표
    }

    /// <summary>마이그레이션의 Up 동작을 꺼내 보는 대역.</summary>
    private sealed class Gift_ForTest : ChallengeBible.Infrastructure.Migrations.Gift
    {
        public IReadOnlyList<MigrationOperation> Up()
        {
            var b = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
            base.Up(b);
            return b.Operations;
        }
    }

    [Fact]
    public async Task 마이그레이션_적용은_플랫폼_관리자만이고_메모리_DB_에서는_하지_않는다()
    {
        var name = InMemoryDb.NewName();
        var boss = InMemoryDb.SeedAccount(name, "boss@example.com", "관리자");
        InMemoryDb.MakePlatformAdmin(name, boss);
        InMemoryDb.SeedAccount(name, "kim@example.com", "김");
        var ops = new OpsService(new InMemorySystemDb(name), new AppInfo(TimeProvider.System), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ops.MigrateAsync("kim", ["x"]));
        var (ok, msg) = await ops.MigrateAsync("boss", ["x"]);
        Assert.False(ok);
        Assert.Contains("메모리 DB", msg);
        Assert.Empty(await ops.PendingAsync("boss"));
        using var raw = InMemoryDb.Raw(name);
        Assert.Empty(raw.AuditLogs.Where(l => l.Action == OpsService.MigrateAction));
    }
}
