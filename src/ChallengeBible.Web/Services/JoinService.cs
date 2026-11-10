using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record JoinTarget(int TenantId, string Name, bool MissionEnabled, bool ReadingEnabled)
{
    public string ThemeColor { get; init; } = "blue";
}

public enum JoinError { None, NotFound, AlreadyMember, NoRole, BadRole }

/// <summary>
/// 목록에서 부서를 고르거나(0059) 참여 코드로 부서를 찾아 참여 신청을 한다(0045, 0048). 신청은 "신청" 상태 소속을 만들고,
/// 부서 관리자가 승인하면 그때 쓸 수 있다(R-130). 역할은 학생 · 교사 · 학부모 · 기타 중 하나만 묻는다(유년부와 같다).
/// 임시 부서(0064)는 신청 대상이 아니다(목록 · 코드 · 번호 모두에서 뺌) — 가입하면 저절로 들어간다.
/// </summary>
public sealed class JoinService(IAppDb db, RequestScope scope, TimeProvider time)
{
        public static string NormalizeCode(string code) =>
        new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>정지된 부서는 "없음"과 똑같이 답한다(0048 결정 5).</summary>
    public async Task<JoinTarget?> LookupAsync(string code, CancellationToken ct = default)
    {
        var c = NormalizeCode(code);
        if (c.Length != 6) return null;
        await using var ctx = db.Create();
        return await ctx.Tenants
            .Where(t => t.JoinCode == c && t.Status == TenantStatus.Active && !t.IsLobby)
            .Select(t => new JoinTarget(t.Id, t.Name, t.MissionEnabled, t.ReadingEnabled) { ThemeColor = t.ThemeColor })
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>참여 신청 화면의 부서 목록(0059): 플랫폼이 "목록에 보이기"를 켠, 운영 중인 부서만.</summary>
    public async Task<List<JoinTarget>> ListedAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Tenants
            .Where(t => t.JoinListed && t.Status == TenantStatus.Active && !t.IsLobby)
            .OrderBy(t => t.Name)
            .Select(t => new JoinTarget(t.Id, t.Name, t.MissionEnabled, t.ReadingEnabled) { ThemeColor = t.ThemeColor })
            .ToListAsync(ct);
    }

    /// <summary>
    /// 목록에서 고른 부서로 신청(0059). 목록에 없는 부서는 그 부서의 코드를 함께 받아야 한다
    /// (부서 주소 · 코드로 찾은 경우) — Id 만 바꿔 숨긴 부서에 신청하지 못하게.
    /// </summary>
    public async Task<(JoinError Error, int? ApplicationId)> ApplyAsync(string userName, int tenantId, string? code, string roleKind, CancellationToken ct = default)
    {
        var c = NormalizeCode(code ?? "");
        JoinTarget? target;
        await using (var ctx = db.Create())
            target = await ctx.Tenants
                .Where(t => t.Id == tenantId && t.Status == TenantStatus.Active && !t.IsLobby && (t.JoinListed || t.JoinCode == c))
                .Select(t => new JoinTarget(t.Id, t.Name, t.MissionEnabled, t.ReadingEnabled))
                .SingleOrDefaultAsync(ct);
        return await ApplyToAsync(userName, target, roleKind, ct);
    }

    public async Task<(JoinError Error, int? ApplicationId)> ApplyAsync(string userName, string code, string roleKind, CancellationToken ct = default) =>
        await ApplyToAsync(userName, await LookupAsync(code, ct), roleKind, ct);

    private async Task<(JoinError Error, int? ApplicationId)> ApplyToAsync(string userName, JoinTarget? target, string roleKind, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(roleKind)) return (JoinError.NoRole, null);
        if (!SignupRoleKind.All.Contains(roleKind)) return (JoinError.BadRole, null);
        if (target is null) return (JoinError.NotFound, null);

        // 신청은 그 부서의 행을 만드는 일이라, 이 요청의 영역을 그 부서로 정한 뒤 새 연결로 쓴다(RLS BLOCK 조건).
        scope.SetTenant(target.TenantId);
        await using var ctx = db.Create();
        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.UserName == userName, ct);
        // 한 사람 한 부서(0090): 임시 부서가 아닌 소속이 있거나 어디든 기다리는 신청이 있으면 또 신청할 수 없다
        var lobbies = ctx.Tenants.Where(t => t.IsLobby).Select(t => t.Id);
        if (await ctx.DeptRoles.IgnoreQueryFilters().AnyAsync(d => d.UserId == user.Id && !lobbies.Contains(d.TenantId), ct)
            || await ctx.SignupApplications.IgnoreQueryFilters().AnyAsync(a => a.UserName == userName && a.Status == SignupStatus.Pending, ct))
            return (JoinError.AlreadyMember, null);

        var a = new SignupApplication
        {
            TenantId = target.TenantId, UserId = user.Id, UserName = userName, FullName = user.DisplayName,
            RoleKind = roleKind, InsertDT = time.GetUtcNow().UtcDateTime,
        };
        ctx.SignupApplications.Add(a);
        await ctx.SaveChangesAsync(ct);
        return (JoinError.None, a.Id);
    }

    /// <summary>본인 신청 취소 — 아직 "신청" 상태일 때만.</summary>
    public async Task<bool> CancelAsync(string userName, int tenantId, CancellationToken ct = default)
    {
        scope.SetTenant(tenantId);
        await using var ctx = db.Create();
        var a = await ctx.SignupApplications.SingleOrDefaultAsync(x => x.UserName == userName && x.Status == SignupStatus.Pending, ct);
        if (a is null) return false;
        ctx.SignupApplications.Remove(a);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public static string Message(JoinError e) => e switch
    {
        JoinError.NotFound => "이 코드로 찾은 부서가 없어요. 선생님께 다시 물어봐 주세요.",
        JoinError.AlreadyMember => "이미 이 부서에 신청했거나 들어가 있어요.",
        JoinError.NoRole => "하나는 골라 주세요.",
        JoinError.BadRole => "역할을 다시 골라 주세요.",
        _ => "",
    };
}
