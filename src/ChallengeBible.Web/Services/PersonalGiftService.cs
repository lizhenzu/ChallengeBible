using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>아이에게 보이는 선물 하나: 필요한 점수만(가격 · 크레딧은 숨김, R-285).</summary>
public sealed record PickItem(int Id, string Name, string? Description, bool HasImage, DateTime UpdatedUtc, int Need, int Credits, int Price);

/// <summary>지금 고른(준비 중) 선물.</summary>
public sealed record PickedGift(long Id, string State, int? ItemId, string Name, bool HasImage, int PointsUsed, int PointsBurned,
    DateTime PickedUtc, DateTime? PurchasedUtc, DateTime? DeliveredUtc);

/// <summary>
/// 개인 선물 지갑(R-280 ~ R-285). 학생만 고른다. YearPoints = 그해 1월 1일부터 모은 점수, Available = 아직 안 쓴 점수,
/// Usable = 상한에서 자른 점수(아이에게 보이는 "쓸 수 있는 점수"). Active = 받기 전인 선물(있으면 다음 선물을 못 고름, 정한 것 8).
/// </summary>
public sealed record GiftWallet(bool Student, int Year, DateOnly Today, int YearPoints, int Available, int Usable, int CapPoints,
    bool FirstBonus, PickedGift? Active, GiftRules Rules)
{
    public int Step => Rules.StepPoints;
    public bool CanPick => Student && Active is null && Usable >= Step;
    public bool AtCap => Student && Available >= CapPoints && CapPoints > 0;
    public bool December => Today.Month == 12;
    /// <summary>처음 단계까지 남은 점수(가. 100점이 안 됐을 때).</summary>
    public int ToFirst => Math.Max(0, Step - Available);
}

public enum PickResult { Ok, NotStudent, NotFound, NotEnough, AlreadyPicked, Busy }

/// <summary>
/// 도전 성경말씀 개인 선물 고르기(ADR 0094 B, 정한 것 1 · 2 · 8, R-280 ~ R-285).
/// 쓴 점수는 GiftPointUse 에만 적고 미션 기록 · 순위 · 가족팀 합계는 그대로(R-281). 한 번에 하나만(받은 뒤 다음).
/// </summary>
public sealed class PersonalGiftService(IAppDb db, RequestScope scope, TenantContext tenant, TenantClock clock, RuleService rules, TimeProvider time)
{
    private int T => tenant.Current.Id;
    public DateOnly Today => clock.Today(tenant.Current.TimeZoneId);
    /// <summary>미션 참여자이면서 학생(T1001)만 선물을 고른다(B-8).</summary>
    public bool IsStudent => tenant.Has(Roles.Participant) && UserRole.KindOf(tenant.RoleCode) == UserRoleKind.Student;
    public string? UserName => tenant.Has(Roles.Participant) ? scope.UserName : null;

    public async Task<GiftWallet?> WalletAsync(CancellationToken ct = default)
    {
        if (UserName is not { } user) return null;
        await using var ctx = db.Create();
        return await WalletAsync(ctx, user, ct);
    }

    private async Task<GiftWallet> WalletAsync(AppDbContext ctx, string user, CancellationToken ct)
    {
        var today = Today;
        var r = (await rules.ForDateAsync(today, ct)).Gifts;
        var year = (short)today.Year;
        var from = new DateOnly(today.Year, 1, 1);   // 정한 것 1: 시즌 시작일과 상관없이 1월 1일부터
        var points = await ctx.MissionActivities.Where(a => a.UserName == user && a.MissionDate >= from && a.MissionDate <= today)
            .SumAsync(a => (int?)a.Points, ct) ?? 0;
        var uses = await ctx.GiftPointUses.AsNoTracking().Where(u => u.UserName == user && u.Year == year).ToListAsync(ct);
        var spent = uses.Sum(u => u.PointsUsed + u.PointsBurned);
        var bonus = !uses.Any(u => u.FirstBonus);
        var cap = await ctx.GiftSettings.Select(s => s.PersonalCap).SingleOrDefaultAsync(ct) ?? r.PersonalCap;
        var capPoints = GiftMath.CapPoints(cap, r, bonus);
        var available = Math.Max(0, points - spent);
        var active = await ctx.Gifts.AsNoTracking()
            .Where(g => g.Kind == GiftKinds.Personal && g.UserName == user && g.Year == year && (g.State == GiftStates.Picked || g.State == GiftStates.Purchased))
            .OrderByDescending(g => g.Id).FirstOrDefaultAsync(ct);
        PickedGift? picked = null;
        if (active is not null)
        {
            var use = uses.FirstOrDefault(u => u.GiftId == active.Id);
            picked = new PickedGift(active.Id, active.State, active.GiftItemId, active.ItemName ?? "", active.ItemHasImage == true,
                use?.PointsUsed ?? active.PointsUsed ?? 0, use?.PointsBurned ?? active.PointsBurned ?? 0, active.PickedUtc ?? active.CreatedUtc, active.PurchasedUtc, active.DeliveredUtc);
        }
        return new GiftWallet(IsStudent, today.Year, today, points, available, Math.Min(available, capPoints), capPoints, bonus, picked, r);
    }

