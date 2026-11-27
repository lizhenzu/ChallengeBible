using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>타임라인 한 단계(확정 시안 할일타임라인): State = d(끝남) · now(지금 차례) · late(밀림) · todo(아직).</summary>
public sealed record TimelineStep(string Label, string State, DateTime? AtUtc = null, string? Who = null);

/// <summary>
/// 선물 한 건의 진행(2026-11-16 사용자 "타임라인 방식"): 선물 대상 → (링크 보냄) → 아이가 고름 → 구매 → 전달.
/// 단계마다 마지막 이력의 시각 · 사람. 링크 단계는 링크로 고르는 선물(계정 없는 아이)이거나 링크를 만든 적이 있을 때만.
/// </summary>
public static class GiftTimeline
{
    public static IReadOnlyList<TimelineStep> Build(string kind, string state, bool noAccount, DateTime createdUtc, DateTime? pickedUtc,
        IReadOnlyList<GiftLogRow> logs, DateTime nowUtc, int lateDays)
    {
        GiftLogRow? Last(params string[] actions) => logs.Where(l => actions.Contains(l.Action)).OrderByDescending(l => l.AtUtc).FirstOrDefault();
        static string? Name(GiftLogRow? l) => l is null ? null : l.ActorName ?? l.Actor;
        var steps = new List<TimelineStep>();
        var open = Last(GiftActions.Open);
        steps.Add(new("선물 대상", "d", open?.AtUtc ?? createdUtc, "자동"));

        var picked = state is GiftStates.Picked or GiftStates.Purchased or GiftStates.Delivered;
        var link = Last(GiftActions.Link);
        var linkStep = BirthdayGiftService.LinkKinds.Contains(kind) && (link is not null || (noAccount && !picked));
        if (linkStep)
            steps.Add(link is not null ? new("링크 보냄", "d", link.AtUtc, Name(link)) : new("링크 보낼 차례", state == GiftStates.Expired ? "late" : "now"));

        var pick = Last(GiftActions.Pick, GiftActions.Change);
        if (picked) steps.Add(new("아이가 고름", "d", pick?.AtUtc ?? pickedUtc, pick is null ? null : pick.Actor is null ? "부모님 링크" : Name(pick)));
        else if (state == GiftStates.Expired) steps.Add(new("고르는 기한이 지났어요", "late"));
        else steps.Add(new("아이가 고를 차례", linkStep && link is null ? "todo" : "now"));

        var buy = Last(GiftActions.Purchase);
        if (state is GiftStates.Purchased or GiftStates.Delivered) steps.Add(new("구매", "d", buy?.AtUtc, Name(buy)));
        else if (state == GiftStates.Picked)
        {
            var days = pickedUtc is { } p ? (int)(nowUtc - p).TotalDays : 0;
            steps.Add(days >= lateDays ? new($"구매 · {days}일째 아무도 안 삼", "late") : new("구매할 차례", "now"));
        }
        else steps.Add(new("구매", "todo"));

        var give = Last(GiftActions.Deliver);
        if (state == GiftStates.Delivered) steps.Add(new("전달", "d", give?.AtUtc, Name(give)));
        else steps.Add(state == GiftStates.Purchased ? new("전달할 차례", "now") : new("전달", "todo"));
        return steps;
    }

    /// <summary>여러 선물의 이력(사람 이름 붙임).</summary>
    public static async Task<Dictionary<long, List<GiftLogRow>>> LogsAsync(AppDbContext ctx, IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var logs = await ctx.GiftLogs.AsNoTracking().Where(l => ids.Contains(l.GiftId)).ToListAsync(ct);
        var users = logs.Select(l => l.ActorUserName).OfType<string>().Distinct().ToList();
        var names = users.Count == 0 ? [] : (await ctx.Users.AsNoTracking().Where(u => users.Contains(u.UserName!)).Select(u => new { u.UserName, u.LastName, u.FirstName }).ToListAsync(ct))
            .ToDictionary(u => u.UserName!, u => AppUser.FullName(u.LastName, u.FirstName) is { Length: > 0 } n ? n : u.UserName!, StringComparer.OrdinalIgnoreCase);
        return logs.GroupBy(l => l.GiftId).ToDictionary(g => g.Key, g => g.Select(l => new GiftLogRow(l.Action, l.ActorUserName,
            l.ActorUserName is { } a ? names.GetValueOrDefault(a) : null, l.AtUtc, l.ItemName, l.ItemPrice)).ToList());
    }
}
