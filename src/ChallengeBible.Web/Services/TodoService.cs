using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>할 일 종류(ADR 0100). "누가 보나" 규칙의 줄 단위.</summary>
public static class TodoKinds
{
    public const string Gift = "gift", Notice = "notice", Birthday = "birthday", Newcomer = "newcomer", Approve = "approve", Attendance = "attendance";
    public static readonly string[] All = [Gift, Notice, Birthday, Newcomer, Approve, Attendance];
    public static string Label(string k) => k switch
    {
        Gift => "🎁 선물 사기 · 전하기", Notice => "🔔 선물 대상 알림 · 링크", Birthday => "🎂 생일 링크 보내기",
        Newcomer => "🆕 데려온 사람 정하기", Approve => "✋ 승인 · 🎓 진급", Attendance => "📋 출석(주일)", _ => k,
    };
}

/// <summary>할 일을 볼 사람 묶음(ADR 0100): 관리자 · 부장 부감 서기 · 지도 · 선물 담당(업무 꼬리표) · 담임(일반 반 선생님).</summary>
public static class TodoRoles
{
    public const string Admin = "admin", Officer = "officer", Director = "director", Gift = "gift", Homeroom = "homeroom";
    public static readonly string[] All = [Admin, Officer, Director, Gift, Homeroom];
    /// <summary>부장 · 부감 · 서기(2026-11-16 사용자 "임원(부장 + 부감 + 서기)").</summary>
    public static readonly string[] OfficerCodes = ["T1005", "T1006", "T1008"];
    public const string DirectorCode = "T1004";
    public const string GiftTag = "Gift";
    public static string Label(string r) => r switch { Admin => "관리자", Officer => "부장 · 부감 · 서기", Director => "지도", Gift => "선물 담당", Homeroom => "담임", _ => r };

    /// <summary>공통 규칙의 기본값(확정 시안 오늘할일 ④): 줄이 없으면 이 값.</summary>
    public static IReadOnlyList<string> Default(string kind) => kind switch
    {
        TodoKinds.Gift or TodoKinds.Notice or TodoKinds.Newcomer => [Admin, Officer, Director, Gift],
        TodoKinds.Birthday => [Homeroom],
        TodoKinds.Approve => [Admin],
        TodoKinds.Attendance => [Admin, Homeroom],
        _ => [],
    };
}

/// <summary>할 일 카드 하나. Href 가 있으면 화면을 여는 일, 버튼(Buy · Deliver · Link)이 있으면 그 자리에서 끝내는 일.</summary>
public sealed record TodoItem(string Kind, string Section, string Icon, string Title, string? Text)
{
    public string? Note { get; init; }
    /// <summary>"late"(3일 넘게 아무도 처리 안 함) · "done"(누가 처리함, 다음 단계) · "".</summary>
    public string Tone { get; init; } = "";
    public long? GiftId { get; init; }
    public string? Coupang { get; init; }
    public bool CanBuy { get; init; }
    public bool CanDeliver { get; init; }
    public bool CanLink { get; init; }
    public string? Href { get; init; }
    public string? HrefLabel { get; init; }
}

public sealed record TodoBoard(string Name, IReadOnlyList<TodoItem> Items, DateOnly? SnoozedUntil);

public enum TodoError { None, NotFound, NotYours, BadState }

