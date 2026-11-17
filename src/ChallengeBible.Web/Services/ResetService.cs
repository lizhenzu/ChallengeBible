using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>완전 초기화의 지울 것 하나(체크 칸 하나). Group = 기록 · 사람.</summary>
public sealed record ResetItem(string Key, string Group, string Label, int Count);

public sealed record ResetStatus(IReadOnlyList<ResetItem> Items, IReadOnlyList<(DateTime AtUtc, string Actor, string? Detail)> History);

/// <summary>
/// 완전 초기화(ADR 0102, R-314, 2026-11-16 사용자): 새해 초기화 · 관리자만 남기기 · 가짜 데이터 지우기를 하나로 합친 화면.
/// 지울 것을 체크 칸으로 고르고(기본 모두), 체크를 뺀 것은 남긴다. 모든 부서에서 한 번에, 되돌릴 수 없다.
/// 절대 안 지움: 플랫폼 관리자 · 부서 관리자 계정(과 그 소속 · 명부 줄 · 반 배정), 부서, 반(이름 · 학년 · 새친구반), 상품 · 설정 · 콘텐츠 · 규칙.
/// 사람(계정 또는 명부)을 지우면 그 사람의 기록이 남을 수 없으므로 기록은 체크와 상관없이 모두 지운다.
/// 계정만 지우고 명부를 남기면 명부 줄의 계정 연결만 끊는다.
/// </summary>
public sealed class ResetService(ISystemDb systemDb, TimeProvider time)
{
    public const string Action = "FullReset";
    public const string ConfirmWord = "RESET";
    public const string Zone = "Asia/Seoul";

    public const string Records = "기록", People = "사람";
    public static readonly IReadOnlyList<(string Key, string Group, string Label)> Catalog =
    [
        ("missions", Records, "미션 · 퀴즈(점수 · 순위)"),
        ("attend", Records, "출석(새친구 포함)"),
        ("gifts", Records, "선물 · 선물 이력"),
        ("awards", Records, "시상 기록"),
        ("teams", Records, "가족팀 · 친구팀"),
        ("reading", Records, "성경통독"),
        ("copy", Records, "성경필사"),
        ("prayer", Records, "365 기도 기록"),   // ADR 0106
        ("requests", Records, "진급 · 참여 신청"),
        ("logs", Records, "검색 기록 · 감사 로그"),
        ("accounts", People, "계정(학생 · 교사 · 학부모 · 가짜)"),
        ("roster", People, "명부 · 반 배정 · 보호자 연결"),
    ];

    public static IReadOnlyList<string> AllKeys => [.. Catalog.Select(c => c.Key)];

    /// <summary>실제로 지울 것: 사람(계정 · 명부) 중 하나라도 고르면 기록은 모두 함께.</summary>
    public static HashSet<string> Effective(IEnumerable<string>? picked)
    {
        var set = new HashSet<string>((picked ?? []).Where(k => Catalog.Any(c => c.Key == k)));
        if (set.Contains("accounts") || set.Contains("roster"))
            foreach (var c in Catalog.Where(c => c.Group == Records)) set.Add(c.Key);
        return set;
    }

    /// <summary>남길 계정: Identity 역할 Admin · SuperAdmin(CLAUDE.md "관리자는 절대 지우지 않는다").</summary>
    private static IQueryable<string> KeepIds(AppDbContext ctx) =>
        from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
        where r.Name == IdentityRoles.Admin || r.Name == IdentityRoles.SuperAdmin
        select ur.UserId;

    private static IQueryable<AppUser> GoneUsers(AppDbContext ctx) { var keep = KeepIds(ctx); return ctx.Users.Where(u => !keep.Contains(u.Id)); }
    private static IQueryable<Person> GonePeople(AppDbContext ctx) { var keep = KeepIds(ctx); return ctx.People.Where(p => p.UserId == null || !keep.Contains(p.UserId)); }

    private static async Task<int> CountAsync(AppDbContext ctx, string key, CancellationToken ct)
    {
        var keep = KeepIds(ctx);
        return key switch
        {
            "missions" => await ctx.MissionActivities.CountAsync(ct) + await ctx.QuizAttempts.CountAsync(ct),
            "attend" => await ctx.AttendanceChecks.CountAsync(ct),
            "gifts" => await ctx.Gifts.CountAsync(ct),
            "awards" => await ctx.TeamAwards.CountAsync(ct) + await ctx.TeamAwardArchives.CountAsync(ct),
            "teams" => await ctx.FamilyTeams.CountAsync(ct),
            "reading" => await ctx.ReadingRounds.CountAsync(ct),
            "copy" => await ctx.CopyVerses.CountAsync(ct),
            "prayer" => await ctx.Prayer365Logs.CountAsync(ct),
            "requests" => await ctx.PromotionRequests.CountAsync(ct) + await ctx.SignupApplications.CountAsync(s => !keep.Contains(s.UserId), ct),
            "logs" => await ctx.SearchLogs.CountAsync(ct) + await ctx.AuditLogs.CountAsync(a => a.Action != Action, ct),
            "accounts" => await GoneUsers(ctx).CountAsync(ct),
            "roster" => await GonePeople(ctx).CountAsync(ct),
            _ => 0,
        };
    }

