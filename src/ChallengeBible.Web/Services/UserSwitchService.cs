using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>바꾸기 목록의 한 사람. Kind = st 학생 · te 교사 · of 임원 · pa 학부모 · et 기타 · no 부서 없음.</summary>
public sealed record SwitchRow(string UserName, string Name, int? TenantId, string? TenantName, string? Slug, string Kind, string RoleLabel,
    bool Admin, string? ClassText, bool Blocked);

public sealed record SwitchDept(int Id, string Name, int Count);

public sealed record SwitchList(IReadOnlyList<SwitchDept> Depts, int Dept, IReadOnlyList<(string Key, string Label, int Count)> Roles, string Role,
    string? Q, IReadOnlyList<SwitchRow> Recent, IReadOnlyList<SwitchRow> Rows, bool More, int Take);

/// <summary>
/// 사람 바꾸기(확정 시안 사람바꾸기, ADR 0096): 가장 중인 플랫폼 관리자가 로그아웃 없이 다른 계정으로 바꾼다. 대상 = 모든 계정
/// (플랫폼 관리자 · 정지된 계정은 들어갈 수 없음 — 흐리게). 계정 · 소속 · 반을 시스템 연결로 읽는다(쓰기는 감사 로그뿐).
/// </summary>
public sealed class UserSwitchService(ISystemDb systemDb, TimeProvider time)
{
    public const string SwitchAction = "ImpersonateSwitch";
    public const int PageSize = 30;
    public const int NoDept = 0;
    public static readonly (string Key, string Label)[] RoleKeys = [("all", "전체"), ("st", "학생"), ("te", "교사"), ("of", "임원"), ("pa", "학부모"), ("et", "기타"), ("ad", "관리자")];

    private static string KindKey(UserRoleKind k) => k switch
    {
        UserRoleKind.Student => "st", UserRoleKind.Teacher => "te", UserRoleKind.Officer => "of", UserRoleKind.Etc => "et", _ => "pa",
    };

    /// <summary>모든 계정을 한 번에 읽어 줄로 만든다(교회 규모라 메모리에서 거른다).</summary>
    private async Task<List<SwitchRow>> AllAsync(AppDbContext ctx, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var users = await ctx.Users.AsNoTracking().Select(u => new { u.Id, u.UserName, u.LastName, u.FirstName, u.LockoutEnd }).ToListAsync(ct);
        var roles = await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                           where r.Name == IdentityRoles.SuperAdmin || r.Name == IdentityRoles.Admin select new { ur.UserId, r.Name }).ToListAsync(ct);
        var super = roles.Where(r => r.Name == IdentityRoles.SuperAdmin).Select(r => r.UserId).ToHashSet();
        var admin = roles.Where(r => r.Name == IdentityRoles.Admin).Select(r => r.UserId).ToHashSet();
        var tenants = await ctx.Tenants.AsNoTracking().Select(t => new { t.Id, t.Name, t.Slug }).ToDictionaryAsync(t => t.Id, ct);
        var members = await ctx.DeptRoles.AsNoTracking().Select(d => new { d.UserId, d.TenantId, d.RoleCode }).ToListAsync(ct);
        var codes = (await ctx.CodeData.AsNoTracking().Where(c => c.MajorCD == CodeData.RoleMajor).Select(c => new { c.TenantId, c.MinorCD, c.MinorNM }).ToListAsync(ct))
            .GroupBy(c => (c.TenantId, c.MinorCD)).ToDictionary(g => g.Key, g => g.First().MinorNM);
        // 반: 부서마다 가장 최근 해(반 해)의 반 · 담임
        var terms = (await ctx.ClassTerms.AsNoTracking().Select(t => new { t.TenantId, t.Id, t.StartDate }).ToListAsync(ct))
            .GroupBy(t => t.TenantId).Select(g => g.OrderByDescending(t => t.StartDate).First().Id).ToList();
        var classes = (await (from m in ctx.ClassMembers join c in ctx.Classes on m.ClassId equals c.Id join p in ctx.People on m.PersonId equals p.Id
                              where terms.Contains(m.TermId) && p.UserId != null
                              select new { p.UserId, c.Name, c.IsNewcomer, m.IsTeacher }).ToListAsync(ct))
            .GroupBy(x => x.UserId!).ToDictionary(g => g.Key, g => g.First());