/// <summary>
/// 오늘 할 일(ADR 0100, 확정 시안 오늘할일): 이 부서에서 내가 볼 수 있는 할 일을 모아, 그 자리에서 버튼 하나로 끝내게 한다.
/// 누가 무엇을 보나 = 부서 규칙(있으면) → 공통 규칙(플랫폼) → 코드 기본값. 선물은 그룹 누구나 사고(구매자 이름 표시) 누구나 전하고, [전달 완료]면 사라진다.
/// 출석은 주일 당일만, 생일 링크는 담임 · 생일 30일 안(선물이 열린 동안) · 아이가 고르면 사라진다. 기존 메뉴는 그대로 두고 같은 데이터를 쓴다.
/// </summary>
public sealed class TodoService(IAppDb db, RequestScope scope, TenantContext tenant, TenantClock clock, TimeProvider time,
    GiftOrderService orders, BirthdayGiftService birthdays, NewcomerGiftService newcomers, ReadingGiftService readings, CopyGiftService copies,
    TeamGiftService teamGifts, AttendanceService attendance)
{
    public const int LateDays = 3;
    private string Me => scope.UserName ?? throw new InvalidOperationException("로그인한 요청에서만 써요.");
    private string Slug => tenant.Current.Slug;
    public DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    /// <summary>이 부서의 규칙: 부서 줄이 있으면 부서 것(Custom = true), 없으면 공통 줄 → 기본값.</summary>
    public async Task<(bool Custom, IReadOnlyDictionary<string, IReadOnlyList<string>> Rules)> RulesAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var rows = await ctx.TodoRules.AsNoTracking().Select(r => new { r.TenantId, r.Kind, r.Roles }).ToListAsync(ct);
        var mine = rows.Where(r => r.TenantId == tenant.Current.Id).ToList();
        var common = rows.Where(r => r.TenantId == null).ToList();
        var src = mine.Count > 0 ? mine : common;
        return (mine.Count > 0, TodoKinds.All.ToDictionary(k => k, k => src.FirstOrDefault(r => r.Kind == k) is { } r ? Split(r.Roles) : TodoRoles.Default(k)));
    }

    public static IReadOnlyList<string> Split(string roles) => roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(TodoRoles.All.Contains).ToList();

    /// <summary>나의 묶음: 관리자 · 부장 부감 서기 · 지도 · 선물 담당 · 담임(맡은 일반 반이 있으면).</summary>
    private async Task<(HashSet<string> Roles, AttendScope Scope)> MineAsync(AppDbContext ctx, CancellationToken ct)
    {
        var roles = new HashSet<string>();
        if (tenant.IsAdmin) roles.Add(TodoRoles.Admin);
        var code = (tenant.RoleCode ?? "").Trim();
        if (TodoRoles.OfficerCodes.Contains(code)) roles.Add(TodoRoles.Officer);
        if (code == TodoRoles.DirectorCode) roles.Add(TodoRoles.Director);
        var uid = await ctx.Users.Where(u => u.UserName == Me).Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (uid is not null && await ctx.MemberTags.AnyAsync(t => t.UserId == uid && t.Tag == TodoRoles.GiftTag, ct)) roles.Add(TodoRoles.Gift);
        var sc = await attendance.ScopeAsync(Me, ct);
        if (sc.Classes.Count > 0) roles.Add(TodoRoles.Homeroom);
        return (roles, sc);
    }

    /// <summary>내가 볼 수 있는 할 일 종류.</summary>
    public async Task<HashSet<string>> VisibleKindsAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby) return [];
        var (_, rules) = await RulesAsync(ct);
        await using var ctx = db.Create();
        var (roles, _) = await MineAsync(ctx, ct);
        return rules.Where(r => r.Value.Any(roles.Contains)).Select(r => r.Key).ToHashSet();
    }

    public async Task<TodoBoard> BoardAsync(CancellationToken ct = default)
    {
        var items = new List<TodoItem>();
        if (tenant.IsLobby) return new("", items, null);
        var (_, rules) = await RulesAsync(ct);
        await using var ctx = db.Create();
        var (roles, sc) = await MineAsync(ctx, ct);
        bool See(string kind) => rules[kind].Any(roles.Contains);
        var name = await ctx.Users.Where(u => u.UserName == Me).Select(u => AppUser.FullName(u.LastName, u.FirstName)).FirstOrDefaultAsync(ct) ?? Me;
        var snooze = await ctx.UserSettings.Where(s => s.UserName == Me).Select(s => s.TodoSnoozedUntil).FirstOrDefaultAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var today = Today;

        if (See(TodoKinds.Gift) || See(TodoKinds.Notice) || See(TodoKinds.Birthday))
        {
            // 선물 생김 · 기한 지남(관리 › 선물과 같다)
            await teamGifts.EnsureAsync(ct);
            await birthdays.EnsureAsync(ct);
            await newcomers.EnsureAsync(ct);
            await readings.EnsureAsync(ct);
            await copies.EnsureAsync(ct);
        }
        if (See(TodoKinds.Gift))
        {
            var buy = await orders.ListAsync(GiftOrderService.Buy, 100, ct);
            var give = await orders.ListAsync(GiftOrderService.Give, 100, ct);
            var ids = give.Select(g => g.Id).ToList();
            var buyers = await (from l in ctx.GiftLogs.AsNoTracking() where ids.Contains(l.GiftId) && l.Action == GiftActions.Purchase
                                select new { l.GiftId, l.ActorUserName, l.AtUtc }).ToListAsync(ct);
            var names = await NamesAsync(ctx, buyers.Select(b => b.ActorUserName).OfType<string>(), ct);
            foreach (var g in buy)
            {
                var days = g.PickedUtc is { } p ? (int)(now - p).TotalDays : 0;
                items.Add(new(TodoKinds.Gift, "gift", "🎁", $"사 주세요 · {g.Who} · {g.ItemName}", $"{g.KindText} · {Local(g.PickedUtc):M/d}에 고름 · {g.Price:N0}원")
                { GiftId = g.Id, Coupang = g.CoupangUrl, CanBuy = true, Tone = days >= LateDays ? "late" : "", Note = days >= LateDays ? $"{days}일째 아무도 안 삼" : null });
            }
            foreach (var g in give)
            {
                var b = buyers.Where(x => x.GiftId == g.Id).OrderByDescending(x => x.AtUtc).FirstOrDefault();
                var by = b?.ActorUserName is { } u ? names.GetValueOrDefault(u, u) : "누군가";
                items.Add(new(TodoKinds.Gift, "gift", "🎁", $"전해 주세요 · {g.Who} · {g.ItemName}", g.KindText)
                { GiftId = g.Id, CanDeliver = true, Tone = "done", Note = $"{by}님이 {Local(g.PurchasedUtc):M/d} 구매" });
            }
        }
        if (See(TodoKinds.Notice))
            foreach (var g in await orders.ListAsync(GiftOrderService.Pick, 100, ct))
            {
                var expired = g.State == GiftStates.Expired;
                items.Add(new(TodoKinds.Notice, "gift", "🔔", $"{g.Who}{Subject(g.Who)} 선물 대상이 됐어요",
                    $"{g.KindText}{(g.NoAccount ? " · 계정 없음" : "")} · {(expired ? "고르는 기한이 지났어요" : g.OpenUntil is { } u ? $"{u:M/d}까지 고르기" : "고르는 중")}")
                { GiftId = g.Id, CanLink = g.NoAccount && !expired && BirthdayGiftService.LinkKinds.Contains(g.Kind), Tone = expired ? "late" : "",
                  Href = expired && tenant.IsAdmin ? $"/{Slug}/admin/gifts?s=pick" : null, HrefLabel = expired && tenant.IsAdmin ? "다시 열기 →" : null,
                  Note = expired && !tenant.IsAdmin ? "부서 관리자가 다시 열 수 있어요" : null });
            }
        if (See(TodoKinds.Newcomer))
            foreach (var p in await newcomers.PendingAsync(ct))
                items.Add(new(TodoKinds.Newcomer, "gift", "🆕", $"새친구 {p.NewcomerName}{Object(p.NewcomerName)} 데려온 사람 정하기", "전도 선물을 주려면 먼저 정해요")
                { Href = tenant.IsAdmin ? $"/{Slug}/admin/gifts?pick={p.NewcomerId}" : null, HrefLabel = tenant.IsAdmin ? "정하기 →" : null,
                  Note = tenant.IsAdmin ? null : $"적어 둔 이름: {p.Written}" });
        if (See(TodoKinds.Approve))
        {
            var signups = await ctx.SignupApplications.AsNoTracking().CountAsync(a => a.Status == SignupStatus.Pending, ct);
            if (signups > 0) items.Add(new(TodoKinds.Approve, "approve", "✋", $"참여 신청 승인 · {signups}건", "명부와 이어서 승인해요") { Href = $"/{Slug}/admin/members", HrefLabel = "승인하러 가기 →" });
            var promos = await ctx.PromotionRequests.AsNoTracking().CountAsync(r => r.TargetTenantId == tenant.Current.Id && r.Status == PromotionStatus.Pending, ct);
            if (promos > 0) items.Add(new(TodoKinds.Approve, "approve", "🎓", $"진급 받기 · {promos}건", "다른 부서에서 올라왔어요") { Href = $"/{Slug}/admin/promotions", HrefLabel = "받으러 가기 →" });
        }
        if (today.DayOfWeek == DayOfWeek.Sunday)   // 출석은 주일 당일만(2026-11-16 사용자)
        {
            var classes = await ctx.Classes.AsNoTracking().Where(c => !c.IsNewcomer).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
            var kids = await ctx.ClassMembers.AsNoTracking().Where(m => !m.IsTeacher).Select(m => new { m.ClassId, m.PersonId }).ToListAsync(ct);
            var checkedIds = (await ctx.AttendanceChecks.AsNoTracking().Where(a => a.Sunday == today).Select(a => a.PersonId).ToListAsync(ct)).ToHashSet();
            int Checked(int cid) => kids.Count(k => k.ClassId == cid && checkedIds.Contains(k.PersonId));
            if (See(TodoKinds.Attendance) && roles.Contains(TodoRoles.Homeroom) && rules[TodoKinds.Attendance].Contains(TodoRoles.Homeroom))
                foreach (var c in classes.Where(c => sc.Classes.Contains(c.Id) && Checked(c.Id) == 0 && kids.Any(k => k.ClassId == c.Id)))
                    items.Add(new(TodoKinds.Attendance, "attendance", "📋", $"우리 반 출석 체크 · {c.Name}", $"{today:M월 d일} 주일 · {kids.Count(k => k.ClassId == c.Id)}명 · 아직 0명")
                    { Href = $"/{Slug}/attendance", HrefLabel = "출석부 열기 →" });
            if (See(TodoKinds.Attendance) && tenant.IsAdmin && rules[TodoKinds.Attendance].Contains(TodoRoles.Admin))
            {
                var withKids = classes.Where(c => kids.Any(k => k.ClassId == c.Id)).ToList();
                var done = withKids.Count(c => Checked(c.Id) > 0);
                if (withKids.Count > 0 && done < withKids.Count)
                    items.Add(new(TodoKinds.Attendance, "attendance", "📋", $"이번 주일 출석 · 전체 {withKids.Count}반", $"체크한 반 {done} / {withKids.Count}")
                    { Href = $"/{Slug}/attendance", HrefLabel = "출석부 열기 →" });
            }
            if (sc.NewcomerStaff)
            {
                var active = await AttendanceService.ActiveNewcomersAsync(ctx, today, ct);
                var left = active.Keys.Count(id => !checkedIds.Contains(id));
                if (active.Count > 0 && left > 0)
                    items.Add(new(TodoKinds.Attendance, "attendance", "🙋", $"새친구 출석 체크 · {active.Count}명", $"아직 {left}명") { Href = $"/{Slug}/attendance/newcomers", HrefLabel = "새친구 열기 →" });
            }
        }
        if (See(TodoKinds.Birthday) && roles.Contains(TodoRoles.Homeroom))
        {
            var mineKids = await ctx.ClassMembers.AsNoTracking().Where(m => !m.IsTeacher && sc.Classes.Contains(m.ClassId)).Select(m => m.PersonId).ToListAsync(ct);
            var gifts = await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Birthday && g.State == GiftStates.Open && g.PersonId != null && mineKids.Contains(g.PersonId.Value))
                .Select(g => new { g.Id, g.PersonId, g.SourceKey }).ToListAsync(ct);
            var people = await ctx.People.AsNoTracking().Where(p => gifts.Select(g => g.PersonId!.Value).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            foreach (var g in gifts.OrderBy(g => BirthdayGiftService.BirthdayOf(g.SourceKey)))
            {
                var bday = BirthdayGiftService.BirthdayOf(g.SourceKey);
                var left = bday is { } b ? b.DayNumber - today.DayNumber : (int?)null;
                items.Add(new(TodoKinds.Birthday, "birthday", "🎂", $"생일 선물 링크 · {people.GetValueOrDefault(g.PersonId!.Value)}",
                    $"{bday:M월 d일} 생일{(left is int l && l >= 0 ? $" ({l}일 남음)" : "")} · 링크를 보내면 로그인 없이 골라요") { GiftId = g.Id, CanLink = true, Note = "아직 안 고름" });
            }
        }
        return new(name, items.OrderByDescending(i => i.Tone == "late").ThenBy(i => Array.IndexOf(new[] { "gift", "approve", "attendance", "birthday" }, i.Section)).ToList(),
            snooze is { } s && s > today ? s : null);
    }

    private DateOnly Local(DateTime? utc) => utc is { } u ? TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(u, DateTimeKind.Utc)), tenant.Current.TimeZoneId) : Today;
    private static string Subject(string name) => name.Length > 0 && name[^1] is >= '가' and <= '힣' && (name[^1] - '가') % 28 != 0 ? "이" : "가";
    private static string Object(string name) => name.Length > 0 && name[^1] is >= '가' and <= '힣' && (name[^1] - '가') % 28 != 0 ? "을" : "를";

    private static async Task<Dictionary<string, string>> NamesAsync(AppDbContext ctx, IEnumerable<string> userNames, CancellationToken ct)
    {
        var list = userNames.Distinct().ToList();
        if (list.Count == 0) return [];
        return (await ctx.Users.AsNoTracking().Where(u => list.Contains(u.UserName!)).Select(u => new { u.UserName, u.LastName, u.FirstName }).ToListAsync(ct))
            .ToDictionary(u => u.UserName!, u => AppUser.FullName(u.LastName, u.FirstName) is { Length: > 0 } n ? n : u.UserName!);
    }

    /// <summary>[구매 완료] · [전달 완료]: 선물 일을 볼 수 있는 사람 누구나(R-310).</summary>
    public async Task<TodoError> MoveGiftAsync(long giftId, string action, CancellationToken ct = default)
    {
        if (!(await VisibleKindsAsync(ct)).Contains(TodoKinds.Gift)) return TodoError.NotYours;
        return await orders.MoveAsync(Me, giftId, action, ct) switch { GiftOrderError.None => TodoError.None, GiftOrderError.NotFound => TodoError.NotFound, _ => TodoError.BadState };
    }

    /// <summary>[링크 복사]: 선물 알림(계정 없는 아이) 또는 담임의 생일 링크를 볼 수 있는 사람. 링크 주소를 돌려준다.</summary>
    public async Task<(TodoError Error, string? Url)> LinkAsync(long giftId, CancellationToken ct = default)
    {
        var kinds = await VisibleKindsAsync(ct);
        await using (var ctx = db.Create())
        {
            var g = await ctx.Gifts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == giftId, ct);
            if (g is null || !BirthdayGiftService.LinkKinds.Contains(g.Kind)) return (TodoError.NotFound, null);
            var ok = kinds.Contains(TodoKinds.Notice);
            if (!ok && kinds.Contains(TodoKinds.Birthday) && g.Kind == GiftKinds.Birthday && g.PersonId is int pid)
            {
                var (_, sc) = await MineAsync(ctx, ct);
                ok = await ctx.ClassMembers.AnyAsync(m => m.PersonId == pid && !m.IsTeacher && sc.Classes.Contains(m.ClassId), ct);
            }
            if (!ok) return (TodoError.NotYours, null);
        }
        var token = await birthdays.LinkAsync(Me, giftId, fresh: false, ct);
        return token is null ? (TodoError.BadState, null) : (TodoError.None, $"/gift/{token}");
    }

    /// <summary>안 보기(R-309): days 일 동안 로그인해도 할 일 화면으로 보내지 않는다(1 = 오늘만, 다음 날 다시).</summary>
    public async Task SnoozeAsync(int days, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var s = await ctx.UserSettings.SingleOrDefaultAsync(x => x.UserName == Me, ct);
        if (s is null) { s = new UserSetting { UserName = Me }; ctx.UserSettings.Add(s); }
        s.TodoSnoozedUntil = Today.AddDays(Math.Clamp(days, 1, 31));
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>로그인 직후 할 일 화면으로 보낼까(R-309): 할 일이 있고, 안 보기 기간이 아닐 때.</summary>
    public async Task<bool> ShouldRedirectAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby) return false;
        var board = await BoardAsync(ct);
        return board.Items.Count > 0 && board.SnoozedUntil is null;
    }
}
