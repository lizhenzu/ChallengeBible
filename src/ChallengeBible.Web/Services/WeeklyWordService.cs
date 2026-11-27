using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>이번 주 말씀 입력값(ADR 0142): 주일 날짜 · 말씀 제목 · 설교 내용.</summary>
public sealed record WeeklyWordInput(DateOnly? SundayDate, string? Title, string? Body);

/// <summary>
/// 부서마다 직접 입력하는 이번 주 말씀(ADR 0142, R-374 · R-375, 2026-11-27 사용자): 주일 날짜 · 제목(60자) · 설교 내용(4000자).
/// 날짜는 입력한 날이 아니라 전한 **주일**(일요일만), 같은 주일은 한 건. 부서 관리자만 입력하고 누구나 본다.
/// 「준비중」: 가장 최근 말씀의 주일에서 7일이 지났거나 말씀이 없으면(부서 시간대 오늘 기준). 바꾼 기록은 AuditLog "WeeklyWord".
/// </summary>
public sealed class WeeklyWordService(IAppDb db, TenantContext tenant, TimeProvider time)
{
    public const string LogAction = "WeeklyWord";
    public const int MaxTitle = 60, MaxBody = 4000, StaleDays = 7;

    /// <summary>가장 최근 주일 순.</summary>
    public async Task<IReadOnlyList<WeeklyWord>> ListAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby) return [];
        await using var ctx = db.Create();
        return await ctx.WeeklyWords.AsNoTracking().OrderByDescending(w => w.SundayDate).ToListAsync(ct);
    }

    public async Task<WeeklyWord?> LatestAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby) return null;
        await using var ctx = db.Create();
        return await ctx.WeeklyWords.AsNoTracking().OrderByDescending(w => w.SundayDate).FirstOrDefaultAsync(ct);
    }

    public async Task<WeeklyWord?> GetAsync(DateOnly sunday, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.WeeklyWords.AsNoTracking().SingleOrDefaultAsync(w => w.SundayDate == sunday, ct);
    }

    /// <summary>준비중인가: 말씀이 없거나, 가장 최근 주일에서 7일 이상 지났다.</summary>
    public static bool IsStale(DateOnly? latest, DateOnly today) => latest is null || today.DayNumber - latest.Value.DayNumber >= StaleDays;

    /// <summary>저장(없으면 만들고, 같은 주일이 있으면 고친다). 부서 관리자만.</summary>
    public async Task<FieldErrors> SaveAsync(string actor, WeeklyWordInput input, CancellationToken ct = default)
    {
        if (!tenant.IsAdmin) throw new UnauthorizedAccessException("부서 관리자만 이번 주 말씀을 올릴 수 있어요.");
        var errors = new FieldErrors();
        var title = input.Title?.Trim();
        var body = input.Body?.Trim();
        if (input.SundayDate is null) errors["SundayDate"] = "언제 주일 설교인지 골라 주세요.";
        else if (input.SundayDate.Value.DayOfWeek != DayOfWeek.Sunday) errors["SundayDate"] = "주일(일요일)을 골라 주세요.";
        if (string.IsNullOrEmpty(title)) errors["Title"] = "말씀 제목을 넣어 주세요.";
        else if (title.Length > MaxTitle) errors["Title"] = $"제목은 {MaxTitle}자까지예요.";
        if (string.IsNullOrEmpty(body)) errors["Body"] = "설교 내용을 넣어 주세요.";
        else if (body.Length > MaxBody) errors["Body"] = $"설교 내용은 {MaxBody:N0}자까지예요.";
        if (!errors.Ok) return errors;

        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        var date = input.SundayDate!.Value;
        var row = await ctx.WeeklyWords.SingleOrDefaultAsync(w => w.SundayDate == date, ct);
        var isNew = row is null;
        if (row is null)
        {
            row = new WeeklyWord { TenantId = tenant.Current.Id, SundayDate = date, Title = title!, Body = body!, CreatedUtc = now, UpdatedUtc = now };
            ctx.WeeklyWords.Add(row);
        }
        (row.Title, row.Body, row.UpdatedUtc, row.UpdatedByUserName) = (title!, body!, now, actor);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = LogAction, TenantId = tenant.Current.Id,
            Detail = $"{date:yyyy-MM-dd} 주일 · \"{title}\" · {(isNew ? "올림" : "고침")}" });
        await ctx.SaveChangesAsync(ct);
        return errors;
    }

    public async Task<bool> DeleteAsync(string actor, DateOnly sunday, CancellationToken ct = default)
    {
        if (!tenant.IsAdmin) throw new UnauthorizedAccessException("부서 관리자만 지울 수 있어요.");
        await using var ctx = db.Create();
        var row = await ctx.WeeklyWords.SingleOrDefaultAsync(w => w.SundayDate == sunday, ct);
        if (row is null) return false;
        ctx.WeeklyWords.Remove(row);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = LogAction, TenantId = tenant.Current.Id,
            Detail = $"{sunday:yyyy-MM-dd} 주일 · \"{row.Title}\" · 지움" });
        await ctx.SaveChangesAsync(ct);
        return true;
    }
}
