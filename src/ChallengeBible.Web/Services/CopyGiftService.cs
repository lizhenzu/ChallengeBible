using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 성경필사 완필 선물(ADR 0099, R-307): 이 부서의 학생 · 새친구가 신약 전체(열쇠 CNT) 또는 구약 전체(COT)를 다 쓰면 하나씩.
/// 다 쓴 날(마지막 절을 쓴 날, 부서 날짜)부터 PickDays 일 안에 고르고, 그보다 오래된 완필에는 생기지 않는다(완독 선물과 같다).
/// 금액 = 규칙 CopyNewAmount · CopyOldAmount. 어른은 축하만. 고르기 · 링크 · 건너뛰기 · 다시 열기는 생일 선물과 같다(BirthdayGiftService.LinkKinds).
/// 성경필사는 새해에도 지우지 않는다.
/// </summary>
public sealed class CopyGiftService(IAppDb db, TenantContext tenant, TenantClock clock, RuleService rules, TimeProvider time, CopyService copy)
{
    public const string NewKey = "CNT", OldKey = "COT";
    private int T => tenant.Current.Id;
    private DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    /// <summary>열쇠 → "신약" · "구약".</summary>
    public static string? AboutOf(string sourceKey) => sourceKey switch { NewKey => "신약", OldKey => "구약", _ => null };

    /// <summary>선물 만들기 + 기한 지난 것 닫기. 필사 홈 · 선물 처리 화면을 열 때 부른다.</summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby || !tenant.Current.CopyEnabled) return;
        var counts = await copy.CountsAsync(ct);
        await using var ctx = db.Create();
        var today = Today;
        var r = (await rules.ForDateAsync(today, ct)).Gifts;
        var now = time.GetUtcNow().UtcDateTime;

        foreach (var g in await ctx.Gifts.Where(g => g.Kind == GiftKinds.Copy && g.State == GiftStates.Open && g.OpenUntil < today).ToListAsync(ct))
        {
            g.State = GiftStates.Expired;
            ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Expire, AtUtc = now });
        }
        await ctx.SaveChangesAsync(ct);

        var students = (await DeptMembership.PeopleAsync(ctx, ct)).Where(p => BirthdayGiftService.BirthdayRoles.Contains((p.RoleCode ?? "").Trim())).ToList();
        if (students.Count == 0) return;
        var names = students.Select(p => p.UserName).ToList();
        // 필사 표는 계정 범위라 전역 필터가 "본인만"이다. 이 부서 승인 학생으로 직접 거른다
        var rows = await ctx.CopyVerses.IgnoreQueryFilters().AsNoTracking().Where(v => names.Contains(v.UserName))
            .Select(v => new { v.UserName, v.BookId, v.Chapter, v.Verse, v.WrittenAt }).ToListAsync(ct);
        if (rows.Count == 0) return;
        int Total(bool isNew) => counts.Where(k => CopyService.IsNew(k.Key.Book) == isNew).Sum(k => k.Value);
        var totals = new Dictionary<bool, int> { [true] = Total(true), [false] = Total(false) };
        var have = (await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Copy && names.Contains(g.UserName!)).Select(g => new { g.UserName, g.SourceKey }).ToListAsync(ct))
            .Select(g => (g.UserName!.ToLowerInvariant(), g.SourceKey)).ToHashSet();
        var people = await ctx.People.AsNoTracking().Where(p => p.UserId != null).Select(p => new { p.Id, p.UserId }).ToListAsync(ct);

        var added = new List<Gift>();
        foreach (var grp in rows.Where(v => counts.TryGetValue((v.BookId, v.Chapter), out var n) && v.Verse <= n).GroupBy(v => (v.UserName, New: CopyService.IsNew(v.BookId))))
        {
            if (totals[grp.Key.New] == 0 || grp.Count() < totals[grp.Key.New]) continue;
            var key = grp.Key.New ? NewKey : OldKey;
            if (have.Contains((grp.Key.UserName.ToLowerInvariant(), key))) continue;
            var on = TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(grp.Max(v => v.WrittenAt), DateTimeKind.Utc)), tenant.Current.TimeZoneId);
            var until = on.AddDays(r.PickDays);
            if (until < today) continue;   // 기한이 지난 옛 완필에는 만들지 않는다
            var uid = students.First(s => s.UserName == grp.Key.UserName).UserId;
            added.Add(new Gift
            {
                TenantId = T, Kind = GiftKinds.Copy, Year = (short)on.Year, UserName = grp.Key.UserName, PersonId = people.FirstOrDefault(p => p.UserId == uid)?.Id,
                SourceKey = key, State = GiftStates.Open, OpenFrom = on, OpenUntil = until, MinAmount = 0,
                MaxAmount = grp.Key.New ? r.CopyNewAmount : r.CopyOldAmount, CreatedUtc = now,
            });
        }
        if (added.Count == 0) return;
        ctx.Gifts.AddRange(added);
        try
        {
            await ctx.SaveChangesAsync(ct);
            foreach (var g in added) ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Open, AtUtc = now, Detail = $"{g.SourceKey} max {g.MaxAmount}" });
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { }   // 다른 요청이 같은 선물을 먼저 만들었다
    }
}
