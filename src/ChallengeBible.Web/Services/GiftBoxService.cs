using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>선물함 한 줄. Points 는 내 개인 선물만(가격 · 금액은 아이에게 보이지 않는다, R-285 · R-291). Status = 신청 완료 · 준비 중 · 받음.</summary>
public sealed record GiftBoxRow(long Id, string Kind, string Name, int? ItemId, bool HasImage, int? Points, DateTime? WhenUtc, string Status, bool Received);

/// <summary>선물함(2026-11-27 민트 시안 「선물함 상세」): 신청 개수 · 받은 개수 · 사용한 점수와 [신청한 선물 / 내가 받은 선물] 목록.</summary>
public sealed record GiftBox(int Requested, int Received, int PointsUsed, IReadOnlyList<GiftBoxRow> Requests, IReadOnlyList<GiftBoxRow> Gots);

/// <summary>
/// 내가 고른 선물의 모음(올해): 개인 · 생일 · 새친구 · 전도 · 완독 · 완필은 내 것, 가족 선물은 내 팀 것, 친구팀 선물은 내 몫.
/// 상태: 골랐어요(Picked) = 신청 완료 · 선생님이 샀어요(Purchased) = 준비 중 · 받았어요(Delivered) = 받음. 취소 · 건너뜀 · 기한 지남은 보이지 않는다.
/// </summary>
public sealed class GiftBoxService(IAppDb db, RequestScope scope, TenantContext tenant, TenantClock clock)
{
    public async Task<GiftBox?> BoxAsync(CancellationToken ct = default)
    {
        if (!tenant.Has(Roles.Participant) || scope.UserName is not { } user) return null;
        await using var ctx = db.Create();
        var year = (short)clock.Today(tenant.Current.TimeZoneId).Year;
        var team = await (from m in ctx.FamilyMembers join f in ctx.FamilyTeams on m.FamilyTeamId equals f.Id
                          where m.UserName == user && m.IsActive && f.IsActive select new { f.Id, f.TeamType }).FirstOrDefaultAsync(ct);
        int? teamId = team?.Id;
        var states = new[] { GiftStates.Picked, GiftStates.Purchased, GiftStates.Delivered };
        var list = await ctx.Gifts.AsNoTracking()
            .Where(g => g.Year == year && states.Contains(g.State) && g.ItemName != null
                && ((g.Kind != GiftKinds.Family && g.UserName == user) || (g.Kind == GiftKinds.Family && teamId != null && g.FamilyTeamId == teamId)))
            .OrderByDescending(g => g.PickedUtc ?? g.CreatedUtc).ToListAsync(ct);
        GiftBoxRow Row(Gift g) => new(g.Id, g.Kind, g.ItemName!, g.GiftItemId, g.ItemHasImage == true,
            g.Kind == GiftKinds.Personal ? (g.PointsUsed ?? 0) + (g.PointsBurned ?? 0) : null,
            g.State == GiftStates.Delivered ? g.DeliveredUtc ?? g.PickedUtc : g.PickedUtc ?? g.CreatedUtc,
            g.State switch { GiftStates.Delivered => "받음", GiftStates.Purchased => "준비 중", _ => "신청 완료" }, g.State == GiftStates.Delivered);
        var rows = list.Select(Row).ToList();
        var requests = rows.Where(r => !r.Received).ToList();
        var gots = rows.Where(r => r.Received).ToList();
        var used = list.Where(g => g.Kind == GiftKinds.Personal).Sum(g => (g.PointsUsed ?? 0) + (g.PointsBurned ?? 0));
        return new GiftBox(requests.Count, gots.Count, used, requests, gots);
    }
}
