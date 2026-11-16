using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>케이스 하나: 번호 · 제목 · 가짜 데이터에 있는 상황 · 로그인할 사람 · 기대 결과.</summary>
public sealed record TestCase(int No, string Title, string Makes, string Who, string Expect);

/// <summary>케이스를 누른 결과: 들어갈 가짜 계정(없으면 지금 계정 그대로 = 플랫폼 할 일), 오늘을 주일로 볼지, 갈 곳.</summary>
public sealed record CaseRun(string? Error, string? LoginAs = null, bool PretendSunday = false, string Next = "/");

/// <summary>케이스 8: 그 케이스로 들어간 가짜 담임 쿠키에만 "오늘을 주일로 본다" 표시가 있다. 요청마다 쿠키에서 채운다.</summary>
public sealed class CaseFlags
{
    public bool PretendSunday { get; set; }
}

/// <summary>
/// 시험 도구 › 케이스별 시험 · 오늘 할 일(2026-11-16 사용자 "실제 명단에 가짜 생일 · 점수를 넣어 실제처럼").
/// [가짜 데이터 넣기]가 끝나면 SeedTodoAsync 가 부서마다 실제 명단 아이(가짜 계정 s05 · s06 · s07 · s09)에게 할 일 상황을 더한다:
/// 선물 고름 · 4일 밀림 · 구매됨 · 아직 안 고름, 담임 t01 반 아이 생일 선물, 새친구(데려온 사람 미정), 참여 신청 1건.
/// 케이스 버튼은 초등부의 그 상황을 처리할 가짜 계정(어드민 · t01 · p01)으로 가장 로그인할 뿐이다(8번만 이번 주일 출석을 비운다).
/// 지우기는 [가짜 데이터 지우기](fake-delete.sql)가 함께 한다 — 선물 열쇠 FAKE- · 가짜 표시 명부 · 가짜 계정. 관리자 계정은 지우지 않는다(CLAUDE.md).
/// </summary>
public sealed class CaseTestService(ISystemDb systemDb, TestToolsService tools, RuleService rules, TimeProvider time)
{
    public const string Mark = "[케이스]";
    public const string ConfirmWord = "CASE";
    /// <summary>gen-fake-seed.py 의 가짜 표시와 같다(지우기가 함께 지운다).</summary>
    public const string FakeMarker = "FAKE-SEED-20261114";

