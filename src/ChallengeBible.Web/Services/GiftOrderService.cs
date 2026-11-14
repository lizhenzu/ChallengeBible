using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>관리자 목록의 선물 한 건(가격 보임). Who = 받는 사람 이름(가족팀이면 팀 이름), ClassName = 반.</summary>
public sealed record GiftOrderRow(long Id, string Kind, string State, string Who, string? ClassName, int? ItemId, string ItemName, int Price, bool HasImage,
    int? PointsUsed, string? CoupangUrl, DateTime CreatedUtc, DateTime? PickedUtc, DateTime? PurchasedUtc, DateTime? DeliveredUtc, short Year,
    int Stage = 0, int MaxAmount = 0, DateOnly? OpenUntil = null)
{
    public bool Team => Kind is GiftKinds.Family or GiftKinds.Friend;
    /// <summary>종류 꼬리표: 팀 선물은 "가족팀 2,000점".</summary>
    public string KindText => Team ? $"{GiftOrderService.KindLabel(Kind)} {Stage:N0}점" : GiftOrderService.KindLabel(Kind);
}

public sealed record GiftLogRow(string Action, string? Actor, string? ActorName, DateTime AtUtc, string? ItemName, int? ItemPrice);

public sealed record GiftOrderCounts(int Buy, int Give, int Done, int Cancel, int Pick = 0);

/// <summary>부서 카드 "누가 선물 받나요"의 한 줄(가격 없음, R-297).</summary>
public sealed record GiftBoardRow(long Id, string Who, string Kind, string State, int? ItemId, string ItemName, bool HasImage, DateTime At);

public enum GiftOrderError { None, NotFound, BadState, LastYear }

/// <summary>
/// 선물 구매 · 전달 처리(확정 시안 관리-선물처리, ADR 0094 G-1 · G-3 · G-6 · H-1, R-284 · R-293 · R-295 · R-297 · R-298).
/// 부서 관리자만 [샀어요] · [줬어요] · [되돌리기] · 관리자 취소. 동작마다 GiftLog 에 남긴다.
/// </summary>
public sealed class GiftOrderService(IAppDb db, TenantContext tenant, TenantClock clock, TimeProvider time)
{
    public const string Pick = "pick", Buy = "buy", Give = "give", Done = "done", Cancel = "cancel";
    /// <summary>부서 카드에 받은 선물을 보이는 날 수(사용자 2026-11-14 "확정" — 시안 질문 ① 30일).</summary>
    public const int BoardDeliveredDays = 30;
    public const int BoardTake = 3;

    private int T => tenant.Current.Id;

    public static string KindLabel(string kind) => kind switch
    {
        GiftKinds.Personal => "개인", GiftKinds.Newcomer => "새친구", GiftKinds.Invite => "초대", GiftKinds.Birthday => "생일",
        GiftKinds.Reading => "완독", GiftKinds.Family => "가족팀", GiftKinds.Friend => "친구팀", _ => kind,
    };

    public static string StateOf(string tab) => tab switch { Give => GiftStates.Purchased, Done => GiftStates.Delivered, Cancel => GiftStates.Cancelled, _ => GiftStates.Picked };

