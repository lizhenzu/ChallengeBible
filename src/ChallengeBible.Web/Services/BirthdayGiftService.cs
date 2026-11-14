using System.Security.Cryptography;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Domain.Time;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>생일 선물 한 건(아이 화면 · 링크 화면). Birthday = 그 해의 생일(2월 29일생은 평년 2월 28일).</summary>
public sealed record BirthdayGiftView(long Id, string Name, DateOnly Birthday, string State, DateOnly? OpenUntil, int MinAmount, int MaxAmount,
    int? ItemId, string? ItemName, bool HasImage, DateTime? PickedUtc, DateTime? PurchasedUtc, int TenantId, string TenantName);

/// <summary>_BirthdayPick 조각의 값: Link = 로그인 없는 링크 화면, Self = 이 화면 주소.</summary>
public sealed record BirthdayPickView(BirthdayGiftView Gift, IReadOnlyList<TeamPickItem> Items, bool Change, string Self, string? Msg, bool Link);

/// <summary>
/// 생일 선물(확정 시안 생일-선물, ADR 0094 D · 정한 것 14, R-288 · R-287): 명부 생일이 있는 학생 · 새친구 **본인**에게, 생일 BirthdayOpenDays 일 전 ~ BirthdayCloseDays 일 전.
/// 미션 홈 · 선물 처리 화면을 열 때 만든다(한 해 한 번, 열쇠 B{해}-{MMdd}). 계정이 있으면 미션 홈 카드, 모두에게 관리자 링크(로그인 없음) — 어느 쪽이든 한 건.
/// 금액 = 부서 생일 범위(빈 칸 = 규칙 기본값) 안의 상품, 가격은 보이지 않는다. 기한 지나면 [다시 열기], 관리자 [건너뛰기] · 되돌리기.
/// </summary>
public sealed class BirthdayGiftService(IAppDb db, ISystemDb systemDb, RequestScope scope, TenantContext tenant, TenantClock clock, RuleService rules, TimeProvider time)
{
    public const int TokenLength = 32;
    private const string TokenChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    /// <summary>생일을 받는 역할: 학생 · 새친구(사용자 2026-11-14 질문 2 → 1).</summary>
    public static readonly string[] BirthdayRoles = [UserRole.StudentCode, ClassService.NewcomerCode];

    private int T => tenant.Current.Id;
    private DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    /// <summary>그 해의 생일. 2월 29일생은 평년 2월 28일.</summary>
    public static DateOnly BirthdayIn(int year, int month, int day) => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <summary>오늘 고를 수 있는 기간(생일 openDays 일 전 ~ closeDays 일 전) 안에 드는 생일. 1월 생일은 전해 12월부터라 올해 · 내년을 본다.</summary>
    public static DateOnly? WindowBirthday(int month, int day, DateOnly today, int openDays, int closeDays)
    {
        foreach (var y in new[] { today.Year, today.Year + 1 })
        {
            var b = BirthdayIn(y, month, day);
            if (b.AddDays(-openDays) <= today && today <= b.AddDays(-closeDays)) return b;
        }
        return null;
    }

