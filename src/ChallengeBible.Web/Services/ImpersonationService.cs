using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public enum ImpersonateError { None, NotAdmin, NotFound, PlatformAdmin, Suspended, Self }

/// <summary>
/// 플랫폼 관리자 가장(ADR 0067). 시작 · 복귀 때 플랫폼 관리자인지 DB 로 다시 확인하고 감사 로그를 남긴다.
/// 계정 표는 플랫폼 표라 시스템 연결로 읽는다(물리 설계 3-3, 쓰기는 감사 로그뿐). 로그인 쿠키는 부르는 화면이 바꾼다.
/// </summary>
public sealed class ImpersonationService(ISystemDb systemDb, TimeProvider time)
{
    public const string StartAction = "ImpersonateStart";
    public const string StopAction = "ImpersonateStop";

    private async Task<bool> IsAdminAsync(AppDbContext ctx, string userName, CancellationToken ct)
    {
        var u = await ctx.Users.AsNoTracking().SingleOrDefaultAsync(a => a.UserName == userName, ct);
        return u is not null && !u.IsDisabled(time.GetUtcNow()) && await AccountService.IsPlatformAdminAsync(ctx, u.Id, ct);
    }

    /// <summary>대상: 플랫폼 관리자를 뺀 사용 중인 계정. 성공하면 대상 계정(쿠키를 이걸로 바꾼다).</summary>
    public async Task<(ImpersonateError Error, AppUser? Target)> StartAsync(string actor, string targetId, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await IsAdminAsync(ctx, actor, ct)) return (ImpersonateError.NotAdmin, null);
        if (actor == targetId) return (ImpersonateError.Self, null);
        var t = await ctx.Users.AsNoTracking().SingleOrDefaultAsync(a => a.UserName == targetId, ct);
        if (t is null) return (ImpersonateError.NotFound, null);
        if (await AccountService.IsPlatformAdminAsync(ctx, t.Id, ct)) return (ImpersonateError.PlatformAdmin, null);
        if (t.IsDisabled(time.GetUtcNow())) return (ImpersonateError.Suspended, null);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = StartAction, Detail = $"account={targetId}", ImpersonatorUserName = actor });
        await ctx.SaveChangesAsync(ct);
        return (ImpersonateError.None, t);
    }

    /// <summary>복귀: 가장한 관리자가 지금도 플랫폼 관리자면 그 계정을 돌려준다(아니면 null — 로그아웃시킨다).</summary>
    public async Task<AppUser?> StopAsync(string impersonator, string current, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(impersonator);
        if (!await IsAdminAsync(ctx, impersonator, ct)) return null;
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = impersonator, Action = StopAction, Detail = $"account={current}", ImpersonatorUserName = impersonator });
        await ctx.SaveChangesAsync(ct);
        return await ctx.Users.AsNoTracking().SingleAsync(a => a.UserName == impersonator, ct);
    }

    public static string Message(ImpersonateError e) => e switch
    {
        ImpersonateError.PlatformAdmin => "플랫폼 관리자 계정으로는 들어갈 수 없어요.",
        ImpersonateError.Suspended => "정지된 계정으로는 들어갈 수 없어요.",
        ImpersonateError.Self => "내 계정이에요.",
        _ => "들어갈 수 없는 계정이에요.",
    };
}