    public async Task<ResetStatus> StatusAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var items = new List<ResetItem>();
        foreach (var (key, group, label) in Catalog) items.Add(new ResetItem(key, group, label, await CountAsync(ctx, key, ct)));
        var history = (await ctx.AuditLogs.AsNoTracking().Where(a => a.Action == Action || a.Action == YearResetService.Action).OrderByDescending(a => a.Id).Take(10)
            .Select(a => new { a.OccurredUtc, a.ActorUserName, a.Detail }).ToListAsync(ct)).Select(a => (a.OccurredUtc, a.ActorUserName, a.Detail)).ToList();
        return new ResetStatus(items, history);
    }

    /// <summary>지우기: DB 에서는 한 번에(ExecuteDelete), 시험용 메모리 DB 는 읽어서 지운다.</summary>
    private static async Task DeleteAsync<T>(AppDbContext ctx, IQueryable<T> q, CancellationToken ct) where T : class
    {
        if (ctx.Database.IsRelational()) await q.ExecuteDeleteAsync(ct);
        else { ctx.Set<T>().RemoveRange(await q.ToListAsync(ct)); await ctx.SaveChangesAsync(ct); }
    }

    /// <summary>고른 것을 지우고, 지운 건수를 "키=수;…" 로 돌려준다(초기화 기록에 남김). 아무것도 안 골랐으면 null.</summary>
    public async Task<string?> ResetAsync(string actor, IEnumerable<string>? picked, CancellationToken ct = default)
    {
        var on = Effective(picked);
        if (on.Count == 0) return null;
        await using var ctx = systemDb.Create(actor);
        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        var keep = KeepIds(ctx);
        var detail = new List<string>();
        foreach (var (key, _, _) in Catalog.Where(c => on.Contains(c.Key))) detail.Add($"{key}={await CountAsync(ctx, key, ct)}");
        var goneNames = GoneUsers(ctx).Select(u => u.UserName);

        if (on.Contains("gifts"))
        {
            await DeleteAsync(ctx, ctx.GiftLogs, ct);
            await DeleteAsync(ctx, ctx.GiftPointUses, ct);
            await DeleteAsync(ctx, ctx.Gifts, ct);
            await DeleteAsync(ctx, ctx.GiftYearCloses, ct);   // 초기화 시각 기록도(처음 상태와 똑같이 계산)
        }
        if (on.Contains("awards"))
        {
            await DeleteAsync(ctx, ctx.TeamAwards, ct);
            await DeleteAsync(ctx, ctx.TeamAwardArchives, ct);
        }
        if (on.Contains("missions"))
        {
            await DeleteAsync(ctx, ctx.QuizAttempts, ct);
            await DeleteAsync(ctx, ctx.MissionActivities, ct);
        }
        if (on.Contains("attend")) await DeleteAsync(ctx, ctx.AttendanceChecks, ct);
        if (on.Contains("teams"))
        {
            await DeleteAsync(ctx, ctx.FamilyRequests, ct);
            await DeleteAsync(ctx, ctx.FamilyNotices, ct);
            await DeleteAsync(ctx, ctx.FamilyMilestonesHidden, ct);
            await DeleteAsync(ctx, ctx.FamilyMembers, ct);
            await DeleteAsync(ctx, ctx.FamilyTeams, ct);
        }
        if (on.Contains("reading"))
        {
            await DeleteAsync(ctx, ctx.ReadingVerses, ct);
            await DeleteAsync(ctx, ctx.ReadingChapterReads, ct);
            await DeleteAsync(ctx, ctx.ReadingChapterProgresses, ct);
            await DeleteAsync(ctx, ctx.ReadingBookmarks, ct);
            await DeleteAsync(ctx, ctx.ReadingRounds, ct);
        }
        if (on.Contains("copy")) await DeleteAsync(ctx, ctx.CopyVerses, ct);
        if (on.Contains("prayer"))
        {
            await DeleteAsync(ctx, ctx.Prayer365Logs, ct);
            await DeleteAsync(ctx, ctx.Prayer365Views, ct);   // 기도문(공용 콘텐츠)은 그대로
        }
        if (on.Contains("requests"))
        {
            await DeleteAsync(ctx, ctx.PromotionRequests, ct);
            await DeleteAsync(ctx, ctx.SignupApplications.Where(s => !keep.Contains(s.UserId)), ct);
        }
        if (on.Contains("logs"))
        {
            await DeleteAsync(ctx, ctx.SearchLogs, ct);
            await DeleteAsync(ctx, ctx.AuditLogs, ct);
        }

        // 명부(관리자 줄은 남김): 반 배정 · 보호자 연결 → 명부 줄. 반은 절대 지우지 않는다
        if (on.Contains("roster"))
        {
            await DeleteAsync(ctx, ctx.ClassMembers.Where(m => ctx.People.Any(p => p.TenantId == m.TenantId && p.Id == m.PersonId && (p.UserId == null || !keep.Contains(p.UserId)))), ct);
            await DeleteAsync(ctx, ctx.GuardianLinks, ct);
            await DeleteAsync(ctx, GonePeople(ctx), ct);
        }

        // 계정(관리자는 남김): 소속 · 업무 꼬리표 · 가입 신청 · 설정 → Identity. 명부를 남기면 계정 연결만 끊는다
        if (on.Contains("accounts"))
        {
            if (!on.Contains("roster"))
            {
                var linked = await ctx.People.Where(p => p.UserId != null && !keep.Contains(p.UserId)).ToListAsync(ct);
                foreach (var p in linked) p.UserId = null;
                await ctx.SaveChangesAsync(ct);
                await DeleteAsync(ctx, ctx.GuardianLinks, ct);   // 보호자 연결은 아이디로 잇는다 — 계정이 없으면 남을 수 없다
            }
            await DeleteAsync(ctx, ctx.DeptRoles.Where(d => d.UserId == null || !keep.Contains(d.UserId)), ct);
            await DeleteAsync(ctx, ctx.MemberTags.Where(t => !keep.Contains(t.UserId)), ct);
            await DeleteAsync(ctx, ctx.SignupApplications.Where(s => !keep.Contains(s.UserId)), ct);
            await DeleteAsync(ctx, ctx.UserSettings.Where(s => goneNames.Contains(s.UserName)), ct);
            // 쪽지(ADR 0112): 쪽지 자체는 초기화에서 남기지만, 지우는 계정이 주고받은 쪽지 · 읽음 기록은 함께 지운다
            var goneMsgs = ctx.Messages.Where(m => (m.SenderUserName != null && goneNames.Contains(m.SenderUserName)) || (m.RecipientUserName != null && goneNames.Contains(m.RecipientUserName))).Select(m => m.Id);
            await DeleteAsync(ctx, ctx.MessageReceipts.Where(r => goneNames.Contains(r.UserName) || goneMsgs.Contains(r.MessageId)), ct);
            await DeleteAsync(ctx, ctx.MessageTargets.Where(t => goneMsgs.Contains(t.MessageId)), ct);
            await DeleteAsync(ctx, ctx.Messages.Where(m => (m.SenderUserName != null && goneNames.Contains(m.SenderUserName)) || (m.RecipientUserName != null && goneNames.Contains(m.RecipientUserName))), ct);
            if (ctx.Database.IsRelational())
            {
                const string keepSql = "SELECT ur.UserId FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE r.NormalizedName IN (N'ADMIN', N'SUPERADMIN')";
                foreach (var t in new[] { "UserTokens", "UserLogins", "UserClaims" })
                    await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM [Identity].[{t}] WHERE UserId NOT IN ({keepSql})", ct);
            }
            await DeleteAsync(ctx, ctx.UserRoles.Where(r => !keep.Contains(r.UserId)), ct);
            await DeleteAsync(ctx, ctx.Users.Where(u => !keep.Contains(u.Id)), ct);
        }

        var text = string.Join(";", detail);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = Action, Detail = text });   // 로그를 지워도 이번 초기화 한 줄은 남긴다
        await ctx.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return text;
    }

    /// <summary>초기화 기록 한 줄 풀이: "missions=3;attend=2" → "미션 · 퀴즈 3 · 출석 2".</summary>
    public static string Describe(string? detail) => string.Join(" · ", (detail ?? "").Split(';').Select(p => p.Split('=')).Where(p => p.Length == 2).Select(p =>
    {
        var label = p[0] switch
        {
            "missions" => "미션", "attend" => "출석", "gifts" => "선물", "awards" => "시상", "teams" => "팀", "reading" => "통독", "copy" => "필사", "prayer" => "365 기도",
            "requests" => "신청", "logs" => "로그", "audits" => "감사 로그", "accounts" => "계정", "people" or "roster" => "명부", _ => p[0],
        };
        return label + " " + (int.TryParse(p[1], out var n) ? n.ToString("N0") : p[1]);
    }));
}