    /// <summary>B{해}-{MMdd}, 새해 초기화를 누른 뒤면 -r{초기화 시각}(정한 것 12, 팀 선물과 같음).</summary>
    public static string SourceKey(DateOnly birthday, DateTime? reset = null) => $"B{birthday:yyyy}-{birthday:MMdd}" + (reset is { } r ? $"-r{r:yyMMddHHmmss}" : "");
    public static DateOnly? BirthdayOf(string sourceKey) =>
        sourceKey.Length >= 10 && sourceKey[0] == 'B' && int.TryParse(sourceKey[1..5], out var y) && int.TryParse(sourceKey[6..8], out var m) && int.TryParse(sourceKey[8..10], out var d)
            && m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m) ? new DateOnly(y, m, d) : null;

    /// <summary>선물 만들기 + 기한 지난 것 닫기. 미션 홈 · 선물 처리 화면을 열 때 부른다.</summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby) return;
        await using var ctx = db.Create();
        var today = Today;
        var r = (await rules.ForDateAsync(today, ct)).Gifts;
        var now = time.GetUtcNow().UtcDateTime;

        foreach (var g in await ctx.Gifts.Where(g => g.Kind == GiftKinds.Birthday && g.State == GiftStates.Open && g.OpenUntil < today).ToListAsync(ct))
        {
            g.State = GiftStates.Expired;
            ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Expire, AtUtc = now });
        }
        await ctx.SaveChangesAsync(ct);

        // 생일이 있는 사람 → 역할(연결된 계정은 소속의 역할, 아니면 명부의 역할) → 학생 · 새친구만
        var people = await ctx.People.AsNoTracking().Where(p => p.BirthMonth != null && p.BirthDay != null)
            .Select(p => new { p.Id, p.UserId, p.RoleCode, M = (int)p.BirthMonth!.Value, D = (int)p.BirthDay!.Value }).ToListAsync(ct);
        var due = people.Select(p => (p, B: WindowBirthday(p.M, p.D, today, r.BirthdayOpenDays, r.BirthdayCloseDays))).Where(x => x.B is not null).ToList();
        if (due.Count == 0) return;
        var userIds = due.Select(x => x.p.UserId).OfType<string>().ToList();
        var linked = userIds.Count == 0 ? [] : await (from d in ctx.DeptRoles join u in ctx.Users on d.UserId equals u.Id
                                                      where userIds.Contains(d.UserId!) select new { u.Id, u.UserName, d.RoleCode }).ToListAsync(ct);
        var s = await ctx.GiftSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        var (min, max) = (s?.BirthdayMin ?? r.Birthday.Min, s?.BirthdayMax ?? r.Birthday.Max);
        var reset = await YearResetService.LastResetAsync(ctx, today.Year, ct);
        var have = (await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Birthday).Select(g => new { g.PersonId, g.SourceKey }).ToListAsync(ct))
            .Select(g => (g.PersonId, g.SourceKey)).ToHashSet();

        var added = new List<Gift>();
        foreach (var (p, b) in due)
        {
            var acct = p.UserId is null ? null : linked.FirstOrDefault(l => l.Id == p.UserId);
            var role = p.UserId is null ? p.RoleCode : acct?.RoleCode;
            if (!BirthdayRoles.Contains((role ?? "").Trim())) continue;
            var birthday = b!.Value;
            var key = SourceKey(birthday, birthday.Year == today.Year ? reset : null);
            if (have.Contains((p.Id, key))) continue;
            var g = new Gift
            {
                TenantId = T, Kind = GiftKinds.Birthday, Year = (short)birthday.Year, UserName = acct?.UserName, PersonId = p.Id, SourceKey = key, State = GiftStates.Open,
                OpenFrom = birthday.AddDays(-r.BirthdayOpenDays), OpenUntil = birthday.AddDays(-r.BirthdayCloseDays), MinAmount = min, MaxAmount = max, CreatedUtc = now,
            };
            ctx.Gifts.Add(g);
            added.Add(g);
        }
        if (added.Count == 0) return;
        try
        {
            await ctx.SaveChangesAsync(ct);
            foreach (var g in added) ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Open, AtUtc = now, Detail = $"{g.SourceKey} {g.MinAmount}-{g.MaxAmount}" });
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { }   // 다른 요청이 같은 선물을 먼저 만들었다
    }

    private static readonly string[] Live = [GiftStates.Open, GiftStates.Picked, GiftStates.Purchased, GiftStates.Expired];

    /// <summary>내 생일 선물(받기 전 + 기한 지남): 계정으로 생긴 것 + 내 명부 줄로 생긴 것(나중에 계정을 연결한 경우).</summary>
    public async Task<IReadOnlyList<BirthdayGiftView>> MineAsync(CancellationToken ct = default)
    {
        if (!tenant.Has(Roles.Participant) || scope.UserName is not { } user) return [];
        await using var ctx = db.Create();
        var uid = scope.UserId;
        var pids = uid is null ? [] : await ctx.People.Where(p => p.UserId == uid).Select(p => p.Id).ToListAsync(ct);
        var list = await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Birthday && Live.Contains(g.State) && (g.UserName == user || (g.PersonId != null && pids.Contains(g.PersonId.Value))))
            .OrderBy(g => g.Id).ToListAsync(ct);
        return await ViewsAsync(ctx, list, ct);
    }

    private static async Task<List<BirthdayGiftView>> ViewsAsync(AppDbContext ctx, List<Gift> list, CancellationToken ct)
    {
        var pids = list.Select(g => g.PersonId).OfType<int>().Distinct().ToList();
        var names = await ctx.People.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var tids = list.Select(g => g.TenantId).Distinct().ToList();
        var depts = await ctx.Tenants.AsNoTracking().Where(t => tids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return list.Select(g => new BirthdayGiftView(g.Id, g.PersonId is int pid ? names.GetValueOrDefault(pid) ?? "" : "", BirthdayOf(g.SourceKey) ?? default, g.State, g.OpenUntil,
            g.MinAmount, g.MaxAmount, g.GiftItemId, g.ItemName, g.ItemHasImage == true, g.PickedUtc, g.PurchasedUtc, g.TenantId, depts.GetValueOrDefault(g.TenantId) ?? "")).ToList();
    }

    /// <summary>금액 범위 안 상품(공통 − 그 부서가 숨긴 것 + 부서 상품, 판매 중), 싼 순. 링크 화면은 시스템 연결이라 부서를 직접 준다.</summary>
    private static Task<List<TeamPickItem>> ItemsAsync(AppDbContext ctx, int tenantId, int min, int max, CancellationToken ct)
    {
        var hidden = ctx.GiftItemsHidden.Where(h => h.TenantId == tenantId).Select(h => h.GiftItemId);
        return ctx.GiftItems.AsNoTracking()
            .Where(i => i.Status == GiftItemStatus.Active && i.Price >= min && i.Price <= max && (i.TenantId == tenantId || (i.TenantId == null && !hidden.Contains(i.Id))))
            .OrderBy(i => i.Price).ThenBy(i => i.Name)
            .Select(i => new TeamPickItem(i.Id, i.Name, i.Description, i.HasImage, i.UpdatedUtc, i.Price)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TeamPickItem>> ItemsAsync(BirthdayGiftView g, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ItemsAsync(ctx, T, g.MinAmount, g.MaxAmount, ct);
    }

    /// <summary>고르기 · 바꾸기: 고를 수 있음 · 고름이고, 기한 안이고, 금액 안일 때. actor = 아이 계정 또는 "link".</summary>
    private static async Task<TeamPickError> PickAsync(AppDbContext ctx, Gift? g, int itemId, string actor, DateOnly today, DateTime now, CancellationToken ct)
    {
        if (g is null) return TeamPickError.NotFound;
        if (g.State is not (GiftStates.Open or GiftStates.Picked) || (g.State == GiftStates.Open && g.OpenUntil is { } until && until < today)) return TeamPickError.Closed;
        var item = (await ItemsAsync(ctx, g.TenantId, g.MinAmount, g.MaxAmount, ct)).FirstOrDefault(i => i.Id == itemId);
        if (item is null) return TeamPickError.TooExpensive;
        var change = g.State == GiftStates.Picked;
        g.State = GiftStates.Picked;
        g.GiftItemId = item.Id; g.ItemName = item.Name; g.ItemPrice = item.Price; g.ItemHasImage = item.HasImage; g.PickedUtc = now;
        ctx.GiftLogs.Add(new GiftLog { TenantId = g.TenantId, GiftId = g.Id, Action = change ? GiftActions.Change : GiftActions.Pick, ActorUserName = actor, AtUtc = now, ItemName = item.Name, ItemPrice = item.Price });
        await ctx.SaveChangesAsync(ct);
        return TeamPickError.None;
    }

    public async Task<TeamPickError> PickAsync(long giftId, int itemId, CancellationToken ct = default)
    {
        if ((await MineAsync(ct)).All(g => g.Id != giftId) || scope.UserName is not { } user) return TeamPickError.NotMember;
        await using var ctx = db.Create();
        return await PickAsync(ctx, await ctx.Gifts.SingleOrDefaultAsync(x => x.Id == giftId, ct), itemId, user, Today, time.GetUtcNow().UtcDateTime, ct);
    }

    // ── 로그인 없는 링크(R-287): 부서를 모르는 요청이라 시스템 연결로 열쇠 하나만 찾는다 ──

    public const string LinkActor = "link";

    public static bool IsToken(string? token) => token is { Length: TokenLength } && token.All(TokenChars.Contains);

    public async Task<BirthdayGiftView?> ByTokenAsync(string token, CancellationToken ct = default)
    {
        if (!IsToken(token)) return null;
        await using var ctx = systemDb.Create(LinkActor);
        var g = await ctx.Gifts.AsNoTracking().SingleOrDefaultAsync(x => x.LinkToken == token && x.Kind == GiftKinds.Birthday, ct);
        return g is null || !Live.Contains(g.State) ? null : (await ViewsAsync(ctx, [g], ct))[0];
    }

    public async Task<IReadOnlyList<TeamPickItem>> ItemsByTokenAsync(BirthdayGiftView g, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(LinkActor);
        return await ItemsAsync(ctx, g.TenantId, g.MinAmount, g.MaxAmount, ct);
    }

    public async Task<TeamPickError> PickByTokenAsync(string token, int itemId, CancellationToken ct = default)
    {
        if (!IsToken(token)) return TeamPickError.NotFound;
        await using var ctx = systemDb.Create(LinkActor);
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.LinkToken == token && x.Kind == GiftKinds.Birthday, ct);
        var zone = g is null ? "Asia/Seoul" : await ctx.Tenants.Where(t => t.Id == g.TenantId).Select(t => t.TimeZoneId).SingleAsync(ct);
        return await PickAsync(ctx, g, itemId, LinkActor, clock.Today(zone), time.GetUtcNow().UtcDateTime, ct);
    }

    // ── 관리자 ──

    /// <summary>링크 열쇠: 없으면 만들고(fresh 면 늘 새로 — 옛 링크는 막힘). 받기 전 생일 선물만. 만든 열쇠를 돌려준다.</summary>
    public async Task<string?> LinkAsync(string actor, long giftId, bool fresh, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.Id == giftId && x.Kind == GiftKinds.Birthday, ct);
        if (g is null || g.State is not (GiftStates.Open or GiftStates.Picked or GiftStates.Expired)) return null;
        if (g.LinkToken is { } have && !fresh) return have;
        g.LinkToken = RandomNumberGenerator.GetString(TokenChars, TokenLength);
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Link, ActorUserName = actor, AtUtc = time.GetUtcNow().UtcDateTime, Detail = fresh ? "new" : null });
        await ctx.SaveChangesAsync(ct);
        return g.LinkToken;
    }

    /// <summary>[건너뛰기]: 아직 사지 않은 생일 선물(고를 수 있음 · 고름 · 기한 지남) → 건너뜀. [되돌리기]: 건너뜀 → 고를 수 있음(기한이 지났으면 기한 지남).</summary>
    public async Task<bool> SkipAsync(string actor, long giftId, bool skip, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.Id == giftId && x.Kind == GiftKinds.Birthday, ct);
        if (g is null) return false;
        if (skip ? g.State is not (GiftStates.Open or GiftStates.Picked or GiftStates.Expired) : g.State != GiftStates.Skipped) return false;
        g.State = skip ? GiftStates.Skipped : g.GiftItemId is not null ? GiftStates.Picked : g.OpenUntil < Today ? GiftStates.Expired : GiftStates.Open;
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = skip ? GiftActions.Skip : GiftActions.Undo, ActorUserName = actor, AtUtc = time.GetUtcNow().UtcDateTime, ItemName = g.ItemName, ItemPrice = g.ItemPrice });
        await ctx.SaveChangesAsync(ct);
        return true;
    }
}