    public async Task<GiftOrderCounts> CountsAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var g = await ctx.Gifts.GroupBy(x => x.State).Select(x => new { x.Key, N = x.Count() }).ToListAsync(ct);
        int N(string s) => g.FirstOrDefault(x => x.Key == s)?.N ?? 0;
        return new(N(GiftStates.Picked), N(GiftStates.Purchased), N(GiftStates.Delivered), N(GiftStates.Cancelled), N(GiftStates.Open) + N(GiftStates.Expired));
    }

    /// <summary>탭 목록: 살 것 · 줄 것은 오래된 순(먼저 처리), 받음 · 취소는 최근 순.</summary>
    public async Task<IReadOnlyList<GiftOrderRow>> ListAsync(string tab, int take = 200, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var state = StateOf(tab);
        var q = tab == Pick   // 고르는 중: 생겼는데 아직 안 고른 것 + 기한 지난 것(팀 선물, 다시 열기)
            ? ctx.Gifts.AsNoTracking().Where(g => g.State == GiftStates.Open || g.State == GiftStates.Expired)
            : ctx.Gifts.AsNoTracking().Where(g => g.State == state);
        q = tab == Pick ? q.OrderBy(g => g.State == GiftStates.Expired).ThenBy(g => g.OpenUntil).ThenBy(g => g.Id) : state switch
        {
            GiftStates.Picked => q.OrderBy(g => g.PickedUtc).ThenBy(g => g.Id),
            GiftStates.Purchased => q.OrderBy(g => g.PurchasedUtc).ThenBy(g => g.Id),
            GiftStates.Delivered => q.OrderByDescending(g => g.DeliveredUtc).ThenByDescending(g => g.Id),
            _ => q.OrderByDescending(g => g.Id),
        };
        return await RowsAsync(ctx, await q.Take(take).ToListAsync(ct), ct);
    }

    public async Task<(GiftOrderRow? Row, IReadOnlyList<GiftLogRow> Logs)> GetAsync(long id, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var g = await ctx.Gifts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return (null, []);
        var row = (await RowsAsync(ctx, [g], ct))[0];
        var logs = await ctx.GiftLogs.AsNoTracking().Where(l => l.GiftId == id).OrderByDescending(l => l.AtUtc).ThenByDescending(l => l.Id).ToListAsync(ct);
        var names = await NamesAsync(ctx, logs.Select(l => l.ActorUserName).OfType<string>().Distinct().ToList(), ct);
        return (row, logs.Select(l => new GiftLogRow(l.Action, l.ActorUserName, l.ActorUserName is { } a ? names.GetValueOrDefault(a) : null, l.AtUtc, l.ItemName, l.ItemPrice)).ToList());
    }

    private static async Task<Dictionary<string, string>> NamesAsync(AppDbContext ctx, List<string> users, CancellationToken ct) =>
        users.Count == 0 ? [] : (await ctx.Users.AsNoTracking().Where(u => users.Contains(u.UserName!)).Select(u => new { u.UserName, u.LastName, u.FirstName }).ToListAsync(ct))
            .ToDictionary(u => u.UserName!, u => AppUser.FullName(u.LastName, u.FirstName) is { Length: > 0 } n ? n : u.UserName!, StringComparer.OrdinalIgnoreCase);

    /// <summary>받는 사람 이름 · 반 · 쿠팡 링크를 붙인다. 개인 · 생일 등은 계정 이름(없으면 명부 이름), 가족팀은 팀 이름.</summary>
    private async Task<List<GiftOrderRow>> RowsAsync(AppDbContext ctx, List<Gift> gifts, CancellationToken ct)
    {
        var users = gifts.Select(g => g.UserName).OfType<string>().Distinct().ToList();
        var names = await NamesAsync(ctx, users, ct);
        var userIds = users.Count == 0 ? [] : await ctx.Users.AsNoTracking().Where(u => users.Contains(u.UserName!)).Select(u => new { u.UserName, u.Id }).ToListAsync(ct);
        var ids = userIds.Select(u => u.Id).ToList();
        var personIds = gifts.Select(g => g.PersonId).OfType<int>().Distinct().ToList();
        var people = await ctx.People.AsNoTracking().Where(p => (p.UserId != null && ids.Contains(p.UserId)) || personIds.Contains(p.Id)).Select(p => new { p.Id, p.UserId, p.Name }).ToListAsync(ct);
        var term = await ctx.ClassTerms.AsNoTracking().OrderByDescending(t => t.StartDate).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        var pids = people.Select(p => p.Id).ToList();
        var classes = term is null ? [] : await (from m in ctx.ClassMembers join c in ctx.Classes on m.ClassId equals c.Id
                                                 where m.TermId == term && pids.Contains(m.PersonId) select new { m.PersonId, c.Name }).ToListAsync(ct);
        var teams = gifts.Select(g => g.FamilyTeamId).OfType<int>().Distinct().ToList();
        var teamNames = teams.Count == 0 ? [] : await ctx.FamilyTeams.AsNoTracking().Where(t => teams.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var items = gifts.Select(g => g.GiftItemId).OfType<int>().Distinct().ToList();
        var urls = await ctx.GiftItems.AsNoTracking().Where(i => items.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.CoupangUrl, ct);

        return gifts.Select(g =>
        {
            var uid = userIds.FirstOrDefault(u => string.Equals(u.UserName, g.UserName, StringComparison.OrdinalIgnoreCase))?.Id;
            var person = g.PersonId is int pid ? people.FirstOrDefault(p => p.Id == pid) : uid is null ? null : people.FirstOrDefault(p => p.UserId == uid);
            var who = g.Kind == GiftKinds.Family && g.FamilyTeamId is int tid && teamNames.TryGetValue(tid, out var tn) ? (tn.EndsWith("가족") ? tn : tn + " 가족")
                : g.UserName is { } un && names.TryGetValue(un, out var nm) ? nm : person?.Name ?? g.UserName ?? "";
            var cls = person is null ? null : classes.FirstOrDefault(c => c.PersonId == person.Id)?.Name;
            return new GiftOrderRow(g.Id, g.Kind, g.State, who, cls, g.GiftItemId, g.ItemName ?? "", g.ItemPrice ?? 0, g.ItemHasImage == true, g.PointsUsed,
                g.GiftItemId is int iid ? urls.GetValueOrDefault(iid) : null, g.CreatedUtc, g.PickedUtc, g.PurchasedUtc, g.DeliveredUtc, g.Year,
                g.Kind is GiftKinds.Family or GiftKinds.Friend ? TeamGiftService.StageOf(g.SourceKey) : 0, g.MaxAmount, g.OpenUntil);
        }).ToList();
    }

    /// <summary>[샀어요] 고름 → 구매 완료, [줬어요] 구매 완료 → 전달 완료, [되돌리기] 한 단계 앞으로(R-293 · R-298).</summary>
    public async Task<GiftOrderError> MoveAsync(string actor, long id, string action, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return GiftOrderError.NotFound;
        var now = time.GetUtcNow().UtcDateTime;
        switch (action)
        {
            case GiftActions.Purchase when g.State == GiftStates.Picked:
                g.State = GiftStates.Purchased; g.PurchasedUtc = now; break;
            case GiftActions.Deliver when g.State == GiftStates.Purchased:
                g.State = GiftStates.Delivered; g.DeliveredUtc = now;
                await TeamGiftService.AwardAsync(ctx, g, give: true, actor, now, ct);   // 가족팀 · 친구팀: 시상 기록 + 팀 잠금(R-292)
                break;
            case GiftActions.Undo when g.State == GiftStates.Purchased:
                g.State = GiftStates.Picked; g.PurchasedUtc = null; break;
            case GiftActions.Undo when g.State == GiftStates.Delivered:
                g.State = GiftStates.Purchased; g.DeliveredUtc = null;
                await TeamGiftService.AwardAsync(ctx, g, give: false, actor, now, ct);
                break;
            default:
                return GiftOrderError.BadState;
        }
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = action, ActorUserName = actor, AtUtc = now, ItemName = g.ItemName, ItemPrice = g.ItemPrice });
        await ctx.SaveChangesAsync(ct);
        return GiftOrderError.None;
    }

    /// <summary>관리자 취소(R-284): 받기 전(고름 · 구매 완료)이면 취소하고 쓴 점수를 돌려준다. 해가 바뀐 선물은 되돌리지 않는다(R-282).</summary>
    public async Task<GiftOrderError> CancelAsync(string actor, long id, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return GiftOrderError.NotFound;
        if (g.State is not (GiftStates.Picked or GiftStates.Purchased)) return GiftOrderError.BadState;
        if (g.Year != clock.Today(tenant.Current.TimeZoneId).Year) return GiftOrderError.LastYear;
        var now = time.GetUtcNow().UtcDateTime;
        g.State = GiftStates.Cancelled;
        ctx.GiftPointUses.RemoveRange(await ctx.GiftPointUses.Where(u => u.GiftId == g.Id).ToListAsync(ct));
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Cancel, ActorUserName = actor, AtUtc = now, ItemName = g.ItemName, ItemPrice = g.ItemPrice, Detail = "admin" });
        await ctx.SaveChangesAsync(ct);
        return GiftOrderError.None;
    }

    /// <summary>
    /// 부서 카드 "누가 선물 받나요"(R-297): 골랐어요 · 준비했어요 + 받은 지 30일까지, 최근 순. take 명과 전체 수.
    /// 생일 선물은 생일 주에만 — 생일 선물을 만들 때(5단계-③) 거른다.
    /// </summary>
    public async Task<(IReadOnlyList<GiftBoardRow> Rows, int Total)> BoardAsync(int take, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var since = time.GetUtcNow().UtcDateTime.AddDays(-BoardDeliveredDays);
        var q = ctx.Gifts.AsNoTracking().Where(g => g.State == GiftStates.Picked || g.State == GiftStates.Purchased || (g.State == GiftStates.Delivered && g.DeliveredUtc >= since));
        var total = await q.CountAsync(ct);
        var list = await q.OrderByDescending(g => g.DeliveredUtc ?? g.PurchasedUtc ?? g.PickedUtc ?? g.CreatedUtc).ThenByDescending(g => g.Id).Take(take).ToListAsync(ct);
        var rows = await RowsAsync(ctx, list, ct);
        return (rows.Select(r => new GiftBoardRow(r.Id, r.Who, r.Kind, r.State, r.ItemId, r.ItemName, r.HasImage, r.DeliveredUtc ?? r.PurchasedUtc ?? r.PickedUtc ?? r.CreatedUtc)).ToList(), total);
    }
}