    public static readonly IReadOnlyList<TestCase> Cases =
    [
        new(1, "선물 사기", "s05 아이가 선물을 고름", "어드민", "[선물 처리로 이동] → 살 것에서 [샀어요]"),
        new(2, "선물 밀림", "s06 아이가 4일 전에 고름 · 아무도 안 삼", "어드민", "빨간 \"4일째\" 표시"),
        new(3, "선물 전하기", "s07 아이 선물 · 이미 구매됨", "어드민", "줄 것에서 [줬어요]"),
        new(4, "선물 대상", "s09 아이 · 아직 안 고름", "어드민", "고르는 중에서 보기"),
        new(5, "생일 선물", "t01 반 아이 생일 20일 뒤", "담임 t01", "생일 할 일 카드"),
        new(6, "데려온 사람 정하기", "새친구가 4번 와서 선물 대상", "어드민", "선물 처리에서 데려온 아이 고르기"),
        new(7, "참여 신청", "새 계정이 참여 신청", "어드민", "[사람 관리로 이동] → 승인"),
        new(8, "출석(주일)", "t01 반 이번 주일 출석 비움 · 오늘을 주일로", "담임 t01", "[출석으로 이동]"),
        new(9, "할 일 없음", "학부모 p01", "학부모 p01", "할 일 화면 없이 부서 홈"),
        new(10, "플랫폼 관리자", "공용 말씀 초안 하나", "지금 계정 그대로", "플랫폼 할 일 · 공개 전 초안"),
    ];

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
        if (no is < 1 or > 9) return new("없는 케이스예요.");
        if (await DeptAsync(ctx, ct) is not { } dept) return new("초등부가 없어요.");
        var slug = dept.Slug.ToLowerInvariant();
        async Task<bool> Has(string code) => await ctx.Users.AnyAsync(u => u.NormalizedUserName == AppUser.Normalize($"{slug}-{code}"), ct);
        if (!await Has("s05")) return new("먼저 위의 [가짜 데이터 넣기]를 해 주세요.");
        var who = no switch { 5 or 8 => "t01", 9 => "p01", _ => "admin" };
        if (!await Has(who)) return new(who == "admin" ? "초등부에 시험 관리자가 없어요. [관리자 없는 부서에 시험 관리자 만들기]를 먼저 눌러 주세요." : $"초등부에 {who} 계정이 없어요(명부가 적어요).");
        if (no == 8)
        {
            // 이번 주일 t01 반 아이들의 가짜 출석을 비운다(진짜 출석은 건드리지 않는다)
            var tz = await ctx.Tenants.Where(x => x.Id == dept.Id).Select(x => x.TimeZoneId).SingleAsync(ct);
            var sunday = AttendanceService.SundayOf(TenantClock.ToTenantDate(time.GetUtcNow(), tz));
            var kids = await KidsOfAsync(ctx, dept.Id, $"{slug}-t01", ct);
            ctx.AttendanceChecks.RemoveRange(await ctx.AttendanceChecks.Where(a => a.TenantId == dept.Id && a.Sunday == sunday && kids.Contains(a.PersonId) && a.CreatedByUserName == FakeMarker).ToListAsync(ct));
            await ctx.SaveChangesAsync(ct);
        }
        return new(null, $"{slug}-{who}", PretendSunday: no == 8);
    }

    /// <summary>이 담임이 맡은 정규 반의 학생 명부 줄.</summary>
    private static async Task<List<int>> KidsOfAsync(AppDbContext ctx, int t, string teacher, CancellationToken ct)
    {
        var uid = await ctx.Users.Where(u => u.NormalizedUserName == AppUser.Normalize(teacher)).Select(u => u.Id).FirstOrDefaultAsync(ct);
        var me = await ctx.People.Where(p => p.TenantId == t && p.UserId == uid).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
        if (me is null) return [];
        var classes = await (from m in ctx.ClassMembers join c in ctx.Classes on new { m.TenantId, Id = m.ClassId } equals new { c.TenantId, c.Id }
                             where m.TenantId == t && m.PersonId == me && m.IsTeacher && !c.IsNewcomer select m.ClassId).ToListAsync(ct);
        return await ctx.ClassMembers.Where(m => m.TenantId == t && classes.Contains(m.ClassId) && !m.IsTeacher).Select(m => m.PersonId).ToListAsync(ct);
    }

    /// <summary>
    /// [가짜 데이터 넣기] 뒤에 부서마다 할 일 상황을 더한다(다시 해도 같은 상태로). 선물 열쇠 FAKE- · 새친구 명부는 가짜 표시 · 신청자는 가짜 계정이라
    /// [가짜 데이터 지우기]가 함께 지운다. 만든 부서 수를 돌려준다.
    /// </summary>
    public async Task<int> SeedTodoAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!tools.Enabled || !await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException();
        var now = time.GetUtcNow().UtcDateTime;
        var n = 0;
        foreach (var t in await ctx.Tenants.Where(x => !x.IsLobby).Select(x => new { x.Id, x.Slug, x.TimeZoneId }).ToListAsync(ct))
        {
            var slug = t.Slug.ToLowerInvariant();
            var today = TenantClock.ToTenantDate(time.GetUtcNow(), t.TimeZoneId);
            var names = new[] { "s05", "s06", "s07", "s09", "t01", "admin" }.Select(c => AppUser.Normalize($"{slug}-{c}")).ToList();
            var users = await ctx.Users.Where(u => names.Contains(u.NormalizedUserName!)).Select(u => new { u.Id, u.UserName }).ToListAsync(ct);
            string? U(string c) => users.FirstOrDefault(u => u.UserName!.Equals($"{slug}-{c}", StringComparison.OrdinalIgnoreCase))?.UserName;
            if (U("s05") is null) continue;
            var people = await ctx.People.Where(p => p.TenantId == t.Id && p.UserId != null).Select(p => new { p.Id, p.UserId }).ToListAsync(ct);
            int? P(string c) => users.FirstOrDefault(u => u.UserName == U(c)) is { } x ? people.FirstOrDefault(p => p.UserId == x.Id)?.Id : null;

            // 다시 넣으면 처음 상태로: 지난번 할 일 선물 · 새친구 · 신청을 지운다
            var old = await ctx.Gifts.Where(g => g.TenantId == t.Id && g.SourceKey.StartsWith("FAKE-")).ToListAsync(ct);
            var oldIds = old.Select(g => g.Id).ToList();
            ctx.GiftLogs.RemoveRange(await ctx.GiftLogs.Where(l => l.TenantId == t.Id && oldIds.Contains(l.GiftId)).ToListAsync(ct));
            ctx.Gifts.RemoveRange(old);
            await ctx.SaveChangesAsync(ct);

            var buyer = U("admin");
            async Task Gift(string kid, string kind, string key, string state, DateTime? picked, DateTime? bought, string? item, int price, DateOnly? until = null)
            {
                if (U(kid) is not { } user) return;
                var g = new Gift
                {
                    TenantId = t.Id, Kind = kind, Year = (short)today.Year, UserName = user, PersonId = P(kid), SourceKey = key, State = state,
                    OpenFrom = today.AddDays(-5), OpenUntil = until ?? today.AddDays(14), MinAmount = price, MaxAmount = Math.Max(price, 10000),
                    ItemName = item, ItemPrice = item is null ? null : price, PickedUtc = picked, PurchasedUtc = bought, CreatedUtc = (picked ?? now).AddDays(-1),
                };
                ctx.Gifts.Add(g);
                await ctx.SaveChangesAsync(ct);
                ctx.GiftLogs.Add(new GiftLog { TenantId = t.Id, GiftId = g.Id, Action = GiftActions.Open, AtUtc = g.CreatedUtc });
                if (picked is { } pk) ctx.GiftLogs.Add(new GiftLog { TenantId = t.Id, GiftId = g.Id, Action = GiftActions.Pick, ActorUserName = user, AtUtc = pk, ItemName = item, ItemPrice = price });
                if (bought is { } bt) ctx.GiftLogs.Add(new GiftLog { TenantId = t.Id, GiftId = g.Id, Action = GiftActions.Purchase, ActorUserName = buyer, AtUtc = bt, ItemName = item, ItemPrice = price });
                await ctx.SaveChangesAsync(ct);
            }
            await Gift("s05", GiftKinds.Personal, "FAKE-1", GiftStates.Picked, now.AddHours(-2), null, "색연필 48색 세트", 9900);
            await Gift("s06", GiftKinds.Personal, "FAKE-2", GiftStates.Picked, now.AddDays(-4), null, "보드게임 할리갈리", 15000);
            await Gift("s07", GiftKinds.Personal, "FAKE-3", GiftStates.Purchased, now.AddDays(-3), now.AddDays(-1), "축구공 4호", 12000);
            await Gift("s09", GiftKinds.Personal, "FAKE-4", GiftStates.Open, null, null, null, 10000);

            // 담임 t01 반 아이 하나에게 생일 선물(생일 20일 뒤)
            var kids = await KidsOfAsync(ctx, t.Id, $"{slug}-t01", ct);
            var kidRow = await ctx.People.Where(p => p.TenantId == t.Id && kids.Contains(p.Id) && p.UserId != null).OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
            if (kidRow is not null)
            {
                var kidUser = await ctx.Users.Where(u => u.Id == kidRow.UserId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
                var b = today.AddDays(20);
                var key = BirthdayGiftService.SourceKey(b);   // 생일 열쇠 모양 그대로(할 일에 생일 날짜가 보이게). 가짜 계정 아이라 지우기가 함께 지운다
                var same = await ctx.Gifts.Where(g => g.TenantId == t.Id && g.Kind == GiftKinds.Birthday && g.PersonId == kidRow.Id && g.SourceKey == key).ToListAsync(ct);
                var sameIds = same.Select(g => g.Id).ToList();
                ctx.GiftLogs.RemoveRange(await ctx.GiftLogs.Where(l => l.TenantId == t.Id && sameIds.Contains(l.GiftId)).ToListAsync(ct));
                ctx.Gifts.RemoveRange(same);
                await ctx.SaveChangesAsync(ct);
                ctx.Gifts.Add(new Gift
                {
                    TenantId = t.Id, Kind = GiftKinds.Birthday, Year = (short)b.Year, UserName = kidUser, PersonId = kidRow.Id, SourceKey = key,
                    State = GiftStates.Open, OpenFrom = today, OpenUntil = b, MinAmount = 10000, MaxAmount = 10000, CreatedUtc = now,
                });
                await ctx.SaveChangesAsync(ct);
            }

            // 새친구: 데려왔다고 적었는데 명부에 없는 이름 · 출석 규칙 횟수를 이번 주일에 막 채움
            ctx.AttendanceChecks.RemoveRange(await ctx.AttendanceChecks.Where(a => a.TenantId == t.Id && a.CreatedByUserName == FakeMarker
                && ctx.People.Any(p => p.TenantId == t.Id && p.Id == a.PersonId && p.CreatedByUserName == FakeMarker && p.NewcomerFirstVisit != null)).ToListAsync(ct));
            ctx.People.RemoveRange(await ctx.People.Where(p => p.TenantId == t.Id && p.CreatedByUserName == FakeMarker && p.NewcomerFirstVisit != null).ToListAsync(ct));
            await ctx.SaveChangesAsync(ct);
            var visits = Math.Max(1, (await rules.ForDateAsync(today, ct)).Gifts.NewcomerVisits);
            var first = AttendanceService.SundayOf(today).AddDays(-7 * (visits - 1));
            var nc = new Person
            {
                TenantId = t.Id, Name = "한새봄", RoleCode = ClassService.NewcomerCode, NewcomerFirstVisit = first, NewcomerVoluntary = false,
                NewcomerEvangelist = "모르는 친구", CreatedUtc = now, CreatedByUserName = FakeMarker,
            };
            ctx.People.Add(nc);
            await ctx.SaveChangesAsync(ct);
            for (var s = first; s <= today; s = s.AddDays(7))
                ctx.AttendanceChecks.Add(new AttendanceCheck { TenantId = t.Id, PersonId = nc.Id, Sunday = s, CreatedUtc = now, CreatedByUserName = FakeMarker });

            // 참여 신청: 가짜 계정 하나(부서주소-a01)가 이 부서에 신청
            var appName = $"{slug}-a01";
            var app = await ctx.Users.SingleOrDefaultAsync(u => u.NormalizedUserName == AppUser.Normalize(appName), ct);
            if (app is null)
            {
                var email = appName + TestToolsService.EmailDomain;
                app = new AppUser
                {
                    UserName = appName, NormalizedUserName = AppUser.Normalize(appName), Email = email, NormalizedEmail = AppUser.Normalize(email),
                    FirstName = "윤다온", LastName = "", SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true, InsertDT = now,
                };
                app.PasswordHash = Hasher.HashPassword(app, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
                ctx.Users.Add(app);
                var basic = await ctx.IdentityRoleRows.Where(r => r.Name == IdentityRoles.Basic).Select(r => r.Id).FirstOrDefaultAsync(ct);
                if (basic is not null) ctx.UserRoles.Add(new AppUserRole { UserId = app.Id, RoleId = basic });
                await ctx.SaveChangesAsync(ct);
            }
            ctx.SignupApplications.RemoveRange(await ctx.SignupApplications.Where(a => a.UserId == app.Id).ToListAsync(ct));
            ctx.SignupApplications.Add(new SignupApplication
            {
                TenantId = t.Id, UserId = app.Id, UserName = appName, FullName = "윤다온", RoleKind = SignupRoleKind.Student, InsertDT = now.AddDays(-2),
            });
            await ctx.SaveChangesAsync(ct);
            n++;
        }
        return n;
    }

    private static readonly Microsoft.AspNetCore.Identity.PasswordHasher<AppUser> Hasher = new();

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

    /// <summary>[예전 케이스 데이터 지우기]: 예전 방식으로 만든 [케이스] 명부(계정 없는 줄) · 반 · 선물 · 출석 · 공용 초안. 계정 · 역할 · 관리자는 남긴다.</summary>
    public async Task<string> ClearAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!tools.Enabled || !await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException();
        var drafts = await ctx.DailyWords.Where(w => w.TenantId == null && w.Title.StartsWith(Mark) && w.Status != ContentStatus.Published).ToListAsync(ct);
        ctx.DailyWords.RemoveRange(drafts);
        int kids = 0, gifts = 0;
        foreach (var t in await ctx.Tenants.Where(x => !x.IsLobby).Select(x => x.Id).ToListAsync(ct))
        {
            var pids = await ctx.People.Where(p => p.TenantId == t && p.UserId == null && p.Name.StartsWith(Mark)).Select(p => p.Id).ToListAsync(ct);
            var gs = await ctx.Gifts.Where(g => g.TenantId == t && g.PersonId != null && pids.Contains(g.PersonId.Value)).ToListAsync(ct);
            var gids = gs.Select(g => g.Id).ToList();
            ctx.GiftLogs.RemoveRange(await ctx.GiftLogs.Where(l => l.TenantId == t && gids.Contains(l.GiftId)).ToListAsync(ct));
            ctx.Gifts.RemoveRange(gs);
            ctx.AttendanceChecks.RemoveRange(await ctx.AttendanceChecks.Where(a => a.TenantId == t && pids.Contains(a.PersonId)).ToListAsync(ct));
            var cls = await ctx.Classes.Where(c => c.TenantId == t && c.Name.StartsWith(Mark)).Select(c => c.Id).ToListAsync(ct);
            ctx.ClassMembers.RemoveRange(await ctx.ClassMembers.Where(m => m.TenantId == t && (pids.Contains(m.PersonId) || cls.Contains(m.ClassId))).ToListAsync(ct));
            ctx.SignupApplications.RemoveRange(await ctx.SignupApplications.Where(a => a.TenantId == t && a.UserName.Contains("-k-")).ToListAsync(ct));
            await ctx.SaveChangesAsync(ct);
            ctx.Classes.RemoveRange(await ctx.Classes.Where(c => c.TenantId == t && cls.Contains(c.Id)).ToListAsync(ct));
            ctx.People.RemoveRange(await ctx.People.Where(p => p.TenantId == t && pids.Contains(p.Id)).ToListAsync(ct));
            await ctx.SaveChangesAsync(ct);
            (kids, gifts) = (kids + pids.Count, gifts + gs.Count);
        }
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = "CaseDataDelete", Detail = "시험 도구 예전 케이스" });
        await ctx.SaveChangesAsync(ct);
        return $"예전 케이스 데이터를 지웠어요 · 아이 {kids} · 선물 {gifts} · 초안 {drafts.Count}";
    }
}
