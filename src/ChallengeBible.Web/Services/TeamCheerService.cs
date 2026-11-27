using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public enum CheerResult { Ok, NoTeam, NotMember, AlreadyDone, AlreadySent, Failed }

/// <summary>
/// 응원하기(2026-11-27 사용자 · 민트 시안 TeamB): 우리 팀에서 오늘 아직 미션을 안 한 팀원에게 쪽지 한 통을 보낸다. 같은 사람에게는 하루(부서 날짜)에 한 번만.
/// 보내는 사람과 받는 사람은 같은 팀의 활성 구성원이어야 하고, 나에게는 보내지 않는다. 쪽지는 보통 1:1 쪽지(답장 가능)로 간다.
/// </summary>
public sealed class TeamCheerService(FamilyTeamService teams, MessageService messages, ISystemDb systemDb, RequestScope scope, TenantContext tenant, TenantClock clock)
{
    public const string Body = "오늘도 말씀 미션을 함께해요! 응원해요.";

    /// <summary>오늘 내가 응원을 보낸 팀원(계정 이름).</summary>
    public async Task<HashSet<string>> SentTodayAsync(CancellationToken ct = default)
    {
        if (scope.UserName is not { } me) return [];
        await using var ctx = systemDb.Create(me);
        var since = DateTime.UtcNow.AddDays(-2);
        var rows = await ctx.Messages.AsNoTracking().Where(m => m.Kind == MessageKinds.Direct && m.SenderUserName == me && m.Body == Body && m.SentUtc >= since)
            .Select(m => new { m.RecipientUserName, m.SentUtc }).ToListAsync(ct);
        var today = clock.Today(tenant.Current.TimeZoneId);
        return rows.Where(m => m.RecipientUserName is not null
                && TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(m.SentUtc, DateTimeKind.Utc)), tenant.Current.TimeZoneId) == today)
            .Select(m => m.RecipientUserName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<CheerResult> SendAsync(string? target, CancellationToken ct = default)
    {
        if (scope.UserName is not { } me || string.IsNullOrWhiteSpace(target)) return CheerResult.NotMember;
        var board = await teams.BoardAsync(ct);
        if (board is null) return CheerResult.NoTeam;
        var row = board.Members.FirstOrDefault(m => string.Equals(m.UserName, target, StringComparison.OrdinalIgnoreCase));
        if (row is null || row.Me) return CheerResult.NotMember;
        if (row.TodayDone) return CheerResult.AlreadyDone;
        if ((await SentTodayAsync(ct)).Contains(row.UserName)) return CheerResult.AlreadySent;
        return await messages.SendAsync(me, row.UserName, Body, null, ct) == MsgError.None ? CheerResult.Ok : CheerResult.Failed;
    }
}
