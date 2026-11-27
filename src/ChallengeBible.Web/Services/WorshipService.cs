using System.Globalization;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>예배 안내 한 줄을 고칠 때 받는 값(ADR 0116). Id 가 없으면 새 줄.</summary>
public sealed record WorshipInput(int? Id, string? Title, int DayOfWeek, string? StartTime, string? Place, string? Address, string? BulletinUrl, string? BulletinDate, int SortOrder = 0, bool IsActive = true);

/// <summary>
/// 부서 예배 안내(ADR 0116, R-337): 예배 시간 · 장소는 바뀔 수 있어 DB 에 두고, 부서 관리자가 언제든 고친다(화면은 나중에 — 2026-11-18 사용자 "개발은 보류").
/// 지금 부서(영역)의 줄만 보고 고친다(RLS + 전역 필터). 바꾼 기록은 AuditLog "WorshipSchedule"(지금 부서 TenantId).
/// </summary>
public sealed class WorshipService(IAppDb db, TenantContext tenant, TimeProvider time)
{
    public const string LogAction = "WorshipSchedule";
    private static readonly string[] DayNames = ["일", "월", "화", "수", "목", "금", "토"];

    /// <summary>보이는 줄(숨긴 줄은 includeHidden 일 때만), 순서대로.</summary>
    public async Task<IReadOnlyList<WorshipSchedule>> ListAsync(bool includeHidden = false, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.WorshipSchedules.AsNoTracking().Where(w => includeHidden || w.IsActive)
            .OrderBy(w => w.SortOrder).ThenBy(w => w.Id).ToListAsync(ct);
    }

    /// <summary>고치기 · 새로 넣기. 틀리면 칸별 오류(저장 안 함). 부서 관리자만(부르는 화면이 확인, 여기서도 다시).</summary>
    public async Task<(int? Id, FieldErrors Errors)> SaveAsync(string actor, WorshipInput input, CancellationToken ct = default)
    {
        if (!tenant.IsAdmin) throw new UnauthorizedAccessException("부서 관리자만 예배 안내를 고칠 수 있어요.");
        var (v, errors) = Check(input);
        if (!errors.Ok) return (null, errors);

        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        WorshipSchedule row;
        string before;
        if (input.Id is int id)
        {
            row = await ctx.WorshipSchedules.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new KeyNotFoundException();
            before = Text(row);
        }
        else
        {
            row = new WorshipSchedule { TenantId = tenant.Current.Id, Place = "" };
            ctx.WorshipSchedules.Add(row);
            before = "없음";
        }
        (row.Title, row.DayOfWeek, row.StartTime, row.Place, row.Address, row.BulletinUrl, row.BulletinDate, row.SortOrder, row.IsActive) =
            (v.Title, v.Day, v.Start, v.Place, v.Address, v.Url, v.Date, input.SortOrder, input.IsActive);
        row.UpdatedUtc = now;
        row.UpdatedByUserName = actor;
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = LogAction, TenantId = tenant.Current.Id, Detail = Cut($"{before} → {Text(row)}") });
        await ctx.SaveChangesAsync(ct);
        return (row.Id, errors);
    }

    /// <summary>줄 지우기(숨기기는 IsActive = false 로 SaveAsync).</summary>
    public async Task<bool> DeleteAsync(string actor, int id, CancellationToken ct = default)
    {
        if (!tenant.IsAdmin) throw new UnauthorizedAccessException("부서 관리자만 예배 안내를 고칠 수 있어요.");
        await using var ctx = db.Create();
        var row = await ctx.WorshipSchedules.SingleOrDefaultAsync(w => w.Id == id, ct);
        if (row is null) return false;
        ctx.WorshipSchedules.Remove(row);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = LogAction, TenantId = tenant.Current.Id, Detail = Cut($"{Text(row)} → 지움") });
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>"매주 주일 오전 10시"처럼 사람이 읽는 시간(카드 · 기록에 쓴다).</summary>
    public static string When(byte day, TimeOnly t) =>
        $"매주 {(day == 0 ? "주일" : DayNames[day] + "요일")} {(t.Hour < 12 ? "오전" : "오후")} {(t.Hour % 12 == 0 ? 12 : t.Hour % 12)}시{(t.Minute > 0 ? $" {t.Minute}분" : "")}";

    private static string Text(WorshipSchedule w) => $"{(w.Title is { } ti ? ti + " " : "")}{When(w.DayOfWeek, w.StartTime)} {w.Place}{(w.IsActive ? "" : "(숨김)")}";

    private static string Cut(string s) => s.Length > 380 ? s[..379] + "…" : s;   // AuditLog.Detail 400자

    private static (( string? Title, byte Day, TimeOnly Start, string Place, string? Address, string? Url, DateOnly? Date) V, FieldErrors Errors) Check(WorshipInput i)
    {
        var e = new FieldErrors();
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var title = Clean(i.Title);
        var place = Clean(i.Place);
        var address = Clean(i.Address);
        var url = Clean(i.BulletinUrl);
        if (title is { Length: > 50 }) e["Title"] = "예배 이름은 50자까지예요.";
        if (i.DayOfWeek is < 0 or > 6) e["DayOfWeek"] = "요일을 골라 주세요.";
        if (!TimeOnly.TryParseExact(i.StartTime ?? "", ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) e["StartTime"] = "시간을 10:00 처럼 써 주세요.";
        if (place is null) e["Place"] = "장소를 써 주세요.";
        else if (place.Length > 100) e["Place"] = "장소는 100자까지예요.";
        if (address is { Length: > 200 }) e["Address"] = "주소는 200자까지예요.";
        if (url is not null && (url.Length > 500 || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))) e["BulletinUrl"] = "주보 링크는 http:// 나 https:// 로 시작하는 주소여야 해요.";
        DateOnly? date = null;
        if (Clean(i.BulletinDate) is { } ds)
        {
            if (DateOnly.TryParseExact(ds, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) date = d;
            else e["BulletinDate"] = "주보 날짜를 2026-11-15 처럼 써 주세요.";
        }
        return ((title, (byte)Math.Clamp(i.DayOfWeek, 0, 6), start, place ?? "", address, url, date), e);
    }
}
