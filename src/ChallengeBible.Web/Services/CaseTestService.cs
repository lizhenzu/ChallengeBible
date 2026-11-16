using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>케이스 하나(확정 시안 케이스시험, 2026-11-16): 번호 · 제목 · 만드는 것 · 로그인할 사람 · 기대 결과.</summary>
public sealed record TestCase(int No, string Title, string Makes, string Who, string Expect);

/// <summary>케이스를 만든 결과: 들어갈 가짜 계정(없으면 지금 계정 그대로 = 플랫폼 할 일), 오늘을 주일로 볼지, 갈 곳.</summary>
public sealed record CaseRun(string? Error, string? LoginAs = null, bool PretendSunday = false, string Next = "/");

/// <summary>케이스 8(2026-11-16 사용자): 그 케이스로 들어간 가짜 담임 쿠키에만 "오늘을 주일로 본다" 표시가 있다. 요청마다 쿠키에서 채운다.</summary>
public sealed class CaseFlags
{
    public bool PretendSunday { get; set; }
}

/// <summary>
/// 시험 도구 › 케이스별 시험 · 오늘 할 일(확정 시안 케이스시험, 2026-11-16 사용자). 플랫폼 관리자만, 시험 도구가 켜져 있을 때만.
/// 초등부(이름에 "초등"이 든 첫 부서)에 케이스 데이터를 만들고 그 일을 할 가짜 계정으로 들어간다.
/// 만든 명부 · 반 · 초안에는 [케이스] 표시가 붙고, 계정은 아이디 부서주소-k-역할 · 이메일 @fake-seed.test.
/// 계정 비밀번호는 아무도 모르는 값이라 비밀번호로는 못 들어가고, 플랫폼 관리자의 가장(사람 바꾸기)으로만 들어간다(실제 아이 명단이 있는 부서라서).
/// 지우기는 [케이스] 데이터만 지우고 계정(관리자 포함)은 남긴다(CLAUDE.md: 관리자 계정은 지우지 않는다).
/// </summary>
public sealed class CaseTestService(ISystemDb systemDb, TestToolsService tools, RuleService rules, TimeProvider time)
{
    public const string Mark = "[케이스]";
    public const string ConfirmWord = "CASE";

    public static readonly IReadOnlyList<TestCase> Cases =
    [
        new(1, "🎁 선물 사기", "아이가 선물을 고름", "부장", "[구매 완료] 버튼이 보여요"),
        new(2, "🎁 선물 밀림", "4일 전에 골랐는데 아무도 안 삼", "관리자", "빨간 \"밀림\" 표시"),
        new(3, "🎁 선물 전하기", "이미 구매됨", "지도", "[전달 완료] 버튼이 보여요"),
        new(4, "🔔 선물 대상 알림", "선물 대상 · 아직 안 고름", "선물 담당", "[링크 복사] 버튼이 보여요"),
        new(5, "🎂 생일 링크", "맡은 반 아이 생일 20일 뒤", "담임", "생일 링크 보내기"),
        new(6, "🆕 데려온 사람 정하기", "새 친구가 옴", "서기", "데려온 사람 고르기"),
        new(7, "✋ 승인", "참여 신청 기다리는 중", "관리자", "승인 · 거절 열기"),
        new(8, "📋 출석(주일)", "우리 반 출석 안 함 · 오늘을 주일로 봄", "담임", "출석 체크 열기"),
        new(9, "🙂 할 일 없음", "아무 일도 없음", "일반 교사", "할 일 화면 없이 부서 홈"),
        new(10, "📌 플랫폼 관리자", "공용 말씀 초안 하나", "지금 계정 그대로(플랫폼 관리자)", "플랫폼 할 일 · 공개 전 초안"),
    ];

