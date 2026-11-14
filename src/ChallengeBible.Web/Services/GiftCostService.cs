using System.Globalization;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>기간: year 올해 · month 이번 달 · last 지난달 · custom 직접(From ~ To, 둘 다 포함).</summary>
public sealed record GiftCostPeriod(string Key, DateOnly From, DateOnly To);
public sealed record GiftCostKind(string Key, string Label, int Count, long Amount);
public sealed record GiftCostDept(int TenantId, string Name, int Count, long Amount);
public sealed record GiftCostSummary(GiftCostPeriod Period, int Count, long Amount, IReadOnlyList<GiftCostKind> Kinds, IReadOnlyList<GiftCostDept> Depts);
/// <summary>받은 선물 한 줄. Dept = 부서 이름(플랫폼만), ClassName = 반(부서만).</summary>
/// <summary>화면 조각에 넘기는 값: 기간 고르기(Action = 주소, Keep = 함께 넘길 값) · 합계 카드(Label = 카드 제목).</summary>
public sealed record GiftCostPeriodView(GiftCostPeriod Period, string Action, IReadOnlyDictionary<string, string?> Keep);
public sealed record GiftCostSummaryView(GiftCostSummary Summary, string Label);
public sealed record GiftCostLine(long Id, string? Dept, DateTime DeliveredUtc, string Who, string? ClassName, string Kind, string ItemName, int? ItemId, bool HasImage, int Price);

/// <summary>
/// 총비용(ADR 0094 G-2 · 정한 것 13, R-294): **[줬어요]를 누른 날** 기준, 받은 선물(전달 완료)의 고를 때 가격 합.
/// 부서 관리자 = 자기 부서(부서 시간대), 플랫폼 관리자 = 모든 부서(서울 시간). 종류별 · 부서별, 엑셀(.xlsx)로 받기.
/// </summary>
public sealed class GiftCostService(IAppDb db, ISystemDb systemDb, TenantContext tenant, TenantClock clock)
{
    public const int PageSize = 20;
    public const string PlatformZone = "Asia/Seoul";

    /// <summary>종류별 줄(시안 순서). 새친구 · 초대는 한 줄.</summary>
    public static readonly IReadOnlyList<(string Key, string Label, string[] Kinds)> KindRows =
    [
        ("personal", "개인 선물", [GiftKinds.Personal]), ("family", "가족팀", [GiftKinds.Family]), ("friend", "친구팀", [GiftKinds.Friend]),
        ("birthday", "생일", [GiftKinds.Birthday]), ("newcomer", "새친구 · 초대", [GiftKinds.Newcomer, GiftKinds.Invite]), ("reading", "완독", [GiftKinds.Reading]),
    ];

