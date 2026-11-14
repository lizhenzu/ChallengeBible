using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>내 팀 선물 한 건(미션 홈 카드 · 고르기 화면). Stage = 넘은 단계 점수, PickedBy = 마지막으로 고른 사람 이름.</summary>
public sealed record TeamGiftView(long Id, bool Friend, int Stage, string State, DateOnly? OpenUntil, int? ItemId, string? ItemName, bool HasImage,
    string? PickedBy, DateTime? PickedUtc, DateTime? PurchasedUtc, int MaxAmount, int? TeamId)
{
    public bool CanPick => State is GiftStates.Open or GiftStates.Picked;
}

/// <summary>고르기 화면의 상품(가격은 아이 · 부모에게 보이지 않는다 — 사용자 2026-11-14 질문 1 → ③).</summary>
public sealed record TeamPickItem(int Id, string Name, string? Description, bool HasImage, DateTime UpdatedUtc, int Price);

public enum TeamPickError { None, NotFound, NotMember, Closed, TooExpensive }

/// <summary>
/// 가족팀 · 친구팀 선물(확정 시안 가족팀-선물고르기, ADR 0094 F · 정한 것 11, R-290 · R-291 · R-292).
/// 팀 점수가 켜진 단계를 넘고 시상 기록이 없으면 선물이 생긴다(가족 = 팀에 하나 · 단계 금액, 친구 = 구성원마다 · 금액 / FriendShare).
/// 기한 = 생긴 날부터 PickDays 일, 지나면 Expired → 관리자 [다시 열기](ReopenDays 일). 구매 전까지 구성원 누구나 바꾼다.
/// </summary>
public sealed class TeamGiftService(IAppDb db, RequestScope scope, TenantContext tenant, TenantClock clock, RuleService rules, FamilyAwardService awards, TimeProvider time)
{
    private int T => tenant.Current.Id;
    private DateOnly Today => clock.Today(tenant.Current.TimeZoneId);
    public static string SourceKey(int year, int stage) => $"T{year}-{stage}";
    public static int StageOf(string sourceKey) => int.TryParse(sourceKey[(sourceKey.IndexOf('-') + 1)..], out var n) ? n : 0;

    /// <summary>선물 만들기 + 기한 지난 것 닫기. 미션 홈 · 가족팀 · 선물 처리 화면을 열 때 부른다(같은 열쇠는 한 번만, UX_Gift_Source).</summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby) return;
        await using var ctx = db.Create();
        var today = Today;
        var r = (await rules.ForDateAsync(today, ct)).Gifts;
        var now = time.GetUtcNow().UtcDateTime;
        var year = (short)today.Year;