    /// <summary>케이스 계정: 코드 · 이름 · 직분 코드 · 관리자 권한 · 선물 담당 · 케이스 반 담임.</summary>
    private sealed record Acct(string Code, string Name, string Role, bool Admin = false, bool GiftTag = false, bool Homeroom = false);
    private static readonly Acct Officer = new("boo", "부장", "T1005");
    private static readonly Acct Admin = new("adm", "관리자", UserRole.TeacherCode, Admin: true);
    private static readonly Acct Director = new("dir", "지도", TodoRoles.DirectorCode);
    private static readonly Acct GiftMgr = new("gft", "선물담당", UserRole.TeacherCode, GiftTag: true);
    private static readonly Acct Homeroom = new("hr", "담임", UserRole.TeacherCode, Homeroom: true);
    private static readonly Acct Clerk = new("clk", "서기", "T1008");
    private static readonly Acct Teacher = new("tch", "교사", UserRole.TeacherCode);
    private static readonly Acct Applicant = new("app", "신청자", "");

    /// <summary>시험 대상 부서: 이름에 "초등"이 든 첫 운영 중 부서(2026-11-16 사용자 "초등부로 고정").</summary>
    public async Task<(int Id, string Name, string Slug)?> DeptAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        return await DeptAsync(ctx, ct);
    }

    private static async Task<(int Id, string Name, string Slug)?> DeptAsync(AppDbContext ctx, CancellationToken ct)
    {
        var t = await ctx.Tenants.AsNoTracking().Where(x => !x.IsLobby && x.Status == TenantStatus.Active && x.Name.Contains("초등"))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.Slug }).FirstOrDefaultAsync(ct);
        return t is null ? null : (t.Id, t.Name, t.Slug);
    }

    public async Task<CaseRun> RunAsync(string actor, int no, CancellationToken ct = default)
    {
        if (!tools.Enabled) return new("시험 도구가 꺼져 있어요.");
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) return new("플랫폼 관리자만 할 수 있어요.");
        if (no == 10) return await DraftAsync(ctx, actor, ct);
        if (await DeptAsync(ctx, ct) is not { } dept) return new("초등부가 없어요.");
        var t = dept.Id;
        var now = time.GetUtcNow().UtcDateTime;
        var tz = await ctx.Tenants.Where(x => x.Id == t).Select(x => x.TimeZoneId).SingleAsync(ct);
        var today = TenantClock.ToTenantDate(time.GetUtcNow(), tz);
        var slug = dept.Slug.ToLowerInvariant();

        switch (no)
        {
            case 1:
            {
                var u = await AccountAsync(ctx, t, slug, Officer, actor, now, ct);
                var kid = await KidAsync(ctx, t, "아이1", actor, now, ct);
                await GiftAsync(ctx, t, kid, GiftStates.Picked, now.AddHours(-2), null, null, today, now, ct);
                return new(null, u);
            }
            case 2:
            {
                var u = await AccountAsync(ctx, t, slug, Admin, actor, now, ct);
                var kid = await KidAsync(ctx, t, "아이2", actor, now, ct);
                await GiftAsync(ctx, t, kid, GiftStates.Picked, now.AddDays(-4), null, null, today, now, ct);
                return new(null, u);
            }
            case 3:
            {
                var u = await AccountAsync(ctx, t, slug, Director, actor, now, ct);
                var buyer = await AccountAsync(ctx, t, slug, Officer, actor, now, ct);
                var kid = await KidAsync(ctx, t, "아이3", actor, now, ct);
                await GiftAsync(ctx, t, kid, GiftStates.Purchased, now.AddDays(-2), now.AddDays(-1), buyer, today, now, ct);
                return new(null, u);
            }
            case 4:
            {
                var u = await AccountAsync(ctx, t, slug, GiftMgr, actor, now, ct);
                var kid = await KidAsync(ctx, t, "아이4", actor, now, ct);
                await GiftAsync(ctx, t, kid, GiftStates.Open, null, null, null, today, now, ct);
                return new(null, u);
            }
            case 5:
            {
                var u = await AccountAsync(ctx, t, slug, Homeroom, actor, now, ct);
                var b = today.AddDays(20);
                var kid = await KidAsync(ctx, t, "생일아이", actor, now, ct, inClass: true, birthday: b);
                await ResetKidAsync(ctx, t, kid, ct);
                var g = new Gift
                {
                    TenantId = t, Kind = GiftKinds.Birthday, Year = (short)b.Year, PersonId = kid, SourceKey = BirthdayGiftService.SourceKey(b), State = GiftStates.Open,
                    OpenFrom = today, OpenUntil = b, MinAmount = 10000, MaxAmount = 10000, CreatedUtc = now,
                };
                ctx.Gifts.Add(g);
                await ctx.SaveChangesAsync(ct);
                return new(null, u);
            }
            case 6:
            {
                var u = await AccountAsync(ctx, t, slug, Clerk, actor, now, ct);
                var kid = await KidAsync(ctx, t, "새친구", actor, now, ct);
                await ResetKidAsync(ctx, t, kid, ct);
                var p = await ctx.People.SingleAsync(x => x.TenantId == t && x.Id == kid, ct);
                // 새친구 출석 규칙 횟수를 이번 주일에 막 채웠다(마지막 출석이 이번 주일이라 새해 정리 뒤에도 센다)
                var visits = Math.Max(1, (await rules.ForDateAsync(today, ct)).Gifts.NewcomerVisits);
                var first = AttendanceService.SundayOf(today).AddDays(-7 * (visits - 1));
                (p.RoleCode, p.NewcomerFirstVisit, p.NewcomerVoluntary, p.NewcomerEvangelist) = (ClassService.NewcomerCode, first, false, $"{Mark} 모르는 친구");
                for (var s = first; s <= today; s = s.AddDays(7))
                    ctx.AttendanceChecks.Add(new AttendanceCheck { TenantId = t, PersonId = kid, Sunday = s, CreatedUtc = now, CreatedByUserName = actor });
                await ctx.SaveChangesAsync(ct);
                return new(null, u);
            }
            case 7:
            {
                var u = await AccountAsync(ctx, t, slug, Admin, actor, now, ct);
                var app = await AccountAsync(ctx, t, slug, Applicant, actor, now, ct);
                var a = await ctx.Users.SingleAsync(x => x.UserName == app, ct);
                ctx.SignupApplications.RemoveRange(await ctx.SignupApplications.Where(x => x.UserId == a.Id).ToListAsync(ct));
                ctx.SignupApplications.Add(new SignupApplication
                {
                    TenantId = t, UserId = a.Id, UserName = app, FullName = AppUser.FullName(a.LastName, a.FirstName), RoleKind = SignupRoleKind.Student, InsertDT = now,
                });
                await ctx.SaveChangesAsync(ct);
                return new(null, u);
            }
            case 8:
            {
                var u = await AccountAsync(ctx, t, slug, Homeroom, actor, now, ct);
                var kid = await KidAsync(ctx, t, "반아이", actor, now, ct, inClass: true);
                ctx.AttendanceChecks.RemoveRange(await ctx.AttendanceChecks.Where(x => x.TenantId == t && x.PersonId == kid).ToListAsync(ct));
                await ctx.SaveChangesAsync(ct);
                return new(null, u, PretendSunday: true);
            }
            case 9:
                return new(null, await AccountAsync(ctx, t, slug, Teacher, actor, now, ct));
            default:
                return new("없는 케이스예요.");
        }
    }

    /// <summary>케이스 10: 공용 말씀 초안 하나(60일 뒤부터 빈 날). 지금 계정 그대로 플랫폼 할 일로.</summary>
    private async Task<CaseRun> DraftAsync(AppDbContext ctx, string actor, CancellationToken ct)
    {
        var today = TenantClock.ToTenantDate(time.GetUtcNow(), PlatformHomeService.PlatformTimeZone);
        var mine = await ctx.DailyWords.AnyAsync(w => w.TenantId == null && w.IsCurrent && w.Title.StartsWith(Mark) && w.Status == ContentStatus.Draft && w.ExposeDate >= today, ct);
        if (!mine)
        {
            var used = (await ctx.DailyWords.Where(w => w.TenantId == null && w.IsCurrent && w.ExposeDate >= today.AddDays(60)).Select(w => w.ExposeDate).ToListAsync(ct)).ToHashSet();
            var d = today.AddDays(60);
            while (used.Contains(d)) d = d.AddDays(1);
            ctx.DailyWords.Add(new DailyWord
            {
                TenantId = null, Title = $"{Mark} 시험 초안", Summary = "케이스 시험으로 만든 초안이에요.", ExposeDate = d, Status = ContentStatus.Draft,
                BookId = 1, StartChapter = 1, StartVerse = 1, EndChapter = 1, EndVerse = 1, CreatedUtc = time.GetUtcNow().UtcDateTime, CreatedByUserName = actor,
            });
            await ctx.SaveChangesAsync(ct);
        }
        return new(null, Next: "/platform/todo");
    }

    /// <summary>[케이스 데이터 지우기]: [케이스] 명부(계정 없는 줄) · 반 · 선물 · 출석 · 참여 신청 · 공용 초안만. 계정 · 직분 · 관리자는 남긴다.</summary>
    public async Task<string> ClearAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!tools.Enabled || !await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException();
        var drafts = await ctx.DailyWords.Where(w => w.TenantId == null && w.Title.StartsWith(Mark) && w.Status != ContentStatus.Published).ToListAsync(ct);
        ctx.DailyWords.RemoveRange(drafts);
        int kids = 0, gifts = 0, signups = 0;
        if (await DeptAsync(ctx, ct) is { } dept)
        {
            var t = dept.Id;
            var pids = await ctx.People.Where(p => p.TenantId == t && p.UserId == null && p.Name.StartsWith(Mark)).Select(p => p.Id).ToListAsync(ct);
            var gs = await ctx.Gifts.Where(g => g.TenantId == t && g.PersonId != null && pids.Contains(g.PersonId.Value)).ToListAsync(ct);
            var gids = gs.Select(g => g.Id).ToList();
            ctx.GiftLogs.RemoveRange(await ctx.GiftLogs.Where(l => l.TenantId == t && gids.Contains(l.GiftId)).ToListAsync(ct));
            ctx.Gifts.RemoveRange(gs);
            ctx.AttendanceChecks.RemoveRange(await ctx.AttendanceChecks.Where(a => a.TenantId == t && pids.Contains(a.PersonId)).ToListAsync(ct));
            var cls = await ctx.Classes.Where(c => c.TenantId == t && c.Name.StartsWith(Mark)).Select(c => c.Id).ToListAsync(ct);
            ctx.ClassMembers.RemoveRange(await ctx.ClassMembers.Where(m => m.TenantId == t && (pids.Contains(m.PersonId) || cls.Contains(m.ClassId))).ToListAsync(ct));
            var prefix = $"{dept.Slug.ToLowerInvariant()}-k-";
            var sa = await ctx.SignupApplications.Where(a => a.TenantId == t && a.UserName.StartsWith(prefix)).ToListAsync(ct);
            ctx.SignupApplications.RemoveRange(sa);
            await ctx.SaveChangesAsync(ct);
            ctx.Classes.RemoveRange(await ctx.Classes.Where(c => c.TenantId == t && cls.Contains(c.Id)).ToListAsync(ct));
            ctx.People.RemoveRange(await ctx.People.Where(p => p.TenantId == t && pids.Contains(p.Id)).ToListAsync(ct));
            ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = "CaseDataDelete", TenantId = t, Detail = "시험 도구 케이스" });
            await ctx.SaveChangesAsync(ct);
            (kids, gifts, signups) = (pids.Count, gs.Count, sa.Count);
        }
        else await ctx.SaveChangesAsync(ct);
        return $"케이스 데이터를 지웠어요 · 아이 {kids} · 선물 {gifts} · 신청 {signups} · 초안 {drafts.Count}";
    }

    // ── 만들기 도우미 ──

    private static readonly Microsoft.AspNetCore.Identity.PasswordHasher<AppUser> Hasher = new();

    /// <summary>케이스 계정을 만들거나 고친다(직분 · 관리자 · 선물 담당 · 담임 반). 비밀번호는 아무도 모르는 값. 아이디를 돌려준다.</summary>
    private static async Task<string> AccountAsync(AppDbContext ctx, int t, string slug, Acct a, string actor, DateTime now, CancellationToken ct)
    {
        var userName = $"{slug}-k-{a.Code}";
        var user = await ctx.Users.SingleOrDefaultAsync(u => u.NormalizedUserName == AppUser.Normalize(userName), ct);
        if (user is null)
        {
            var email = userName + TestToolsService.EmailDomain;
            user = new AppUser
            {
                UserName = userName, NormalizedUserName = AppUser.Normalize(userName), Email = email, NormalizedEmail = AppUser.Normalize(email),
                FirstName = $"{Mark} {a.Name}", LastName = "", SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString(),
                LockoutEnabled = true, InsertDT = now,
            };
            user.PasswordHash = Hasher.HashPassword(user, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
            ctx.Users.Add(user);
            var basic = await ctx.IdentityRoleRows.Where(r => r.Name == IdentityRoles.Basic).Select(r => r.Id).FirstOrDefaultAsync(ct);
            if (basic is not null) ctx.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = basic });
            await ctx.SaveChangesAsync(ct);
        }
        if (a == Applicant) return userName;   // 신청자는 아직 부서에 들지 않았다
        if (!await ctx.DeptRoles.AnyAsync(d => d.UserId == user.Id && d.TenantId == t && d.RoleCode == a.Role, ct))
            await DeptMembership.PlaceAsync(ctx, user, t, a.Role, now, ct, actor: actor);
        if (a.Admin)
        {
            var adminRole = await ctx.IdentityRoleRows.FirstOrDefaultAsync(r => r.Name == IdentityRoles.Admin, ct);
            if (adminRole is null)
            {
                ctx.IdentityRoleRows.Add(adminRole = new AppRole { Name = IdentityRoles.Admin, NormalizedName = IdentityRoles.Admin.ToUpperInvariant() });
                await ctx.SaveChangesAsync(ct);
            }
            if (!await ctx.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == adminRole.Id, ct)) ctx.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = adminRole.Id });
        }
        if (a.GiftTag && !await ctx.MemberTags.AnyAsync(m => m.TenantId == t && m.UserId == user.Id && m.Tag == TodoRoles.GiftTag, ct))
            ctx.MemberTags.Add(new MemberTag { TenantId = t, UserId = user.Id, Tag = TodoRoles.GiftTag, CreatedUtc = now, CreatedByUserName = actor });
        await ctx.SaveChangesAsync(ct);
        if (a.Homeroom)
        {
            var me = await ctx.People.Where(p => p.TenantId == t && p.UserId == user.Id).Select(p => p.Id).SingleAsync(ct);
            var (term, cls) = await ClassAsync(ctx, t, now, ct);
            if (!await ctx.ClassMembers.AnyAsync(m => m.TenantId == t && m.PersonId == me && m.ClassId == cls, ct))
            {
                ctx.ClassMembers.RemoveRange(await ctx.ClassMembers.Where(m => m.TenantId == t && m.PersonId == me && m.TermId == term).ToListAsync(ct));
                ctx.ClassMembers.Add(new ClassMember { TenantId = t, TermId = term, ClassId = cls, PersonId = me, IsTeacher = true, CreatedUtc = now });
                await ctx.SaveChangesAsync(ct);
            }
        }
        return userName;
    }

    /// <summary>[케이스] 반(지금 반 묶음 안). 반 묶음이 없으면 올해 것을 만든다.</summary>
    private static async Task<(int Term, int Class)> ClassAsync(AppDbContext ctx, int t, DateTime now, CancellationToken ct)
    {
        var term = await ctx.ClassTerms.Where(x => x.TenantId == t).OrderByDescending(x => x.StartDate).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (term is null)
        {
            var nt = new ClassTerm { TenantId = t, Year = now.Year, StartDate = new DateOnly(now.Year, 1, 1), CreatedUtc = now };
            ctx.ClassTerms.Add(nt);
            await ctx.SaveChangesAsync(ct);
            term = nt.Id;
        }
        var name = $"{Mark} 반";
        var cls = await ctx.Classes.Where(c => c.TenantId == t && c.TermId == term && c.Name == name).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
        if (cls is null)
        {
            var c = new SchoolClass { TenantId = t, TermId = term.Value, Name = name, CreatedUtc = now };
            ctx.Classes.Add(c);
            await ctx.SaveChangesAsync(ct);
            cls = c.Id;
        }
        return (term.Value, cls.Value);
    }

    /// <summary>[케이스] 아이(계정 없는 학생 명부 줄). inClass 면 [케이스] 반에 넣는다.</summary>
    private static async Task<int> KidAsync(AppDbContext ctx, int t, string name, string actor, DateTime now, CancellationToken ct, bool inClass = false, DateOnly? birthday = null)
    {
        var full = $"{Mark} {name}";
        var p = await ctx.People.SingleOrDefaultAsync(x => x.TenantId == t && x.UserId == null && x.Name == full, ct);
        if (p is null)
        {
            p = new Person { TenantId = t, Name = full, RoleCode = UserRole.StudentCode, CreatedUtc = now, CreatedByUserName = actor };
            ctx.People.Add(p);
        }
        (p.BirthMonth, p.BirthDay) = birthday is { } b ? ((byte?)b.Month, (byte?)b.Day) : (null, null);
        await ctx.SaveChangesAsync(ct);
        if (inClass)
        {
            var (term, cls) = await ClassAsync(ctx, t, now, ct);
            if (!await ctx.ClassMembers.AnyAsync(m => m.TenantId == t && m.PersonId == p.Id && m.TermId == term, ct))
            {
                ctx.ClassMembers.Add(new ClassMember { TenantId = t, TermId = term, ClassId = cls, PersonId = p.Id, IsTeacher = false, CreatedUtc = now });
                await ctx.SaveChangesAsync(ct);
            }
        }
        return p.Id;
    }

    /// <summary>이 아이의 선물 · 출석을 비운다(케이스를 다시 눌러도 처음 상태로).</summary>
    private static async Task ResetKidAsync(AppDbContext ctx, int t, int kid, CancellationToken ct)
    {
        var gs = await ctx.Gifts.Where(g => g.TenantId == t && g.PersonId == kid).ToListAsync(ct);
        var ids = gs.Select(g => g.Id).ToList();
        ctx.GiftLogs.RemoveRange(await ctx.GiftLogs.Where(l => l.TenantId == t && ids.Contains(l.GiftId)).ToListAsync(ct));
        ctx.Gifts.RemoveRange(gs);
        ctx.AttendanceChecks.RemoveRange(await ctx.AttendanceChecks.Where(a => a.TenantId == t && a.PersonId == kid).ToListAsync(ct));
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>케이스 선물 한 건(생일 선물 장부, 기한 20일 뒤). 고름 · 구매 시각과 산 사람을 정한다.</summary>
    private static async Task GiftAsync(AppDbContext ctx, int t, int kid, string state, DateTime? picked, DateTime? purchased, string? buyer, DateOnly today, DateTime now, CancellationToken ct)
    {
        await ResetKidAsync(ctx, t, kid, ct);
        var key = today.AddDays(20);
        var g = new Gift
        {
            TenantId = t, Kind = GiftKinds.Birthday, Year = (short)key.Year, PersonId = kid, SourceKey = BirthdayGiftService.SourceKey(key), State = state,
            OpenFrom = today, OpenUntil = key, MinAmount = 10000, MaxAmount = 10000, CreatedUtc = now, PickedUtc = picked, PurchasedUtc = purchased,
            ItemName = state == GiftStates.Open ? null : $"{Mark} 색연필 세트", ItemPrice = state == GiftStates.Open ? null : 9900,
        };
        ctx.Gifts.Add(g);
        await ctx.SaveChangesAsync(ct);
        ctx.GiftLogs.Add(new GiftLog { TenantId = t, GiftId = g.Id, Action = GiftActions.Open, AtUtc = now });
        if (picked is { } p) ctx.GiftLogs.Add(new GiftLog { TenantId = t, GiftId = g.Id, Action = GiftActions.Pick, AtUtc = p, ItemName = g.ItemName, ItemPrice = g.ItemPrice });
        if (purchased is { } b) ctx.GiftLogs.Add(new GiftLog { TenantId = t, GiftId = g.Id, Action = GiftActions.Purchase, ActorUserName = buyer, AtUtc = b, ItemName = g.ItemName, ItemPrice = g.ItemPrice });
        await ctx.SaveChangesAsync(ct);
    }
}
