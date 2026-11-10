using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record AccountHit(string Id, string Name, string Email, bool PlatformAdmin, bool TenantAdmin, IReadOnlyList<string> Tenants);
public sealed record AccountMembership(string Tenant, string ThemeColor, string Status, IReadOnlyList<string> Roles);
public sealed record AccountDetail(string Id, string Name, string Email, bool PlatformAdmin, DateTime CreatedUtc,
    IReadOnlyList<AccountMembership> Memberships, IReadOnlyList<string> AdminOf, IReadOnlyList<PlatformLog> Log);

/// <summary>
/// 플랫폼 · 계정 찾기(확정 시안: 플랫폼-계정찾기, 0035 · 0029 · 0050). 여러 부서 계정을 보므로 시스템 연결로만, 시작마다 플랫폼 관리자 확인.
/// 임시 비밀번호는 서버가 만들어 한 번만 돌려준다(원문은 저장하지 않음). 플랫폼 관리자 계정은 초기화하지 않는다.
/// 기록은 AuditLog 에 "account={id} …" 로 대상 계정을 적어 남긴다(이 계정 관련 기록에서 찾는다).
/// </summary>
public sealed partial class PlatformAccountService(ISystemDb systemDb, TimeProvider time)
{
    public const string ResetPasswordAction = "PasswordReset";
    public const string ResetMarksAction = "BibleMarksReset";
    public const string RenameAction = "UserRenamed";
    public const int MaxUserName = 50;
    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static readonly string[] Words = ["bless", "grace", "faith", "hope", "light", "peace", "love", "joy"];

    /// <summary>읽기 쉬운 임시 비밀번호: 낱말-숫자 4자리(예: grace-4821).</summary>
    public static string NewTemporaryPassword() => $"{Words[RandomNumberGenerator.GetInt32(Words.Length)]}-{RandomNumberGenerator.GetInt32(1000, 10000)}";

    /// <summary>계정에 임시 비밀번호를 걸고(다음 로그인 때 바꾸게) 원문을 돌려준다. 저장은 부르는 쪽이.</summary>
    public static string ApplyTemporaryPassword(AppUser a)
    {
        var temp = NewTemporaryPassword();
        a.PasswordHash = Hasher.HashPassword(a, temp);
        return temp;
    }

    /// <summary>임시 비밀번호를 건 계정은 다음 로그인에서 비밀번호를 바꾸게 한다(0035). 저장은 부르는 쪽이.</summary>
    public static async Task MarkMustChangeAsync(AppDbContext ctx, string userName, CancellationToken ct)
    {
        var s = await ctx.UserSettings.SingleOrDefaultAsync(x => x.UserName == userName, ct);
        if (s is null) ctx.UserSettings.Add(s = new UserSetting { UserName = userName });
        s.MustChangePassword = true;
    }

    /// <summary>계정의 성경 읽음 표시 · 책갈피를 모두 지운다(0029). 시스템 연결에서만. 저장은 부르는 쪽이.</summary>
    public static async Task<(int Marks, int Bookmarks)> ClearBibleMarksAsync(AppDbContext ctx, string userName, CancellationToken ct)
    {
        var marks = await ctx.ReadingVerses.IgnoreQueryFilters().Where(m => m.UserName == userName).ToListAsync(ct);
        var books = await ctx.ReadingBookmarks.IgnoreQueryFilters().Where(b => b.UserName == userName).ToListAsync(ct);
        ctx.ReadingVerses.RemoveRange(marks);
        ctx.ReadingBookmarks.RemoveRange(books);
        return (marks.Count, books.Count);
    }

    private async Task<AppDbContext> OpenAsync(string actor, CancellationToken ct)
    {
        var ctx = systemDb.Create(actor);
        var me = await ctx.Users.AsNoTracking().SingleOrDefaultAsync(a => a.UserName == actor, ct);
        if (me is null || me.IsDisabled(time.GetUtcNow()) || !await AccountService.IsPlatformAdminAsync(ctx, me.Id, ct))
        {
            await ctx.DisposeAsync();
            throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        }
        return ctx;
    }

