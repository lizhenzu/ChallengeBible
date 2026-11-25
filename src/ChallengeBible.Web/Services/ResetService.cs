using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>완전 초기화의 지울 것 하나(체크 칸 하나). Group = 기록 · 사람.</summary>
public sealed record ResetItem(string Key, string Group, string Label, int Count);

public sealed record ResetStatus(IReadOnlyList<ResetItem> Items, IReadOnlyList<(DateTime AtUtc, string Actor, string? Detail)> History);

/// <summary>
/// 완전 초기화(ADR 0102, R-314, 2026-11-16 사용자): 새해 초기화 · 관리자만 남기기 · 가짜 데이터 지우기를 하나로 합친 화면.
/// 지울 것을 체크 칸으로 고르고(기본 모두), 체크를 뺀 것은 남긴다. 모든 부서에서 한 번에, 되돌릴 수 없다.
/// 절대 안 지움: 플랫폼 관리자 · 부서 관리자 계정(R-315). 직접 가입한 회원은 기본으로 남기고(2026-11-20 사용자), "members"를 고르면 그들도 지운다(2026-11-21 사용자 "관리자 빼고 모든 사용자 다 삭제")과 그 사람들의 소속 · 명부 줄 · 반 배정 · 기록,
/// 부서, 반(이름 · 학년 · 새친구반), 상품 · 설정 · 콘텐츠 · 규칙.
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
        ("prayer", Records, "365축복기도 기록"),   // ADR 0106
        ("requests", Records, "진급 · 참여 신청"),
        ("logs", Records, "검색 기록 · 감사 로그"),
        ("accounts", People, "가짜 계정(@fake-seed.test)"),
        ("members", People, "직접 가입한 회원 계정(관리자 빼고 모두)"),   // 2026-11-21 사용자 — 기본은 꺼짐
        ("roster", People, "명부 · 반 배정 · 보호자 연결"),
    ];

    public static IReadOnlyList<string> AllKeys => [.. Catalog.Select(c => c.Key)];

    /// <summary>실제로 지울 것: 사람(계정 · 명부) 중 하나라도 고르면 기록은 모두 함께.</summary>
    public static HashSet<string> Effective(IEnumerable<string>? picked)
    {
        var set = new HashSet<string>((picked ?? []).Where(k => Catalog.Any(c => c.Key == k)));
        if (set.Contains("members")) { set.Add("accounts"); set.Add("roster"); }   // 회원까지 지우면 계정 · 명부 · 기록 모두
        if (set.Contains("accounts") || set.Contains("roster"))
            foreach (var c in Catalog.Where(c => c.Group == Records)) set.Add(c.Key);
        return set;
    }

    /// <summary>
    /// 남길 계정: Identity 역할 Admin · SuperAdmin(CLAUDE.md "관리자는 절대 지우지 않는다") + 직접 가입한 진짜 회원(이메일이 가짜 데이터 주소가 아닌 계정, 2026-11-20 사용자).
    /// 지우는 계정은 가짜 데이터(이메일 @fake-seed.test)뿐이다.
    /// </summary>
    /// <summary>이번 계산에서 직접 가입한 회원도 지우나("members").</summary>
    private bool _members;

    /// <summary>처음 화면에서 기본으로 체크하지 않는 것(직접 가입한 회원 지우기).</summary>
    public static bool OffByDefault(string key) => key == "members";

    private IQueryable<string> KeepIds(AppDbContext ctx)
    {
        var drop = _members;
        var admins = from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                     where r.Name == IdentityRoles.Admin || r.Name == IdentityRoles.SuperAdmin
                     select ur.UserId;
        return ctx.Users.Where(u => admins.Contains(u.Id) || (!drop && (u.Email == null || !u.Email.EndsWith(TestToolsService.EmailDomain)))).Select(u => u.Id);
    }

    /// <summary>남길 계정의 아이디(기록은 아이디로 잇는다). 이 밖의 아이디 기록 = 가짜 계정 · 이미 없는 계정의 기록.</summary>
    private IQueryable<string> KeepNames(AppDbContext ctx) { var keep = KeepIds(ctx); return ctx.Users.Where(u => keep.Contains(u.Id)).Select(u => u.UserName!); }

    private IQueryable<AppUser> GoneUsers(AppDbContext ctx) { var keep = KeepIds(ctx); return ctx.Users.Where(u => !keep.Contains(u.Id)); }
    private IQueryable<Person> GonePeople(AppDbContext ctx) { var keep = KeepIds(ctx); return ctx.People.Where(p => p.UserId == null || !keep.Contains(p.UserId)); }

    /// <summary>남길 사람(관리자 · 진짜 회원)과 이어진 명부 줄 — 그 줄의 출석 · 선물은 남긴다.</summary>
    private IQueryable<int> KeepPeopleIds(AppDbContext ctx) { var keep = KeepIds(ctx); return ctx.People.Where(p => p.UserId != null && keep.Contains(p.UserId)).Select(p => p.Id); }

    /// <summary>남길 사람이 한 명도 없는 팀(가족팀 · 친구팀).</summary>
    private IQueryable<FamilyTeam> GoneTeams(AppDbContext ctx)
    {
        var names = KeepNames(ctx);
        return ctx.FamilyTeams.Where(t => !ctx.FamilyMembers.Any(m => m.TenantId == t.TenantId && m.FamilyTeamId == t.Id && names.Contains(m.UserName)));
    }

    /// <summary>지울 선물: 가짜 계정 · 지울 명부 줄 · 지울 팀의 선물. 남길 사람 것은 남긴다.</summary>
    private IQueryable<Gift> GoneGifts(AppDbContext ctx)
    {
        var names = KeepNames(ctx); var people = KeepPeopleIds(ctx); var teams = GoneTeams(ctx).Select(t => t.Id);
        return ctx.Gifts.Where(g => (g.UserName != null && !names.Contains(g.UserName))
            || (g.UserName == null && g.PersonId != null && !people.Contains(g.PersonId.Value))
            || (g.UserName == null && g.PersonId == null && g.FamilyTeamId != null && teams.Contains(g.FamilyTeamId.Value)));
    }

    /// <summary>지울 시상: 지울 팀의 이름(AwardKey)으로 받은 것.</summary>
    private IQueryable<string> GoneAwardKeys(AppDbContext ctx)
    {
        var names = KeepNames(ctx);
        var keepKeys = ctx.FamilyTeams.Where(t => t.AwardKey != null && ctx.FamilyMembers.Any(m => m.TenantId == t.TenantId && m.FamilyTeamId == t.Id && names.Contains(m.UserName))).Select(t => t.AwardKey!);
        return ctx.TeamAwards.Select(a => a.AwardKey).Union(ctx.TeamAwardArchives.Select(a => a.AwardKey)).Where(k => !keepKeys.Contains(k));
    }

    private async Task<int> CountAsync(AppDbContext ctx, string key, CancellationToken ct)
    {
        var keep = KeepIds(ctx);
        var names = KeepNames(ctx);
        var people = KeepPeopleIds(ctx);
        return key switch
        {
            "missions" => await ctx.MissionActivities.CountAsync(x => !names.Contains(x.UserName), ct) + await ctx.QuizAttempts.CountAsync(x => !names.Contains(x.UserName), ct),
            "attend" => await ctx.AttendanceChecks.CountAsync(a => !people.Contains(a.PersonId), ct),
            "gifts" => await GoneGifts(ctx).CountAsync(ct),
            "awards" => await ctx.TeamAwards.CountAsync(a => GoneAwardKeys(ctx).Contains(a.AwardKey), ct) + await ctx.TeamAwardArchives.CountAsync(a => GoneAwardKeys(ctx).Contains(a.AwardKey), ct),
            "teams" => await GoneTeams(ctx).CountAsync(ct),
            "reading" => await ctx.ReadingRounds.CountAsync(x => !names.Contains(x.UserName), ct),
            "copy" => await ctx.CopyVerses.CountAsync(x => !names.Contains(x.UserName), ct),
            "prayer" => await ctx.Prayer365Logs.CountAsync(x => !names.Contains(x.UserName), ct),
            "requests" => await ctx.PromotionRequests.CountAsync(x => !names.Contains(x.UserName), ct) + await ctx.SignupApplications.CountAsync(s => !keep.Contains(s.UserId), ct),
            "logs" => await ctx.SearchLogs.CountAsync(ct) + await ctx.AuditLogs.CountAsync(a => a.Action != Action, ct),
            "accounts" => await GoneUsers(ctx).CountAsync(ct),
            "members" => await MembersAsync(ctx, ct),
            "roster" => await GonePeople(ctx).CountAsync(ct),
            _ => 0,
        };
    }

    /// <summary>직접 가입한 회원 수(관리자 · 가짜 계정 빼고) — "members"를 고르면 지울 계정.</summary>
    private static async Task<int> MembersAsync(AppDbContext ctx, CancellationToken ct)
    {
        var admins = from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                     where r.Name == IdentityRoles.Admin || r.Name == IdentityRoles.SuperAdmin select ur.UserId;
        return await ctx.Users.CountAsync(u => !admins.Contains(u.Id) && (u.Email == null || !u.Email.EndsWith(TestToolsService.EmailDomain)), ct);
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
        _members = on.Contains("members");
        await using var ctx = systemDb.Create(actor);
        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        var keep = KeepIds(ctx);
        var detail = new List<string>();
        foreach (var (key, _, _) in Catalog.Where(c => on.Contains(c.Key) && c.Key != "members")) detail.Add($"{key}={await CountAsync(ctx, key, ct)}");
        var goneNames = GoneUsers(ctx).Select(u => u.UserName);

        var keepNames = KeepNames(ctx);
        var keepPeople = KeepPeopleIds(ctx);
        // 기록은 지울 사람(가짜 계정 · 지울 명부 줄 · 남길 사람이 없는 팀) 것만 — 관리자 · 직접 가입한 회원의 기록은 남긴다(2026-11-20 사용자 A)
        if (on.Contains("gifts"))
        {
            var gone = GoneGifts(ctx).Select(g => g.Id);
            await DeleteAsync(ctx, ctx.GiftLogs.Where(l => gone.Contains(l.GiftId)), ct);
            await DeleteAsync(ctx, ctx.GiftPointUses.Where(u => !keepNames.Contains(u.UserName) || gone.Contains(u.GiftId)), ct);
            await DeleteAsync(ctx, GoneGifts(ctx), ct);
            if (!await ctx.Gifts.AnyAsync(ct)) await DeleteAsync(ctx, ctx.GiftYearCloses, ct);   // 선물이 하나도 안 남으면 초기화 시각도(처음 상태와 똑같이 계산)
        }
        if (on.Contains("awards"))
        {
            var keys = await GoneAwardKeys(ctx).ToListAsync(ct);
            await DeleteAsync(ctx, ctx.TeamAwards.Where(a => keys.Contains(a.AwardKey)), ct);
            await DeleteAsync(ctx, ctx.TeamAwardArchives.Where(a => keys.Contains(a.AwardKey)), ct);
        }
        if (on.Contains("missions"))
        {
            await DeleteAsync(ctx, ctx.QuizAttempts.Where(x => !keepNames.Contains(x.UserName)), ct);
            await DeleteAsync(ctx, ctx.MissionActivities.Where(x => !keepNames.Contains(x.UserName)), ct);
        }
        if (on.Contains("attend")) await DeleteAsync(ctx, ctx.AttendanceChecks.Where(a => !keepPeople.Contains(a.PersonId)), ct);
        if (on.Contains("teams"))
        {
            var teams = await GoneTeams(ctx).Select(t => t.Id).ToListAsync(ct);
            await DeleteAsync(ctx, ctx.FamilyRequests.Where(r => teams.Contains(r.FamilyTeamId)), ct);
            await DeleteAsync(ctx, ctx.FamilyNotices.Where(n => teams.Contains(n.FamilyTeamId) || !keepNames.Contains(n.UserName)), ct);
            await DeleteAsync(ctx, ctx.FamilyMilestonesHidden.Where(h => teams.Contains(h.FamilyTeamId)), ct);
            await DeleteAsync(ctx, ctx.FamilyMembers.Where(m => teams.Contains(m.FamilyTeamId) || !keepNames.Contains(m.UserName)), ct);
            await DeleteAsync(ctx, ctx.FamilyTeams.Where(t => teams.Contains(t.Id)), ct);
        }
        if (on.Contains("reading"))
        {
            await DeleteAsync(ctx, ctx.ReadingVerses.Where(x => !keepNames.Contains(x.UserName)), ct);
            await DeleteAsync(ctx, ctx.ReadingChapterReads.Where(x => !keepNames.Contains(x.UserName)), ct);
            await DeleteAsync(ctx, ctx.ReadingChapterProgresses.Where(x => !keepNames.Contains(x.UserName)), ct);
            await DeleteAsync(ctx, ctx.ReadingBookmarks.Where(x => !keepNames.Contains(x.UserName)), ct);
            await DeleteAsync(ctx, ctx.ReadingRounds.Where(x => !keepNames.Contains(x.UserName)), ct);
        }
        if (on.Contains("copy")) await DeleteAsync(ctx, ctx.CopyVerses.Where(x => !keepNames.Contains(x.UserName)), ct);
        if (on.Contains("prayer"))
        {
            await DeleteAsync(ctx, ctx.Prayer365Logs.Where(x => !keepNames.Contains(x.UserName)), ct);
            if (!await ctx.Prayer365Logs.AnyAsync(ct)) await DeleteAsync(ctx, ctx.Prayer365Views, ct);   // 함께 본 수는 모두의 것 — 기도 기록이 하나도 안 남을 때만. 기도문(공용 콘텐츠)은 그대로
        }
        if (on.Contains("requests"))
        {
            await DeleteAsync(ctx, ctx.PromotionRequests.Where(x => !keepNames.Contains(x.UserName)), ct);
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
            await DeleteAsync(ctx, ctx.GuardianLinks.Where(l => !keepNames.Contains(l.GuardianUserName) || !keepNames.Contains(l.StudentUserName)), ct);
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
                await DeleteAsync(ctx, ctx.GuardianLinks.Where(l => !keepNames.Contains(l.GuardianUserName) || !keepNames.Contains(l.StudentUserName)), ct);   // 보호자 연결은 아이디로 잇는다 — 계정이 없으면 남을 수 없다
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
                    await ctx.Database.ExecuteSqlRawAsync(_members
                        ? $"DELETE FROM [Identity].[{t}] WHERE UserId NOT IN ({keepSql})"   // 관리자 빼고 모두(2026-11-21)
                        : $"DELETE FROM [Identity].[{t}] WHERE UserId NOT IN ({keepSql}) AND UserId IN (SELECT Id FROM [Identity].[User] WHERE Email LIKE N'%@fake-seed.test')", ct);   // 가짜 계정만
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
            "missions" => "미션", "attend" => "출석", "gifts" => "선물", "awards" => "시상", "teams" => "팀", "reading" => "통독", "copy" => "필사", "prayer" => "365축복기도",
            "requests" => "신청", "logs" => "로그", "audits" => "감사 로그", "accounts" => "계정", "members" => "회원", "people" or "roster" => "명부", _ => p[0],
        };
        return label + " " + (int.TryParse(p[1], out var n) ? n.ToString("N0") : p[1]);
    }));
}
