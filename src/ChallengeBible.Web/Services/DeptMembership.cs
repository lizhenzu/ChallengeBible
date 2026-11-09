using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public enum MoveError { None, TeamLocked }

/// <summary>
/// 사람의 소속을 한 곳에서 바꾼다(ADR 0090: 한 사람 한 부서, DeptRoleUserAssoc 한 줄). 승인 · 진급 · 해제가 같이 쓴다.
/// 다른 부서로 옮기면 미션 기록을 옮기고 그 부서의 팀 자리 · 보호 관계를 정리한다. 시상 기록이 있는 팀에 있으면 옮기지 않는다.
/// 시스템 연결의 트랜잭션 안에서 부른다.
/// </summary>
public static class DeptMembership
{
    public static async Task<(MoveError Error, LobbyMove Moved)> PlaceAsync(AppDbContext sys, AppUser user, int target, string? roleCode, DateTime now, CancellationToken ct)
    {
        var moved = new LobbyMove(0, 0, 0, 0);
        var userName = user.UserName!;
        var rows = await sys.DeptRoles.Where(d => d.UserId == user.Id).OrderBy(d => d.Id).ToListAsync(ct);
        var from = rows.FirstOrDefault(r => r.TenantId != target)?.TenantId;
        if (from is int source)
        {
            if (await FamilyTeamService.IsLockedAsync(sys, source, userName, ct)) return (MoveError.TeamLocked, moved);
            moved = await RecordMover.MoveAsync(sys, userName, source, target, ct);
            await FamilyTeamService.RemovePersonAsync(sys, source, userName, ct);
            var links = await sys.GuardianLinks.Where(l => l.TenantId == source && (l.GuardianUserName == userName || l.StudentUserName == userName)).ToListAsync(ct);
            sys.GuardianLinks.RemoveRange(links);
        }

        var keep = rows.FirstOrDefault(r => r.TenantId == target);
        sys.DeptRoles.RemoveRange(rows.Where(r => r != keep));
        if (keep is null) sys.DeptRoles.Add(new DeptRoleUserAssoc { TenantId = target, UserId = user.Id, RoleCode = roleCode, InsertDT = now, UpdateDT = now });
        else { keep.RoleCode = roleCode; keep.UpdateDT = now; }

        var setting = await sys.UserSettings.SingleOrDefaultAsync(s => s.UserName == userName, ct);
        if (setting is not null && from is int f && setting.LastTenantId == f) setting.LastTenantId = target;
        await sys.SaveChangesAsync(ct);
        return (MoveError.None, moved);
    }
}