        // 기한 지남(R-291)
        foreach (var g in await ctx.Gifts.Where(g => (g.Kind == GiftKinds.Family || g.Kind == GiftKinds.Friend) && g.State == GiftStates.Open && g.OpenUntil < today).ToListAsync(ct))
        {
            g.State = GiftStates.Expired;
            ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Expire, AtUtc = now });
        }
        await ctx.SaveChangesAsync(ct);

        var due = await awards.DueForGiftsAsync(ctx, ct);
        if (due.Count == 0) return;
        var have = (await ctx.Gifts.AsNoTracking().Where(g => (g.Kind == GiftKinds.Family || g.Kind == GiftKinds.Friend) && g.Year == year)
            .Select(g => new { g.FamilyTeamId, g.UserName, g.SourceKey }).ToListAsync(ct))
            .Select(g => (g.FamilyTeamId, g.UserName ?? "", g.SourceKey)).ToHashSet();
        var added = new List<Gift>();
        foreach (var d in due.Where(d => d.Amount > 0))
        {
            var key = SourceKey(today.Year, d.Stage);
            var owners = d.Friend ? (d.Pids ?? []).Select(p => (string?)p).ToList() : [null];
            var amount = d.Friend ? d.Amount / Math.Max(1, r.FriendShare) : d.Amount;
            foreach (var who in owners)
            {
                if (have.Contains((d.TeamId, who ?? "", key))) continue;
                var g = new Gift
                {
                    TenantId = T, Kind = d.Friend ? GiftKinds.Friend : GiftKinds.Family, Year = year, UserName = who, FamilyTeamId = d.TeamId, SourceKey = key,
                    State = GiftStates.Open, OpenFrom = today, OpenUntil = today.AddDays(r.PickDays), MinAmount = 0, MaxAmount = amount, CreatedUtc = now,
                };
                ctx.Gifts.Add(g);
                added.Add(g);
            }
        }
        if (added.Count == 0) return;
        try
        {
            await ctx.SaveChangesAsync(ct);
            foreach (var g in added) ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Open, AtUtc = now, Detail = $"{g.SourceKey} max {g.MaxAmount}" });
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { }   // 다른 요청이 같은 선물을 먼저 만들었다
    }

    /// <summary>내 활성 팀(가족 · 친구). 없으면 null.</summary>
    private async Task<(int TeamId, bool Friend)?> MyTeamAsync(AppDbContext ctx, string user, CancellationToken ct)
    {
        var t = await (from m in ctx.FamilyMembers join f in ctx.FamilyTeams on m.FamilyTeamId equals f.Id
                       where m.UserName == user && m.IsActive && f.IsActive select new { f.Id, f.TeamType }).FirstOrDefaultAsync(ct);
        return t is null ? null : (t.Id, t.TeamType == TeamTypes.Friend);
    }

    /// <summary>내가 볼 팀 선물(올해, 받기 전 + 기한 지남): 가족 = 내 팀의 선물, 친구 = 내 몫.</summary>
    public async Task<IReadOnlyList<TeamGiftView>> MineAsync(CancellationToken ct = default)
    {
        if (!tenant.Has(Roles.Participant) || scope.UserName is not { } user) return [];
        await using var ctx = db.Create();
        var team = await MyTeamAsync(ctx, user, ct);
        if (team is null) return [];
        var year = (short)Today.Year;
        var (teamId, friend) = team.Value;
        var states = new[] { GiftStates.Open, GiftStates.Picked, GiftStates.Purchased, GiftStates.Expired };
        var list = await ctx.Gifts.AsNoTracking().Where(g => g.Year == year && states.Contains(g.State) && g.FamilyTeamId == teamId
                && (friend ? g.Kind == GiftKinds.Friend && g.UserName == user : g.Kind == GiftKinds.Family))
            .OrderBy(g => g.Id).ToListAsync(ct);
        return await ViewsAsync(ctx, list, ct);
    }

    private static async Task<List<TeamGiftView>> ViewsAsync(AppDbContext ctx, List<Gift> list, CancellationToken ct)
    {
        var ids = list.Select(g => g.Id).ToList();
        var picks = await ctx.GiftLogs.AsNoTracking().Where(l => ids.Contains(l.GiftId) && (l.Action == GiftActions.Pick || l.Action == GiftActions.Change))
            .Select(l => new { l.GiftId, l.Id, l.ActorUserName }).ToListAsync(ct);
        var last = picks.GroupBy(p => p.GiftId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First().ActorUserName);
        var users = last.Values.OfType<string>().Distinct().ToList();
        var names = users.Count == 0 ? [] : (await ctx.Users.AsNoTracking().Where(u => users.Contains(u.UserName!)).Select(u => new { u.UserName, u.LastName, u.FirstName }).ToListAsync(ct))
            .ToDictionary(u => u.UserName!, u => AppUser.FullName(u.LastName, u.FirstName), StringComparer.OrdinalIgnoreCase);
        return list.Select(g => new TeamGiftView(g.Id, g.Kind == GiftKinds.Friend, StageOf(g.SourceKey), g.State, g.OpenUntil, g.GiftItemId, g.ItemName, g.ItemHasImage == true,
            last.TryGetValue(g.Id, out var by) && by is not null ? names.GetValueOrDefault(by) ?? by : null, g.PickedUtc, g.PurchasedUtc, g.MaxAmount, g.FamilyTeamId)).ToList();
    }

    /// <summary>한 건(내 것일 때만).</summary>
    public async Task<TeamGiftView?> GetAsync(long id, CancellationToken ct = default) => (await MineAsync(ct)).FirstOrDefault(g => g.Id == id);

    /// <summary>그 선물의 금액 안 상품(공통 − 숨김 + 부서 상품, 판매 중), 싼 순.</summary>
    public async Task<IReadOnlyList<TeamPickItem>> ItemsAsync(int maxAmount, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var hidden = await ctx.GiftItemsHidden.Select(h => h.GiftItemId).ToListAsync(ct);
        return await ctx.GiftItems.AsNoTracking()
            .Where(i => i.Status == GiftItemStatus.Active && i.Price <= maxAmount && (i.TenantId == T || (i.TenantId == null && !hidden.Contains(i.Id))))
            .OrderBy(i => i.Price).ThenBy(i => i.Name)
            .Select(i => new TeamPickItem(i.Id, i.Name, i.Description, i.HasImage, i.UpdatedUtc, i.Price)).ToListAsync(ct);
    }

    /// <summary>고르기 · 바꾸기(R-291): 내 팀 선물이고, 고를 수 있음 · 고름 상태이고, 기한 안이고, 금액 안일 때.</summary>
    public async Task<TeamPickError> PickAsync(long giftId, int itemId, CancellationToken ct = default)
    {
        var mine = await GetAsync(giftId, ct);
        if (mine is null || scope.UserName is not { } user) return TeamPickError.NotMember;
        await using var ctx = db.Create();
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.Id == giftId, ct);
        if (g is null) return TeamPickError.NotFound;
        if (g.State is not (GiftStates.Open or GiftStates.Picked) || (g.OpenUntil is { } until && until < Today && g.State == GiftStates.Open)) return TeamPickError.Closed;
        var item = (await ItemsAsync(g.MaxAmount, ct)).FirstOrDefault(i => i.Id == itemId);
        if (item is null) return TeamPickError.TooExpensive;
        var now = time.GetUtcNow().UtcDateTime;
        var change = g.State == GiftStates.Picked;
        g.State = GiftStates.Picked;
        g.GiftItemId = item.Id; g.ItemName = item.Name; g.ItemPrice = item.Price; g.ItemHasImage = item.HasImage; g.PickedUtc = now;
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = change ? GiftActions.Change : GiftActions.Pick, ActorUserName = user, AtUtc = now, ItemName = item.Name, ItemPrice = item.Price });
        await ctx.SaveChangesAsync(ct);
        return TeamPickError.None;
    }

    /// <summary>관리자 [다시 열기](R-291): 기한 지난 선물을 오늘부터 ReopenDays 일 더 고를 수 있게.</summary>
    public async Task<bool> ReopenAsync(string actor, long giftId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.Id == giftId, ct);
        if (g is null || g.State != GiftStates.Expired) return false;
        var r = (await rules.ForDateAsync(Today, ct)).Gifts;
        g.State = GiftStates.Open;
        g.OpenUntil = Today.AddDays(r.ReopenDays);
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Reopen, ActorUserName = actor, AtUtc = time.GetUtcNow().UtcDateTime, Detail = $"until {g.OpenUntil:yyyy-MM-dd}" });
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// 전달 완료 때 시상 기록 + 팀 잠금(R-292, R-225): 그 팀 · 단계 기록이 없으면 남긴다. 되돌리기(받음 → 줄 것)는 같은 단계의 다른 받은 선물이 없을 때 기록을 지운다.
    /// GiftOrderService 가 같은 연결 안에서 부른다.
    /// </summary>
    internal static async Task AwardAsync(AppDbContext ctx, Gift g, bool give, string actor, DateTime now, CancellationToken ct)
    {
        if (g.Kind is not (GiftKinds.Family or GiftKinds.Friend) || g.FamilyTeamId is not int teamId) return;
        var team = await ctx.FamilyTeams.SingleOrDefaultAsync(t => t.Id == teamId, ct);
        if (team is null) return;
        var kind = g.Kind == GiftKinds.Friend ? TeamTypes.Friend : TeamTypes.Family;
        var key = FamilyAwardService.KeyOf(team);
        if (string.IsNullOrWhiteSpace(key)) return;
        var stage = StageOf(g.SourceKey);
        var row = await ctx.TeamAwards.FirstOrDefaultAsync(a => a.Kind == kind && a.AwardKey == key && a.Threshold == stage, ct);
        if (give && row is null)
        {
            ctx.TeamAwards.Add(new TeamAward { TenantId = g.TenantId, Kind = kind, AwardKey = key, Threshold = stage, AwardedAt = now });
            ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = FamilyAwardService.GiveAction, TenantId = g.TenantId, Detail = $"{key} · {stage}점 · gift {g.Id}" });
        }
        else if (!give && row is not null && !await ctx.Gifts.AnyAsync(x => x.Id != g.Id && x.FamilyTeamId == teamId && x.SourceKey == g.SourceKey && x.State == GiftStates.Delivered, ct))
        {
            ctx.TeamAwards.Remove(row);
            ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = FamilyAwardService.UndoAction, TenantId = g.TenantId, Detail = $"{key} · {stage}점 · gift {g.Id}" });
        }
    }
}