    public static GiftCostPeriod Period(string? key, string? from, string? to, DateOnly today)
    {
        var month = new DateOnly(today.Year, today.Month, 1);
        switch (key)
        {
            case "month": return new("month", month, today);
            case "last": return new("last", month.AddMonths(-1), month.AddDays(-1));
            case "custom" when DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
                            && DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t):
                return f <= t ? new("custom", f, t) : new("custom", t, f);
            default: return new("year", new DateOnly(today.Year, 1, 1), today);
        }
    }

    /// <summary>기간의 시작 · 끝(끝은 다음 날 0시, 포함 안 함)을 UTC 로.</summary>
    private static (DateTime From, DateTime To) Utc(GiftCostPeriod p, string zoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        DateTime U(DateOnly d) => TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(TimeOnly.MinValue), zone);
        return (U(p.From), U(p.To.AddDays(1)));
    }

    private static IQueryable<Gift> Delivered(AppDbContext ctx, (DateTime From, DateTime To) r) =>
        ctx.Gifts.AsNoTracking().Where(g => g.State == GiftStates.Delivered && g.DeliveredUtc >= r.From && g.DeliveredUtc < r.To);

    private static string[]? KindsOf(string? key) => KindRows.FirstOrDefault(k => k.Key == key).Kinds;

    private static async Task<GiftCostSummary> SummaryAsync(AppDbContext ctx, GiftCostPeriod p, IQueryable<Gift> q, bool depts, CancellationToken ct)
    {
        var byKind = await q.GroupBy(g => g.Kind).Select(g => new { g.Key, N = g.Count(), Sum = g.Sum(x => (long)(x.ItemPrice ?? 0)) }).ToListAsync(ct);
        var kinds = KindRows.Select(k =>
        {
            var rows = byKind.Where(b => k.Kinds.Contains(b.Key)).ToList();
            return new GiftCostKind(k.Key, k.Label, rows.Sum(r => r.N), rows.Sum(r => r.Sum));
        }).ToList();
        var deptRows = new List<GiftCostDept>();
        if (depts)
        {
            var byTenant = await q.GroupBy(g => g.TenantId).Select(g => new { g.Key, N = g.Count(), Sum = g.Sum(x => (long)(x.ItemPrice ?? 0)) }).ToListAsync(ct);
            var ids = byTenant.Select(b => b.Key).ToList();
            var names = await ctx.Tenants.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            deptRows = byTenant.Select(b => new GiftCostDept(b.Key, names.GetValueOrDefault(b.Key) ?? b.Key.ToString(), b.N, b.Sum)).OrderByDescending(d => d.Amount).ThenBy(d => d.Name).ToList();
        }
        return new GiftCostSummary(p, kinds.Sum(k => k.Count), kinds.Sum(k => k.Amount), kinds, deptRows);
    }

    private static async Task<List<GiftCostLine>> LinesAsync(AppDbContext ctx, IQueryable<Gift> q, int skip, int take, bool dept, CancellationToken ct)
    {
        var gifts = await q.OrderByDescending(g => g.DeliveredUtc).ThenByDescending(g => g.Id).Skip(skip).Take(take).ToListAsync(ct);
        var rows = await GiftOrderService.RowsAsync(ctx, gifts, ct);
        var tenantIds = gifts.Select(g => g.TenantId).Distinct().ToList();
        var names = dept ? await ctx.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct) : [];
        return gifts.Zip(rows, (g, r) => new GiftCostLine(g.Id, dept ? names.GetValueOrDefault(g.TenantId) : null, g.DeliveredUtc!.Value, r.Who,
            dept ? null : r.ClassName, r.KindText, r.ItemName, r.ItemId, r.HasImage, r.Price)).ToList();   // 플랫폼은 반을 쓰지 않는다(부서마다 학기가 다름)
    }

    // ── 부서 관리자: 자기 부서 ──

    public DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    public async Task<(GiftCostSummary Summary, IReadOnlyList<GiftCostLine> Lines, bool More)> DeptAsync(GiftCostPeriod p, int take, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var q = Delivered(ctx, Utc(p, tenant.Current.TimeZoneId));
        var summary = await SummaryAsync(ctx, p, q, depts: false, ct);
        var lines = await LinesAsync(ctx, q, 0, take + 1, dept: false, ct);
        return (summary, lines.Take(take).ToList(), lines.Count > take);
    }

    public async Task<byte[]> DeptExcelAsync(GiftCostPeriod p, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var q = Delivered(ctx, Utc(p, tenant.Current.TimeZoneId));
        var summary = await SummaryAsync(ctx, p, q, depts: false, ct);
        return Excel(tenant.Current.Name, summary, await LinesAsync(ctx, q, 0, int.MaxValue, dept: false, ct), tenant.Current.TimeZoneId, platform: false);
    }

    // ── 플랫폼 관리자: 모든 부서(시스템 연결) ──

    public DateOnly PlatformToday => clock.Today(PlatformZone);

    public async Task<IReadOnlyList<(int Id, string Name)>> TenantsAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        return (await ctx.Tenants.AsNoTracking().Where(t => !t.IsLobby).OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync(ct)).Select(t => (t.Id, t.Name)).ToList();
    }

    private static IQueryable<Gift> Filter(IQueryable<Gift> q, int? tenantId, string? kind)
    {
        if (tenantId is int t) q = q.Where(g => g.TenantId == t);
        if (KindsOf(kind) is { } ks) q = q.Where(g => ks.Contains(g.Kind));
        return q;
    }

    public async Task<GiftCostSummary> PlatformAsync(string actor, GiftCostPeriod p, int? tenantId, string? kind, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        return await SummaryAsync(ctx, p, Filter(Delivered(ctx, Utc(p, PlatformZone)), tenantId, kind), depts: true, ct);
    }

    public async Task<byte[]> PlatformExcelAsync(string actor, GiftCostPeriod p, int? tenantId, string? kind, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var q = Filter(Delivered(ctx, Utc(p, PlatformZone)), tenantId, kind);
        var summary = await SummaryAsync(ctx, p, q, depts: true, ct);
        return Excel("모든 부서", summary, await LinesAsync(ctx, q, 0, int.MaxValue, dept: true, ct), PlatformZone, platform: true);
    }

    // ── 엑셀: 시트 ① 합계(종류별 · 플랫폼은 부서별) ② 받은 선물 한 줄씩 ──

    public static string FileName(string who, GiftCostPeriod p) => $"선물총비용_{who}_{p.From:yyyyMMdd}-{p.To:yyyyMMdd}.xlsx";

    private static byte[] Excel(string title, GiftCostSummary s, IReadOnlyList<GiftCostLine> lines, string zoneId, bool platform)
    {
        const string won = "#,##0\"원\"";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        using var wb = new XLWorkbook();

        var sum = wb.AddWorksheet("합계");
        sum.Cell(1, 1).Value = $"선물 총비용 · {title}";
        sum.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sum.Cell(2, 1).Value = $"{s.Period.From:yyyy.M.d} ~ {s.Period.To:yyyy.M.d} · [줬어요]를 누른 날 기준 · 받은 선물만";
        var r = 4;
        void Head(params string[] cols) { for (var i = 0; i < cols.Length; i++) sum.Cell(r, i + 1).Value = cols[i]; sum.Row(r).Style.Font.SetBold(); r++; }
        void Line(string name, int n, long amount, bool bold = false)
        {
            sum.Cell(r, 1).Value = name; sum.Cell(r, 2).Value = n; sum.Cell(r, 3).Value = amount;
            sum.Cell(r, 3).Style.NumberFormat.Format = won;
            if (bold) sum.Row(r).Style.Font.SetBold();
            r++;
        }
        Head("종류", "건수", "금액");
        foreach (var k in s.Kinds) Line(k.Label, k.Count, k.Amount);
        Line("합계", s.Count, s.Amount, bold: true);
        if (platform)
        {
            r++;
            Head("부서", "건수", "금액");
            foreach (var d in s.Depts) Line(d.Name, d.Count, d.Amount);
            Line("합계", s.Count, s.Amount, bold: true);
        }
        sum.Columns(1, 3).AdjustToContents();

        var ws = wb.AddWorksheet("받은 선물");
        string[] cols = platform ? ["부서", "받은 날", "받는 사람", "종류", "상품", "금액"] : ["받은 날", "받는 사람", "반", "종류", "상품", "금액"];
        for (var i = 0; i < cols.Length; i++) ws.Cell(1, i + 1).Value = cols[i];
        ws.Row(1).Style.Font.SetBold();
        var row = 2;
        foreach (var l in lines)
        {
            var c = 1;
            if (platform) ws.Cell(row, c++).Value = l.Dept ?? "";
            ws.Cell(row, c).Value = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(l.DeliveredUtc, DateTimeKind.Utc), zone);
            ws.Cell(row, c++).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            ws.Cell(row, c++).Value = l.Who;
            if (!platform) ws.Cell(row, c++).Value = l.ClassName ?? "";
            ws.Cell(row, c++).Value = l.Kind;
            ws.Cell(row, c++).Value = l.ItemName;
            ws.Cell(row, c).Value = l.Price;
            ws.Cell(row, c).Style.NumberFormat.Format = won;
            row++;
        }
        ws.Cell(row, cols.Length - 1).Value = "합계";
        ws.Cell(row, cols.Length).Value = lines.Sum(l => (long)l.Price);
        ws.Cell(row, cols.Length).Style.NumberFormat.Format = won;
        ws.Row(row).Style.Font.SetBold();
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, cols.Length).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
