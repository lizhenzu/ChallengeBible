using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 새해 초기화 기록(ADR 0094 정한 것 17). 버튼 · 화면은 완전 초기화(ADR 0102, <see cref="ResetService"/>)로 합쳤다.
/// 남은 일: 예전 초기화 시각(GiftYearClose)을 읽는 도우미 — 완전 초기화가 이 기록도 지우므로 보통 null.
/// </summary>
public static class YearResetService
{
    /// <summary>예전 새해 초기화의 감사 로그 Action(초기화 기록에 함께 보인다).</summary>
    public const string Action = "GiftYearReset";

    /// <summary>그해 마지막 초기화 시각(UTC). 2026-11-16 부터 초기화가 이 기록까지 지우므로 보통 null — 개인 점수 · 팀 선물이 이 시각 뒤만 본다.</summary>
    public static Task<DateTime?> LastResetAsync(AppDbContext ctx, int year, CancellationToken ct) =>
        ctx.GiftYearCloses.AsNoTracking().Where(c => c.Year == year).Select(c => (DateTime?)c.ClosedUtc).SingleOrDefaultAsync(ct);

    /// <summary>가장 최근 초기화 날(부서 날짜, 해와 상관없이). 새친구 · 전도 · 생일 선물은 이날 뒤에 시작된 것만 다시 생긴다. 없으면 null.</summary>
    public static async Task<DateOnly?> LastResetDateAsync(AppDbContext ctx, string timeZoneId, CancellationToken ct)
    {
        var at = await ctx.GiftYearCloses.AsNoTracking().OrderByDescending(c => c.ClosedUtc).Select(c => (DateTime?)c.ClosedUtc).FirstOrDefaultAsync(ct);
        return at is { } u ? TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(u, DateTimeKind.Utc)), timeZoneId) : null;
    }
}
