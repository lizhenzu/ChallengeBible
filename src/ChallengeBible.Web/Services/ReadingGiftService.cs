using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 성경통독 완독 선물(확정 시안 완독-선물, ADR 0094 E · 정한 것 16, R-289): 이 부서의 학생 · 새친구가 1,189장을 다 읽으면 그 회차에 하나(열쇠 R{회차}).
/// 완독한 날(부서 날짜)부터 PickDays 일 안에 고르고, 그보다 오래된 완독에는 생기지 않는다(질문 1 → 1). 비용 부서 = 완독한 뒤 이 부서 화면을 연 부서.
/// 금액 = 부서 회차 표(GiftReadingAmount, 빈 칸은 규칙 ReadingAmounts), 표에 없는 회차는 마지막 값. 어른은 박수만.
/// 고르기 · 링크 · 건너뛰기 · 다시 열기는 생일 선물과 같다(BirthdayGiftService.LinkKinds). 성경통독은 해가 바뀌어도 초기화하지 않는다.
/// </summary>
public sealed class ReadingGiftService(IAppDb db, TenantContext tenant, TenantClock clock, RuleService rules, TimeProvider time)
{
    private int T => tenant.Current.Id;
    private DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    public static string SourceKey(int round) => $"R{round}";
    public static int? RoundOf(string sourceKey) => sourceKey.Length > 1 && sourceKey[0] == 'R' && int.TryParse(sourceKey[1..], out var n) ? n : null;

    /// <summary>회차 금액: 부서 표 → 규칙, 표에 없는 회차는 마지막 값(R-289).</summary>
    public static int AmountFor(int round, IReadOnlyList<int> amounts) => amounts.Count == 0 ? 0 : amounts[Math.Clamp(round - 1, 0, amounts.Count - 1)];

    /// <summary>선물 만들기 + 기한 지난 것 닫기. 통독 홈 · 미션 홈 · 선물 처리 화면을 열 때 부른다.</summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby) return;
        await using var ctx = db.Create();
        var today = Today;
        var r = (await rules.ForDateAsync(today, ct)).Gifts;
        var now = time.GetUtcNow().UtcDateTime;

        foreach (var g in await ctx.Gifts.Where(g => g.Kind == GiftKinds.Reading && g.State == GiftStates.Open && g.OpenUntil < today).ToListAsync(ct))
        {
            g.State = GiftStates.Expired;
            ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Expire, AtUtc = now });
        }
        await ctx.SaveChangesAsync(ct);

        var students = (await DeptMembership.PeopleAsync(ctx, ct)).Where(p => BirthdayGiftService.BirthdayRoles.Contains((p.RoleCode ?? "").Trim())).ToList();
        if (students.Count == 0) return;
        var names = students.Select(p => p.UserName).ToList();
        var since = now.AddDays(-(r.PickDays + 1));   // 완독 30일 안만(넉넉히 하루 더 읽고 날짜로 다시 거른다)
        // 통독 표는 계정 범위라 전역 필터가 "본인만"이다. 이 부서 승인 학생으로 직접 거른다(ReadingBoardService 와 같다)
        var done = await ctx.ReadingRounds.IgnoreQueryFilters().AsNoTracking().Where(x => names.Contains(x.UserName) && x.CompletedAt != null && x.CompletedAt >= since)
            .Select(x => new { x.UserName, x.RoundNo, At = x.CompletedAt!.Value }).ToListAsync(ct);
        if (done.Count == 0) return;
        var have = (await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Reading && names.Contains(g.UserName!)).Select(g => new { g.UserName, g.SourceKey }).ToListAsync(ct))
            .Select(g => (g.UserName!.ToLowerInvariant(), g.SourceKey)).ToHashSet();
        var dept = await ctx.GiftReadingAmounts.AsNoTracking().OrderBy(a => a.RoundNo).ToListAsync(ct);
        var amounts = Enumerable.Range(1, Math.Max(r.ReadingAmounts.Count, dept.Count == 0 ? 0 : dept.Max(a => a.RoundNo)))
            .Select(n => dept.FirstOrDefault(a => a.RoundNo == n)?.Amount ?? r.ReadingAmounts[Math.Min(n, r.ReadingAmounts.Count) - 1]).ToList();
        var people = await ctx.People.AsNoTracking().Where(p => p.UserId != null).Select(p => new { p.Id, p.UserId }).ToListAsync(ct);

        var added = new List<Gift>();
        foreach (var d in done)
        {
            var on = TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(d.At, DateTimeKind.Utc)), tenant.Current.TimeZoneId);
            var until = on.AddDays(r.PickDays);
            var key = SourceKey(d.RoundNo);
            if (until < today || have.Contains((d.UserName.ToLowerInvariant(), key))) continue;
            var uid = students.First(s => s.UserName == d.UserName).UserId;
            added.Add(new Gift
            {
                TenantId = T, Kind = GiftKinds.Reading, Year = (short)on.Year, UserName = d.UserName, PersonId = people.FirstOrDefault(p => p.UserId == uid)?.Id,
                SourceKey = key, State = GiftStates.Open, OpenFrom = on, OpenUntil = until, MinAmount = 0, MaxAmount = AmountFor(d.RoundNo, amounts), CreatedUtc = now,
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
