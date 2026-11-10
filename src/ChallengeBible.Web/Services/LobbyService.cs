using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>진짜 부서로 옮긴 결과: 옮긴 미션 기록 · 점수 합 · 퀴즈 시도, 같은 날 같은 미션이 있어 남겨 둔 기록.</summary>
public sealed record LobbyMove(int Activities, int Points, int QuizAttempts, int Skipped);

/// <summary>
/// 임시 부서 "처음 오신 분"(ADR 0064, R-171 ~ R-174). 시스템 연결 처리기(물리 설계 3-3).
/// 소속은 DeptRoleUserAssoc 한 줄이다(0090): 다른 부서로 가면 그 줄이 옮겨 가고 기록도 함께 옮긴다.
/// </summary>
public sealed class LobbyService(ISystemDb systemDb, TimeProvider time)
{
    public const string MovedAction = "LobbyMoved";

    /// <summary>R-171: 가입하면 임시 부서의 참여자가 된다(승인 절차 없음). 임시 부서가 없거나 이미 소속이 있으면 아무것도 하지 않는다.</summary>
    public async Task<bool> EnrollAsync(string userName, string userId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(userName);
        var lobby = await sys.Tenants.Where(t => t.IsLobby && t.Status == TenantStatus.Active).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        if (lobby is not int id) return false;
        if (await sys.DeptRoles.AnyAsync(d => d.UserId == userId, ct)) return true;
        var now = time.GetUtcNow().UtcDateTime;
        sys.DeptRoles.Add(new DeptRoleUserAssoc { TenantId = id, UserId = userId, RoleCode = UserRole.StudentCode, InsertDT = now, UpdateDT = now });
        await sys.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// R-173 · R-174: 임시 부서에 있던 사람이 진짜 부서로 승인되면 그 부서로 옮긴다. <see cref="DeptMembership"/> 이 소속 · 기록을 옮긴다.
    /// 임시 부서에 없으면 null.
    /// </summary>
    public static Task<int?> LobbyIdAsync(AppDbContext sys, CancellationToken ct) =>
        sys.Tenants.Where(t => t.IsLobby).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
}
