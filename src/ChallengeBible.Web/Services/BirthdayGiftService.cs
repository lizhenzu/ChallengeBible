using System.Security.Cryptography;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Domain.Time;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 링크로 고르는 선물 한 건(생일 · 새친구 · 전도, 아이 화면 · 링크 화면). Birthday = 그 해의 생일(2월 29일생은 평년 2월 28일, 생일 선물만),
/// About = 전도 선물이면 데려온 새친구 이름.
/// </summary>
public sealed record BirthdayGiftView(long Id, string Name, DateOnly Birthday, string State, DateOnly? OpenUntil, int MinAmount, int MaxAmount,
    int? ItemId, string? ItemName, bool HasImage, DateTime? PickedUtc, DateTime? PurchasedUtc, int TenantId, string TenantName,
    string Kind = GiftKinds.Birthday, string? About = null)
{
    public string KindLabel => Kind switch { GiftKinds.Newcomer => "새친구 선물", GiftKinds.Invite => "전도 선물", GiftKinds.Reading => "완독 선물", GiftKinds.Copy => "완필 선물", _ => "생일 선물" };
}

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
    /// <summary>미션 홈 카드 · 로그인 없는 링크로 고르는 선물(생일 · 새친구 · 전도 · 완독, ADR 0094 C · D · E).</summary>
    public static readonly string[] LinkKinds = [GiftKinds.Birthday, GiftKinds.Newcomer, GiftKinds.Invite, GiftKinds.Reading, GiftKinds.Copy];
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

        await AlignAsync(ctx, today, r.BirthdayOpenDays, r.BirthdayCloseDays, now, ct);

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
        var resetOn = await YearResetService.LastResetDateAsync(ctx, tenant.Current.TimeZoneId, ct);   // 고르는 기간이 초기화 뒤에 시작된 아이만(정한 것 17)
        // 한 해에 한 아이당 1건(R-322): 같은 명부 줄 · 같은 해 · 같은 초기화 구간이면 생일 날짜가 달라도 이미 있다
        var have = (await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Birthday && g.PersonId != null).Select(g => new { g.PersonId, g.Year, g.SourceKey }).ToListAsync(ct))
            .Select(g => (g.PersonId!.Value, (int)g.Year, Epoch(g.SourceKey))).ToHashSet();

        var added = new List<Gift>();
        foreach (var (p, b) in due)
        {
            var acct = p.UserId is null ? null : linked.FirstOrDefault(l => l.Id == p.UserId);
            var role = p.UserId is null ? p.RoleCode : acct?.RoleCode;
            if (!BirthdayRoles.Contains((role ?? "").Trim())) continue;
            var birthday = b!.Value;
            if (resetOn is { } ro && birthday.AddDays(-r.BirthdayOpenDays) < ro) continue;
            var key = SourceKey(birthday, birthday.Year == today.Year ? reset : null);
            if (!have.Add((p.Id, birthday.Year, Epoch(key)))) continue;
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

    /// <summary>명부 줄을 따라가는 선물(생일 · 새친구 · 전도 — 완독 · 완필은 계정이 한 일이라 계정을 따라간다).</summary>
    public static readonly string[] PersonKinds = [GiftKinds.Birthday, GiftKinds.Newcomer, GiftKinds.Invite];

    /// <summary>명부 줄의 계정이 바뀌면(연결 · 풀기) 받기 전 선물의 계정도 바꾼다(R-322). 저장은 부르는 쪽에서.</summary>
    public static async Task FollowPersonAsync(AppDbContext ctx, int personId, string? userName, CancellationToken ct)
    {
        foreach (var g in await ctx.Gifts.Where(g => g.PersonId == personId && PersonKinds.Contains(g.Kind) && Live.Contains(g.State)).ToListAsync(ct))
            if (!NewcomerGiftService.Unassigned(g)) g.UserName = userName;
    }

    /// <summary>열쇠의 초기화 구간 꼬리("-r…", 없으면 "").</summary>
    public static string Epoch(string sourceKey) => sourceKey.Length > 10 ? sourceKey[10..] : "";

    /// <summary>
    /// 명부 기준 맞추기(ADR 0109, R-322): 생일 선물은 명부를 따라간다.
    /// ① 계정 = 지금 그 명부 줄에 연결된 계정(연결을 풀면 비고, 바꾸면 새 계정) — 받기 전 선물(고를 수 있음 · 고름 · 삼 · 기한 지남).
    /// ② 아직 안 고른 선물(고를 수 있음 · 기한 지남)의 생일이 명부와 다르면(생일을 고침) 새로 만들지 않고 그 선물의 생일 · 기한을 고친다.
    ///    고친 생일의 기간이 아직 안 왔으면 지운다(기간이 되면 다시 생김), 이미 지났으면 기한 지남(관리자가 다시 열 수 있음). 생일을 지웠으면 지운다.
    /// </summary>
    private async Task AlignAsync(AppDbContext ctx, DateOnly today, int openDays, int closeDays, DateTime now, CancellationToken ct)
    {
        var live = await ctx.Gifts.Where(g => g.Kind == GiftKinds.Birthday && g.PersonId != null && Live.Contains(g.State)).ToListAsync(ct);
        if (live.Count == 0) return;
        var pids = live.Select(g => g.PersonId!.Value).Distinct().ToList();
        var people = await ctx.People.AsNoTracking().Where(p => pids.Contains(p.Id)).Select(p => new { p.Id, p.UserId, p.BirthMonth, p.BirthDay }).ToDictionaryAsync(p => p.Id, ct);
        var uids = people.Values.Select(p => p.UserId).OfType<string>().Distinct().ToList();
        var accounts = uids.Count == 0 ? [] : await ctx.Users.Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
        foreach (var g in live)
        {
            if (!people.TryGetValue(g.PersonId!.Value, out var p)) continue;
            var acct = p.UserId is { } uid ? accounts.GetValueOrDefault(uid) : null;
            if (g.UserName != acct) g.UserName = acct;
            if (g.State is not (GiftStates.Open or GiftStates.Expired) || BirthdayOf(g.SourceKey) is not { } old) continue;
            if (p.BirthMonth is not { } m || p.BirthDay is not { } d)
            {
                ctx.GiftLogs.RemoveRange(await ctx.GiftLogs.Where(l => l.GiftId == g.Id).ToListAsync(ct));
                ctx.Gifts.Remove(g);
                continue;
            }
            if (BirthdayIn(old.Year, m, d) == old) continue;
            var nb = WindowBirthday(m, d, today, openDays, closeDays);
            var same = BirthdayIn(old.Year, m, d);
            if (nb is null && today < same.AddDays(-openDays))
            {
                ctx.GiftLogs.RemoveRange(await ctx.GiftLogs.Where(l => l.GiftId == g.Id).ToListAsync(ct));
                ctx.Gifts.Remove(g);
                continue;
            }
            var b = nb ?? same;
            g.SourceKey = $"B{b:yyyy}-{b:MMdd}" + Epoch(g.SourceKey);
            g.Year = (short)b.Year;
            g.OpenFrom = b.AddDays(-openDays);
            g.OpenUntil = b.AddDays(-closeDays);
            g.State = nb is null ? GiftStates.Expired : GiftStates.Open;
            ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Open, AtUtc = now, Detail = $"생일 고침 {old:M/d} → {b:M/d}" });
        }
        await ctx.SaveChangesAsync(ct);
    }

    private static readonly string[] Live = [GiftStates.Open, GiftStates.Picked, GiftStates.Purchased, GiftStates.Expired];

    /// <summary>내 생일 선물(받기 전 + 기한 지남): 계정으로 생긴 것 + 내 명부 줄로 생긴 것(나중에 계정을 연결한 경우).</summary>
    public async Task<IReadOnlyList<BirthdayGiftView>> MineAsync(CancellationToken ct = default)
    {
        if (!tenant.Has(Roles.Participant) || scope.UserName is not { } user) return [];
        await using var ctx = db.Create();
        var uid = scope.UserId;
        var pids = uid is null ? [] : await ctx.People.Where(p => p.UserId == uid).Select(p => p.Id).ToListAsync(ct);
        // 명부 줄이 있는 선물은 명부만 따라간다(R-322): 지금 내 명부 줄의 선물만(연결을 풀거나 바꾼 옛 계정은 못 본다). 완독 · 명부 줄 없는 선물은 계정으로
        var list = (await ctx.Gifts.AsNoTracking().Where(g => LinkKinds.Contains(g.Kind) && Live.Contains(g.State)
                && (g.Kind == GiftKinds.Reading || g.Kind == GiftKinds.Copy || g.PersonId == null ? g.UserName == user : pids.Contains(g.PersonId.Value)))
            .OrderBy(g => g.Id).ToListAsync(ct)).Where(g => !NewcomerGiftService.Unassigned(g)).ToList();
        return await ViewsAsync(ctx, list, ct);
    }

    private static async Task<List<BirthdayGiftView>> ViewsAsync(AppDbContext ctx, List<Gift> list, CancellationToken ct)
    {
        var pids = list.Select(g => g.PersonId).OfType<int>().Concat(list.Select(g => NewcomerGiftService.NewcomerOf(g.SourceKey)).OfType<int>()).Distinct().ToList();
        var names = await ctx.People.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var tids = list.Select(g => g.TenantId).Distinct().ToList();
        var depts = await ctx.Tenants.AsNoTracking().Where(t => tids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return list.Select(g => new BirthdayGiftView(g.Id, g.PersonId is int pid ? names.GetValueOrDefault(pid) ?? "" : "", BirthdayOf(g.SourceKey) ?? default, g.State, g.OpenUntil,
            g.MinAmount, g.MaxAmount, g.GiftItemId, g.ItemName, g.ItemHasImage == true, g.PickedUtc, g.PurchasedUtc, g.TenantId, depts.GetValueOrDefault(g.TenantId) ?? "",
            g.Kind, g.Kind == GiftKinds.Invite && NewcomerGiftService.NewcomerOf(g.SourceKey) is int nid ? names.GetValueOrDefault(nid)
                : g.Kind == GiftKinds.Reading && ReadingGiftService.RoundOf(g.SourceKey) is int rn ? $"{rn}독"
                : g.Kind == GiftKinds.Copy ? CopyGiftService.AboutOf(g.SourceKey) : null)).ToList();
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
        var g = await ctx.Gifts.AsNoTracking().SingleOrDefaultAsync(x => x.LinkToken == token && LinkKinds.Contains(x.Kind), ct);
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
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.LinkToken == token && LinkKinds.Contains(x.Kind), ct);
        var zone = g is null ? "Asia/Seoul" : await ctx.Tenants.Where(t => t.Id == g.TenantId).Select(t => t.TimeZoneId).SingleAsync(ct);
        return await PickAsync(ctx, g, itemId, LinkActor, clock.Today(zone), time.GetUtcNow().UtcDateTime, ct);
    }

    // ── 관리자 ──

    /// <summary>링크 열쇠: 없으면 만들고(fresh 면 늘 새로 — 옛 링크는 막힘). 받기 전 생일 · 새친구 · 전도 선물만. 만든 열쇠를 돌려준다.</summary>
    public async Task<string?> LinkAsync(string actor, long giftId, bool fresh, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.Id == giftId && LinkKinds.Contains(x.Kind), ct);
        if (g is null || g.State is not (GiftStates.Open or GiftStates.Picked or GiftStates.Expired) || NewcomerGiftService.Unassigned(g)) return null;
        if (g.LinkToken is { } have && !fresh) return have;
        g.LinkToken = RandomNumberGenerator.GetString(TokenChars, TokenLength);
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Link, ActorUserName = actor, AtUtc = time.GetUtcNow().UtcDateTime, Detail = fresh ? "new" : null });
        await ctx.SaveChangesAsync(ct);
        return g.LinkToken;
    }

    /// <summary>
    /// 부모님께 보낼 안내글(확정 시안 생일링크-부모안내 ②, 2026-11-17): 링크가 없으면 만들고(LinkAsync — 처음 만들 때 "링크 보냄" 이력) 안내글에 넣을 값을 돌려준다.
    /// Linked = 명부가 로그인 계정과 연결된 아이(아이 계정에서 직접 고를 수도 있음).
    /// </summary>
    public async Task<(BirthdayGiftView View, string Token, bool Linked)?> ShareAsync(string actor, long giftId, CancellationToken ct = default)
    {
        var token = await LinkAsync(actor, giftId, false, ct);
        if (token is null) return null;
        await using var ctx = db.Create();
        var g = await ctx.Gifts.AsNoTracking().SingleAsync(x => x.Id == giftId, ct);
        var linked = g.UserName is not null || (g.PersonId is int pid && await ctx.People.AnyAsync(p => p.Id == pid && p.UserId != null, ct));
        return ((await ViewsAsync(ctx, [g], ct))[0], token, linked);
    }

    /// <summary>안내글 본문: 부서 · 아이 이름 · 왜(생일 · 새친구 · 전도 · 완독 · 완필) · 할 일 · 기한 · 링크.</summary>
    public static string ShareText(BirthdayGiftView g, string url, bool linked)
    {
        var why = g.Kind switch
        {
            GiftKinds.Newcomer => $"{g.TenantName}에 와 줘서 고마워요!",
            GiftKinds.Invite => $"{g.About ?? "친구"}{Obj(g.About ?? "친구")} 교회에 데려와 줘서 고마워요!",
            GiftKinds.Reading => $"성경 {g.About} 완독을 축하해요!",
            GiftKinds.Copy => $"성경 {g.About} 필사 완필을 축하해요!",
            _ => $"생일({g.Birthday:M월 d일})을 축하해요! 🎂",
        };
        var sb = new System.Text.StringBuilder();
        sb.Append('[').Append(g.TenantName).Append("]\n");
        sb.Append(g.Name).Append(g.Kind == GiftKinds.Birthday ? " 어린이의 " : " 어린이, ").Append(why).Append("\n\n");
        sb.Append("교회에서 ").Append(g.KindLabel).Append("을 준비해요. 아래 링크를 눌러 아이와 함께 선물 하나를 골라 주세요.\n\n");
        sb.Append("· 로그인 없이 바로 열려요\n");
        if (g.OpenUntil is { } u && g.State == GiftStates.Open) sb.Append($"· {u:M월 d일}까지 골라 주세요\n");
        if (linked) sb.Append("· 아이 계정으로 로그인해서 직접 골라도 돼요\n");
        sb.Append("· 고른 선물은 선생님이 준비해서 주일에 전해 드려요\n\n");
        sb.Append(url);
        return sb.ToString();
    }

    private static string Obj(string w) => w.Length > 0 && w[^1] is >= '가' and <= '힣' && (w[^1] - '가') % 28 != 0 ? "을" : "를";

    /// <summary>
    /// [건너뛰기]: 아직 사지 않은 생일 · 새친구 · 전도 선물(고를 수 있음 · 고름 · 기한 지남) → 건너뜀. [되돌리기]: 건너뜀 → 고를 수 있음(기한이 지났으면 기한 지남).
    /// 전도한 아이를 정하지 못한 채 건너뛴 것은 되돌리면 지워서 다시 "전도한 아이를 골라 주세요"로.
    /// </summary>
    public async Task<bool> SkipAsync(string actor, long giftId, bool skip, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var g = await ctx.Gifts.SingleOrDefaultAsync(x => x.Id == giftId && LinkKinds.Contains(x.Kind), ct);
        if (g is null) return false;
        if (skip ? g.State is not (GiftStates.Open or GiftStates.Picked or GiftStates.Expired) : g.State != GiftStates.Skipped) return false;
        if (!skip && NewcomerGiftService.Unassigned(g))
        {
            ctx.GiftLogs.RemoveRange(await ctx.GiftLogs.Where(l => l.GiftId == g.Id).ToListAsync(ct));
            ctx.Gifts.Remove(g);
            await ctx.SaveChangesAsync(ct);
            return true;
        }
        g.State = skip ? GiftStates.Skipped : g.GiftItemId is not null ? GiftStates.Picked : g.OpenUntil < Today ? GiftStates.Expired : GiftStates.Open;
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = skip ? GiftActions.Skip : GiftActions.Undo, ActorUserName = actor, AtUtc = time.GetUtcNow().UtcDateTime, ItemName = g.ItemName, ItemPrice = g.ItemPrice });
        await ctx.SaveChangesAsync(ct);
        return true;
    }
}