        var rows = new List<SwitchRow>();
        foreach (var u in users)
        {
            if (u.UserName is null) continue;
            var name = AppUser.FullName(u.LastName, u.FirstName);
            var blocked = super.Contains(u.Id) || (u.LockoutEnd is { } end && end > now);
            var m = members.FirstOrDefault(x => x.UserId == u.Id);
            classes.TryGetValue(u.Id, out var cls);
            var classText = cls is null ? null : cls.IsTeacher ? (cls.IsNewcomer ? "새친구반 담당" : cls.Name + " 담임") : cls.Name;
            if (m is null)
            {
                rows.Add(new SwitchRow(u.UserName, name.Length == 0 ? u.UserName : name, null, null, null, "no", super.Contains(u.Id) ? "플랫폼 관리자" : "부서 없음", false, null, blocked));
                continue;
            }
            var kind = UserRole.KindOf(m.RoleCode);
            var label = kind == UserRoleKind.Parent ? "학부모"
                : codes.TryGetValue((m.TenantId, (m.RoleCode ?? "").Trim()), out var nm) ? nm : UserRole.LabelOf(kind);
            tenants.TryGetValue(m.TenantId, out var t);
            rows.Add(new SwitchRow(u.UserName, name.Length == 0 ? u.UserName : name, m.TenantId, t?.Name, t?.Slug, KindKey(kind), label,
                admin.Contains(u.Id), classText, blocked));
        }
        return rows;
    }

    /// <summary>
    /// 목록: q 가 있으면 모든 부서에서 이름 · 아이디로 찾고, 없으면 dept(없으면 지금 사람의 부서)의 사람. role = all · st · te · of · pa · et · ad. 이름 순 take 명.
    /// 최근에 본 사람 = 이 관리자의 가장 시작 · 바꾸기 감사 로그에서 최근 5명(지금 사람 포함).
    /// </summary>
    public async Task<SwitchList?> ListAsync(string admin, string current, int? dept, string? role, string? q, int take, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(admin);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, admin, ct)) return null;   // 가장한 관리자가 지금도 플랫폼 관리자일 때만
        var all = await AllAsync(ctx, ct);
        var byUser = all.ToDictionary(r => r.UserName, StringComparer.OrdinalIgnoreCase);
        var me = byUser.GetValueOrDefault(current);
        var depts = all.Where(r => r.TenantId is not null).GroupBy(r => (r.TenantId!.Value, r.TenantName ?? "")).OrderBy(g => g.Key.Item2)
            .Select(g => new SwitchDept(g.Key.Value, g.Key.Item2, g.Count())).ToList();
        var noDept = all.Count(r => r.TenantId is null);
        if (noDept > 0) depts.Add(new SwitchDept(NoDept, "부서 없음", noDept));
        var d = dept ?? me?.TenantId ?? depts.FirstOrDefault()?.Id ?? NoDept;

        var text = q?.Trim();
        IEnumerable<SwitchRow> pool = string.IsNullOrEmpty(text)
            ? all.Where(r => (r.TenantId ?? NoDept) == d)
            : all.Where(r => r.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || r.UserName.Contains(text, StringComparison.OrdinalIgnoreCase));
        var list = pool.ToList();
        var counts = RoleKeys.Select(k => (k.Key, k.Label, k.Key switch
        {
            "all" => list.Count,
            "ad" => list.Count(r => r.Admin),
            _ => list.Count(r => r.Kind == k.Key),
        })).Where(x => x.Key == "all" || x.Item3 > 0).ToList();
        var r0 = RoleKeys.Any(k => k.Key == role) ? role! : "all";
        var picked = r0 switch { "all" => list, "ad" => list.Where(r => r.Admin).ToList(), _ => list.Where(r => r.Kind == r0).ToList() };
        var rows = picked.OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.UserName).ToList();

        var seen = await ctx.AuditLogs.AsNoTracking()
            .Where(a => a.ImpersonatorUserName == admin && (a.Action == ImpersonationService.StartAction || a.Action == SwitchAction))
            .OrderByDescending(a => a.Id).Take(50).Select(a => a.Detail).ToListAsync(ct);
        var recent = new List<SwitchRow>();
        foreach (var who in new[] { current }.Concat(seen.Select(AccountOf)))
        {
            if (who is null || recent.Any(r => r.UserName.Equals(who, StringComparison.OrdinalIgnoreCase))) continue;
            if (byUser.TryGetValue(who, out var row)) recent.Add(row);
            if (recent.Count == 5) break;
        }
        return new SwitchList(depts, d, counts, r0, text, recent, rows.Take(take).ToList(), rows.Count > take, take);
    }

    /// <summary>감사 로그 Detail "from=a;account=b" 또는 "account=b" 에서 b.</summary>
    public static string? AccountOf(string? detail)
    {
        if (detail is null) return null;
        foreach (var part in detail.Split(';'))
            if (part.StartsWith("account=", StringComparison.Ordinal)) return part["account=".Length..];
        return null;
    }

    /// <summary>바꾸기: 관리자가 지금도 플랫폼 관리자인지 다시 보고, 대상이 들어갈 수 있는 계정이면 감사 로그를 남기고 대상과 그 부서 주소를 돌려준다.</summary>
    public async Task<(ImpersonateError Error, AppUser? Target, string? Slug)> SwitchAsync(string admin, string current, string targetId, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(admin);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, admin, ct)) return (ImpersonateError.NotAdmin, null, null);
        if (admin.Equals(targetId, StringComparison.OrdinalIgnoreCase)) return (ImpersonateError.Self, null, null);
        var t = await ctx.Users.AsNoTracking().SingleOrDefaultAsync(a => a.UserName == targetId, ct);
        if (t is null) return (ImpersonateError.NotFound, null, null);
        if (await AccountService.IsPlatformAdminAsync(ctx, t.Id, ct)) return (ImpersonateError.PlatformAdmin, null, null);
        if (t.IsDisabled(time.GetUtcNow())) return (ImpersonateError.Suspended, null, null);
        var slug = await (from d in ctx.DeptRoles join x in ctx.Tenants on d.TenantId equals x.Id where d.UserId == t.Id select x.Slug).FirstOrDefaultAsync(ct);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = admin, Action = SwitchAction, Detail = $"from={current};account={t.UserName}", ImpersonatorUserName = admin });
        await ctx.SaveChangesAsync(ct);
        return (ImpersonateError.None, t, slug);
    }

    /// <summary>바꾼 뒤 갈 곳: 보던 화면이 대상의 부서 화면이면 그대로, 아니면 대상 부서 홈(부서가 없으면 첫 화면). 계정 · 플랫폼 화면은 부서 홈으로.</summary>
    public static string Next(string? back, string? slug)
    {
        var home = slug is null ? "/" : "/" + slug;
        if (string.IsNullOrEmpty(back) || !back.StartsWith('/') || back.StartsWith("//") || slug is null) return home;
        var first = back.TrimStart('/').Split('/', '?', '#')[0];
        return first.Equals(slug, StringComparison.OrdinalIgnoreCase) ? back : home;
    }
}