    /// <summary>이 부서 아이에게 보이는 선물(R-276): 공통 − 숨김 + 부서 상품, 판매 중만. 필요한 점수 낮은 순. 상한보다 비싼 것은 뺀다.</summary>
    public async Task<IReadOnlyList<PickItem>> ItemsAsync(GiftWallet w, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ItemsAsync(ctx, w, ct);
    }

    private async Task<IReadOnlyList<PickItem>> ItemsAsync(AppDbContext ctx, GiftWallet w, CancellationToken ct)
    {
        var hidden = await ctx.GiftItemsHidden.Select(h => h.GiftItemId).ToListAsync(ct);
        var items = await ctx.GiftItems.AsNoTracking()
            .Where(i => i.Status == GiftItemStatus.Active && (i.TenantId == T || (i.TenantId == null && !hidden.Contains(i.Id))))
            .Select(i => new { i.Id, i.Name, i.Description, i.HasImage, i.UpdatedUtc, i.Price }).ToListAsync(ct);
        var rates = await ctx.GiftCreditRates.AsNoTracking().ToDictionaryAsync(x => x.Credits, x => x.Amount, ct);
        return items.Select(i =>
            {
                var credits = GiftMath.CreditsForPrice(i.Price, rates);
                return new PickItem(i.Id, i.Name, i.Description, i.HasImage, i.UpdatedUtc, GiftMath.NeedPoints(credits, w.Rules, w.FirstBonus), credits, i.Price);
            })
            .Where(i => i.Need > 0 && i.Need <= w.CapPoints)
            .OrderBy(i => i.Need).ThenBy(i => i.Name).ToList();
    }

    /// <summary>선물 고르기(R-281): 필요한 점수만 쓰고 남은 점수는 모두 남긴다(정한 것 9). 받기 전 선물이 있으면 못 고른다(정한 것 8).</summary>
    public async Task<(PickResult Result, long? GiftId)> PickAsync(int itemId, CancellationToken ct = default)
    {
        if (!IsStudent || UserName is not { } user) return (PickResult.NotStudent, null);
        await using var ctx = db.Create();
        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var w = await WalletAsync(ctx, user, ct);
        if (w.Active is not null) return (PickResult.AlreadyPicked, w.Active.Id);
        var item = (await ItemsAsync(ctx, w, ct)).FirstOrDefault(i => i.Id == itemId);
        if (item is null) return (PickResult.NotFound, null);
        if (item.Need > w.Usable) return (PickResult.NotEnough, null);

        var now = time.GetUtcNow().UtcDateTime;
        var year = (short)w.Year;
        var n = await ctx.Gifts.CountAsync(g => g.Kind == GiftKinds.Personal && g.UserName == user && g.Year == year, ct) + 1;
        const int burned = 0;   // 남은 점수는 모두 남긴다(사용자 2026-11-14: 1,111점에서 1,000점 선물 → 111점 그대로, ADR 0094 정한 것 9)
        var gift = new Gift
        {
            TenantId = T, Kind = GiftKinds.Personal, Year = year, UserName = user, SourceKey = $"P{year}-{n}", State = GiftStates.Picked,
            MinAmount = 0, MaxAmount = item.Price, GiftItemId = item.Id, ItemName = item.Name, ItemPrice = item.Price, ItemHasImage = item.HasImage,
            CreditsUsed = item.Credits, PointsUsed = item.Need, PointsBurned = burned, PickedUtc = now, CreatedUtc = now,
        };
        ctx.Gifts.Add(gift);
        try
        {
            await ctx.SaveChangesAsync(ct);
            ctx.GiftPointUses.Add(new GiftPointUse { TenantId = T, UserName = user, Year = year, GiftId = gift.Id, PointsUsed = item.Need, PointsBurned = burned, FirstBonus = w.FirstBonus, CreatedUtc = now });
            ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = gift.Id, Action = GiftActions.Pick, ActorUserName = user, AtUtc = now, ItemName = item.Name, ItemPrice = item.Price,
                Detail = $"points {item.Need}, credits {item.Credits}" });
            await ctx.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            return (PickResult.Busy, null);   // 두 번 누름 · 동시에 고름: 같은 SourceKey 는 하나만(UX_Gift_Source)
        }
        return (PickResult.Ok, gift.Id);
    }

    /// <summary>본인 취소(R-284): 선생님이 준비(구매 완료)하기 전, 그해 선물만. 쓴 점수가 모두 돌아온다.</summary>
    public async Task<bool> CancelAsync(long giftId, CancellationToken ct = default)
    {
        if (UserName is not { } user) return false;
        await using var ctx = db.Create();
        var year = (short)Today.Year;
        var gift = await ctx.Gifts.SingleOrDefaultAsync(g => g.Id == giftId && g.Kind == GiftKinds.Personal && g.UserName == user, ct);
        if (gift is null || gift.State != GiftStates.Picked || gift.Year != year) return false;   // R-282: 해가 바뀐 뒤에는 되살리지 않음
        var now = time.GetUtcNow().UtcDateTime;
        gift.State = GiftStates.Cancelled;
        ctx.GiftPointUses.RemoveRange(await ctx.GiftPointUses.Where(u => u.GiftId == gift.Id).ToListAsync(ct));
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = gift.Id, Action = GiftActions.Cancel, ActorUserName = user, AtUtc = now, ItemName = gift.ItemName, ItemPrice = gift.ItemPrice });
        await ctx.SaveChangesAsync(ct);
        return true;
    }
}