    private static async Task<List<(string UserId, string Tenant, string Color, string Status, List<string> Roles, int TenantId)>> MembershipsAsync(
        AppDbContext ctx, IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        var admins = (await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                             where r.Name == IdentityRoles.Admin select ur.UserId).ToListAsync(ct)).ToHashSet();
        var rows = await (from d in ctx.DeptRoles
                          join t in ctx.Tenants on d.TenantId equals t.Id
                          where userIds.Contains(d.UserId!)
                          orderby t.Name
                          select new { d.UserId, t.Name, t.ThemeColor, d.RoleCode, d.TenantId }).ToListAsync(ct);
        return rows.Select(r => (r.UserId!, r.Name, r.ThemeColor, MembershipStatus.Approved, UserRole.ScreenRoles(r.RoleCode, admins.Contains(r.UserId!)).ToList(), r.TenantId)).ToList();
    }

    public async Task<List<AccountHit>> SearchAsync(string actor, string? q, CancellationToken ct = default)
    {
        var term = q?.Trim() ?? "";
        if (term.Length == 0) return [];
        await using var ctx = await OpenAsync(actor, ct);
        var lower = term.ToLowerInvariant();
        var accounts = (await ctx.Users.AsNoTracking().Where(a => a.Email!.Contains(lower) || a.UserName!.Contains(term) || (a.LastName! + a.FirstName!).Contains(term))
            .Take(100).ToListAsync(ct)).OrderBy(a => a.DisplayName).ThenBy(a => a.Email).Take(30).ToList();
        var ids = accounts.Select(a => a.Id).ToList();
        var mems = await MembershipsAsync(ctx, ids, ct);
        var platform = (await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                               where r.Name == IdentityRoles.SuperAdmin && ids.Contains(ur.UserId) select ur.UserId).ToListAsync(ct)).ToHashSet();
        return accounts.Select(a =>
        {
            var mine = mems.Where(m => m.UserId == a.Id).ToList();
            return new AccountHit(a.UserName!, a.DisplayName, a.Email ?? "", platform.Contains(a.Id),
                mine.Any(m => m.Roles.Contains(Roles.TenantAdmin)), mine.Select(m => m.Tenant).ToList());
        }).ToList();
    }

    public async Task<AccountDetail?> DetailAsync(string actor, string id, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserName == id, ct);
        if (a is null) return null;
        var mems = await MembershipsAsync(ctx, [a.Id], ct);

        // 이 계정이 한 일 + 이 계정에 한 일(Detail 에 "account={id} " 로 적힌 기록: 비밀번호 · 읽음 초기화, 진급)
        var marker = $"account={id} ";
        var logs = (await (from l in ctx.AuditLogs
                           join actorAcc in ctx.Users on l.ActorUserName equals actorAcc.UserName
                           where l.ActorUserName == id || (l.Detail != null && (l.Detail.StartsWith(marker) || l.Detail.Contains(" " + marker)))
                           orderby l.OccurredUtc descending, l.Id descending
                           select new { l.OccurredUtc, actorAcc.Id, actorAcc.LastName, actorAcc.FirstName, l.TenantId, l.Action, l.Detail }).Take(30).ToListAsync(ct))
            .Select(l => new { l.OccurredUtc, DisplayName = AppUser.FullName(l.LastName, l.FirstName), l.Id, l.TenantId, l.Action, l.Detail }).ToList();
        var platformIds = (await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id where r.Name == IdentityRoles.SuperAdmin select ur.UserId).ToListAsync(ct)).ToHashSet();
        var tenants = await ctx.Tenants.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return new AccountDetail(a.UserName!, a.DisplayName, a.Email ?? "", platformIds.Contains(a.Id), a.InsertDT,
            mems.Select(m => new AccountMembership(m.Tenant, m.Color, m.Status, m.Roles)).ToList(),
            mems.Where(m => m.Roles.Contains(Roles.TenantAdmin)).Select(m => m.Tenant).ToList(),
            logs.Select(l => new PlatformLog(l.OccurredUtc, platformIds.Contains(l.Id) ? $"{l.DisplayName}(운영자)" : l.DisplayName,
                l.TenantId is int t ? tenants.GetValueOrDefault(t) : null, PlatformHomeService.LogText(l.Action, l.Detail))).ToList());
    }

    // 이메일 앞부분으로 쓸 수 있는 글자만. @ 가 있으면 로그인이 이메일로 읽는다
    [GeneratedRegex(@"^[A-Za-z0-9._+-]{2,50}$")]
    private static partial Regex UserNameRule();

    /// <summary>
    /// 아이디 바꾸기(ADR 0090, 지시서 07): 플랫폼 관리자만, 이메일을 똑같이 써야 한다. 회원 표와 그 사람의 모든 기록을 한 트랜잭션으로 고친다.
    /// 아이디가 기본 키에 든 표(통독 · 퀴즈 시도 · 설정)는 새 줄을 넣고 옛 줄을 지운다. 잘못되면 이유, 성공하면 null.
    /// </summary>
    public async Task<string?> RenameAsync(string actor, string id, string? newName, string? confirmEmail, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Users.SingleOrDefaultAsync(x => x.UserName == id, ct);
        if (a is null) return "계정을 찾을 수 없어요.";
        if (AccountService.NormalizeEmail(confirmEmail ?? "") != AccountService.NormalizeEmail(a.Email ?? "")) return "이메일이 맞지 않아요. 똑같이 써 주세요.";
        var name = (newName ?? "").Trim();
        if (!UserNameRule().IsMatch(name)) return "아이디는 영문 · 숫자 · . _ - + 로 2 ~ 50자만 쓸 수 있어요.";
        if (name == id) return "지금 아이디와 같아요.";
        var normalized = AppUser.Normalize(name);
        if (await ctx.Users.AnyAsync(x => x.NormalizedUserName == normalized && x.Id != a.Id, ct)) return "이미 쓰는 아이디예요.";

        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        a.UserName = name;
        a.NormalizedUserName = normalized;
        a.ConcurrencyStamp = Guid.NewGuid().ToString();

        await MoveKeyedRowsAsync(ctx, id, name, ct);
        foreach (var x in await ctx.MissionActivities.Where(x => x.UserName == id).ToListAsync(ct)) x.UserName = name;
        foreach (var x in await ctx.FamilyMembers.Where(x => x.UserName == id).ToListAsync(ct)) x.UserName = name;
        foreach (var x in await ctx.FamilyNotices.Where(x => x.UserName == id).ToListAsync(ct)) x.UserName = name;
        foreach (var x in await ctx.FamilyTeams.Where(x => x.CreatorUserName == id).ToListAsync(ct)) x.CreatorUserName = name;
        foreach (var x in await ctx.FamilyRequests.Where(x => x.TargetUserName == id || x.FromUserName == id).ToListAsync(ct))
        {
            if (x.TargetUserName == id) x.TargetUserName = name;
            if (x.FromUserName == id) x.FromUserName = name;
        }
        foreach (var x in await ctx.SignupApplications.Where(x => x.UserName == id || x.DecidedBy == id).ToListAsync(ct))
        {
            if (x.UserName == id) x.UserName = name;
            if (x.DecidedBy == id) x.DecidedBy = name;
        }
        foreach (var x in await ctx.GuardianLinks.Where(x => x.GuardianUserName == id || x.StudentUserName == id || x.CreatedByUserName == id).ToListAsync(ct))
        {
            if (x.GuardianUserName == id) x.GuardianUserName = name;
            if (x.StudentUserName == id) x.StudentUserName = name;
            if (x.CreatedByUserName == id) x.CreatedByUserName = name;
        }
        foreach (var x in await ctx.PromotionRequests.Where(x => x.UserName == id || x.RequestedByUserName == id || x.DecidedByUserName == id).ToListAsync(ct))
        {
            if (x.UserName == id) x.UserName = name;
            if (x.RequestedByUserName == id) x.RequestedByUserName = name;
            if (x.DecidedByUserName == id) x.DecidedByUserName = name;
        }
        foreach (var x in await ctx.TenantSeasons.Where(x => x.ChangedByUserName == id).ToListAsync(ct)) x.ChangedByUserName = name;
        foreach (var x in await ctx.PlatformRuleSetVersions.Where(x => x.ChangedByUserName == id).ToListAsync(ct)) x.ChangedByUserName = name;
        foreach (var x in await ctx.DailyWords.Where(x => x.CreatedByUserName == id).ToListAsync(ct)) x.CreatedByUserName = name;
        foreach (var x in await ctx.DailyPrayers.Where(x => x.CreatedByUserName == id).ToListAsync(ct)) x.CreatedByUserName = name;
        foreach (var x in await ctx.DailyQuizzes.Where(x => x.CreatedByUserName == id).ToListAsync(ct)) x.CreatedByUserName = name;
        foreach (var x in await ctx.WeeklyVerses.Where(x => x.CreatedByUserName == id).ToListAsync(ct)) x.CreatedByUserName = name;

        // 감사 로그: 한 사람 칸과, "account=아이디" 로 적힌 대상. 앞부분만 같은 다른 아이디는 건드리지 않는다
        var marker = "account=" + id;
        var exact = new Regex(Regex.Escape(marker) + @"(?=\s|$)");
        foreach (var x in await ctx.AuditLogs.Where(x => x.ActorUserName == id || x.ImpersonatorUserName == id || (x.Detail != null && x.Detail.Contains(marker))).ToListAsync(ct))
        {
            if (x.ActorUserName == id) x.ActorUserName = name;
            if (x.ImpersonatorUserName == id) x.ImpersonatorUserName = name;
            if (x.Detail is not null) x.Detail = exact.Replace(x.Detail, "account=" + name);
        }
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor == id ? name : actor, Action = RenameAction, Detail = $"account={name} 이전 아이디 {id}" });
        await ctx.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return null;
    }

    private static async Task MoveKeyedRowsAsync(AppDbContext ctx, string id, string name, CancellationToken ct)
    {
        var rounds = await ctx.ReadingRounds.Where(x => x.UserName == id).ToListAsync(ct);
        ctx.ReadingRounds.AddRange(rounds.Select(x => new ReadingRound { UserName = name, RoundNo = x.RoundNo, StartedAt = x.StartedAt, CompletedAt = x.CompletedAt, ClosedAt = x.ClosedAt }));
        var verses = await ctx.ReadingVerses.Where(x => x.UserName == id).ToListAsync(ct);
        ctx.ReadingVerses.AddRange(verses.Select(x => new ReadingVerse { UserName = name, RoundNo = x.RoundNo, BookId = x.BookId, Chapter = x.Chapter, Verse = x.Verse, ReadAt = x.ReadAt }));
        var reads = await ctx.ReadingChapterReads.Where(x => x.UserName == id).ToListAsync(ct);
        ctx.ReadingChapterReads.AddRange(reads.Select(x => new ReadingChapterRead { UserName = name, RoundNo = x.RoundNo, BookId = x.BookId, Chapter = x.Chapter, ReadAt = x.ReadAt }));
        var progress = await ctx.ReadingChapterProgresses.Where(x => x.UserName == id).ToListAsync(ct);
        ctx.ReadingChapterProgresses.AddRange(progress.Select(x => new ReadingChapterProgress { UserName = name, RoundNo = x.RoundNo, BookId = x.BookId, Chapter = x.Chapter, LastViewedVerse = x.LastViewedVerse, UpdatedAt = x.UpdatedAt }));
        var marks = await ctx.ReadingBookmarks.Where(x => x.UserName == id).ToListAsync(ct);
        ctx.ReadingBookmarks.AddRange(marks.Select(x => new ReadingBookmark { UserName = name, BookId = x.BookId, Chapter = x.Chapter, Verse = x.Verse, CreatedAt = x.CreatedAt }));
        var quiz = await ctx.QuizAttempts.Where(x => x.UserName == id).ToListAsync(ct);
        ctx.QuizAttempts.AddRange(quiz.Select(x => new QuizAttempt { TenantId = x.TenantId, UserName = name, MissionDate = x.MissionDate, QuestionId = x.QuestionId, AttemptNo = x.AttemptNo, AnswerText = x.AnswerText, CorrectAnswerText = x.CorrectAnswerText, IsCorrect = x.IsCorrect, AttemptedAt = x.AttemptedAt }));
        var settings = await ctx.UserSettings.Where(x => x.UserName == id).ToListAsync(ct);
        ctx.UserSettings.AddRange(settings.Select(x => new UserSetting { UserName = name, MustChangePassword = x.MustChangePassword, PhotoPath = x.PhotoPath, LastTenantId = x.LastTenantId, ReadingTheme = x.ReadingTheme, MissionTheme = x.MissionTheme, FamilyInvitesSnoozedAt = x.FamilyInvitesSnoozedAt }));

        ctx.ReadingVerses.RemoveRange(verses);
        ctx.ReadingChapterReads.RemoveRange(reads);
        ctx.ReadingChapterProgresses.RemoveRange(progress);
        ctx.ReadingBookmarks.RemoveRange(marks);
        ctx.ReadingRounds.RemoveRange(rounds);
        ctx.QuizAttempts.RemoveRange(quiz);
        ctx.UserSettings.RemoveRange(settings);
    }

    /// <summary>임시 비밀번호를 만들고 다음 로그인 때 바꾸게 한다(0035). 플랫폼 관리자 계정은 null.</summary>
    public async Task<string?> ResetPasswordAsync(string actor, string id, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Users.SingleOrDefaultAsync(x => x.UserName == id, ct);
        if (a is null || await AccountService.IsPlatformAdminAsync(ctx, a.Id, ct)) return null;
        var temp = ApplyTemporaryPassword(a);
        await MarkMustChangeAsync(ctx, id, ct);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = ResetPasswordAction, Detail = $"account={id} {a.DisplayName}({a.Email})" });
        await ctx.SaveChangesAsync(ct);
        return temp;
    }

    /// <summary>성경 읽음 표시 · 책갈피를 모두 지운다(0029, 되돌릴 수 없음). 확인 이메일이 맞아야 한다. 지운 줄 수, 틀리면 null.</summary>
    public async Task<int?> ResetBibleMarksAsync(string actor, string id, string? confirmEmail, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Users.SingleOrDefaultAsync(x => x.UserName == id, ct);
        if (a is null || await AccountService.IsPlatformAdminAsync(ctx, a.Id, ct) || AccountService.NormalizeEmail(confirmEmail ?? "") != (a.Email ?? "")) return null;
        var (marks, books) = await ClearBibleMarksAsync(ctx, id, ct);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = ResetMarksAction,
            Detail = $"account={id} {a.DisplayName}({a.Email}) 읽음 {marks} · 책갈피 {books}" });
        await ctx.SaveChangesAsync(ct);
        return marks + books;
    }
}
