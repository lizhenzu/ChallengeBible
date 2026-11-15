using System.Globalization;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>부서 하나(명단 올리기 첫 화면): 반 수(새친구반 빼고) · 명부 수.</summary>
public sealed record RosterDept(int Id, string Name, string Short, int Classes, int People);

/// <summary>엑셀 한 줄(학생 또는 교사). Row = 엑셀 줄 번호. Class = 반 이름(학생: 새친구반 가능, 교사: 맡은 반 · 없으면 null). Officer = 임원 역할 이름.</summary>
public sealed record RosterLine(int Row, string Name, string? Class, byte? BirthMonth, byte? BirthDay, string? Phone1, string? Phone2, string? Officer);

/// <summary>고칠 곳: 탭(학생 · 교사) · 줄 번호 · 까닭. 이 줄은 넣지 않는다.</summary>
public sealed record RosterIssue(string Sheet, int Row, string Message);

/// <summary>미리 보기: 넣을 학생 · 교사(고칠 곳 줄은 빠짐) · 고칠 곳 · 지금 지울 것(명단 · 반 배정 · 출석) · 남는 관리자(계정 있는 명부 줄).</summary>
public sealed record RosterPlan(int TenantId, string TenantName, string FileName, IReadOnlyList<RosterLine> Students, IReadOnlyList<RosterLine> Teachers,
    IReadOnlyList<RosterIssue> Issues, int DropPeople, int DropMembers, int DropAttendance, IReadOnlyList<string> Kept);

public sealed record RosterResult(int Students, int Teachers, int Updated, int DroppedPeople, int DroppedAttendance);

/// <summary>
/// 명단 올리기(ADR 0097, 확정 시안 명단올리기 · 반편성-업로드-템플릿, 2026-11-15 사용자): 플랫폼 관리자가 부서마다 엑셀 하나(파일 이름 = 부서)를 올린다.
/// 학생 탭(반 · 이름 · 생일 · 전화번호 · 보호자 전화번호) · 교사 탭(이름 · 전화번호 · 생일 · 맡은 반 · 임원 역할).
/// 넣을 때 그 부서의 <b>명단(계정 없는 명부 줄)과 반 배정을 비우고</b>(그 사람들의 출석 · 새친구 선물도 함께, R-302) 엑셀대로 채운다.
/// 계정이 있는 명부 줄(부서 관리자 등)은 지우지 않고(CLAUDE.md "관리자는 절대 지우지 않는다"), 엑셀에 같은 이름이 있으면 그 줄의 생일 · 전화번호 · 반만 고친다.
/// 고칠 곳이 있는 줄만 빼고 넣는다(질문 1 → 1). 시스템 연결, 시작마다 플랫폼 관리자인지 DB 로 다시 확인. 한 트랜잭션.
/// </summary>
public sealed partial class RosterUploadService(ISystemDb systemDb, TimeProvider time)
{
    public const string Action = "RosterUpload";
    public const string StudentSheet = "학생";
    public const string TeacherSheet = "교사";
    private const string ListSheet = "목록";
    private const int MaxRows = 1000;
    private static readonly string[] NotOfficer = [UserRole.StudentCode, UserRole.TeacherCode, UserRole.TeacherSCode, ClassService.NewcomerCode, UserRole.EtcCode];

    private async Task<AppDbContext> OpenAsync(string actor, CancellationToken ct)
    {
        var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct))
        {
            await ctx.DisposeAsync();
            throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        }
        return ctx;
    }

    /// <summary>부서 이름의 마지막 낱말(잠실새내교회 초등부 → 초등부). 파일 이름 · 템플릿 이름에 쓴다.</summary>
    public static string ShortName(string name) => name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } w ? w[^1] : name;

    public async Task<IReadOnlyList<RosterDept>> DeptsAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var tenants = await ctx.Tenants.AsNoTracking().Where(t => !t.IsLobby && t.Status == TenantStatus.Active).Select(t => new { t.Id, t.Name }).ToListAsync(ct);
        var firstTerms = await ctx.ClassTerms.AsNoTracking().GroupBy(t => t.TenantId).Select(g => new { g.Key, Id = g.Min(x => x.Id) }).ToListAsync(ct);
        var termIds = firstTerms.Select(t => t.Id).ToList();
        var classes = await ctx.Classes.AsNoTracking().Where(c => termIds.Contains(c.TermId) && !c.IsNewcomer).GroupBy(c => c.TenantId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var people = await ctx.People.AsNoTracking().GroupBy(p => p.TenantId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return tenants.OrderBy(t => DeptOrder.Key(t.Name)).ThenBy(t => t.Id)
            .Select(t => new RosterDept(t.Id, t.Name, ShortName(t.Name), classes.GetValueOrDefault(t.Id), people.GetValueOrDefault(t.Id))).ToList();
    }

    /// <summary>부서의 첫 반 묶음(없으면 null) · 반 이름(새친구반 먼저, 나머지 이름 순).</summary>
    private static async Task<(int? TermId, List<SchoolClass> Classes)> ClassesAsync(AppDbContext ctx, int tenantId, CancellationToken ct)
    {
        var term = await ctx.ClassTerms.Where(t => t.TenantId == tenantId).OrderBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        if (term is null) return (null, []);
        var ko = StringComparer.Create(new CultureInfo("ko-KR"), false);
        var list = await ctx.Classes.Where(c => c.TenantId == tenantId && c.TermId == term).ToListAsync(ct);
        return (term, list.OrderByDescending(c => c.IsNewcomer).ThenBy(c => c.Name, ko).ToList());
    }

    /// <summary>부서의 임원 역할(이름 → 코드): 부서 역할 코드에서 학생 · 교사 · 교사(S) · 새친구 · 기타를 뺀 것(유년부와 같은 18개 기준).</summary>
    private static async Task<List<(string Code, string Name)>> OfficersAsync(AppDbContext ctx, int tenantId, CancellationToken ct)
    {
        var codes = await ctx.CodeData.AsNoTracking().Where(c => c.TenantId == tenantId && c.MajorCD == CodeData.RoleMajor)
            .OrderBy(c => c.DisplayOrder).Select(c => new { c.MinorCD, c.MinorNM }).ToListAsync(ct);
        var all = codes.Count > 0 ? codes.Select(c => (Code: (c.MinorCD ?? "").Trim(), Name: (c.MinorNM ?? "").Trim())).ToList() : UserRole.Codes.Select(c => (c.Code, c.Name)).ToList();
        return all.Where(c => !NotOfficer.Contains(c.Code) && c.Name.Length > 0).ToList();
    }

    /// <summary>템플릿(확정 시안 반편성-업로드-템플릿): 학생 · 교사 탭 + 숨긴 목록 탭(반 · 임원 역할), 반 · 역할은 목록에서 고른다. 파일 이름 = 부서 짧은 이름.xlsx.</summary>
    public async Task<(string FileName, byte[] Bytes)> TemplateAsync(string actor, int tenantId, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var tenant = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, ct);
        var (_, classes) = await ClassesAsync(ctx, tenantId, ct);
        var names = classes.Select(c => c.Name).ToList();
        if (!classes.Any(c => c.IsNewcomer)) names.Insert(0, ClassService.NewcomerName);
        var regular = names.Where(n => n != ClassService.NewcomerName).ToList();
        var officers = (await OfficersAsync(ctx, tenantId, ct)).Select(o => o.Name).ToList();

        using var wb = new XLWorkbook();
        var head = XLColor.FromHtml("#2B59C3");
        var opt = XLColor.FromHtml("#7A8AA8");
        void Sheet(IXLWorksheet ws, string[] cols, double[] widths, int[] optional, string note, string[][] examples)
        {
            ws.Range(1, 1, 1, cols.Length).Merge().Value = note;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#5B6470");
            ws.Cell(1, 1).Style.Font.FontSize = 10;
            ws.Cell(1, 1).Style.Alignment.WrapText = true;
            ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            ws.Row(1).Height = 70;
            for (var i = 0; i < cols.Length; i++)
            {
                var c = ws.Cell(2, i + 1);
                c.Value = cols[i];
                c.Style.Font.Bold = true;
                c.Style.Font.FontColor = XLColor.White;
                c.Style.Fill.BackgroundColor = optional.Contains(i + 1) ? opt : head;
                c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                c.Style.Alignment.WrapText = true;
                ws.Column(i + 1).Width = widths[i];
            }
            ws.Row(2).Height = 32;
            for (var r = 0; r < examples.Length; r++)
                for (var i = 0; i < examples[r].Length; i++)
                {
                    var c = ws.Cell(3 + r, i + 1);
                    c.Value = examples[r][i];
                    c.Style.Font.FontColor = XLColor.FromHtml("#9AA1AB");
                    c.Style.Font.Italic = true;
                }
            ws.Range(3, 1, 302, cols.Length).Style.NumberFormat.Format = "@";
            ws.SheetView.FreezeRows(2);
        }
        var shortName = ShortName(tenant.Name);
        var s = wb.AddWorksheet(StudentSheet);
        Sheet(s, ["반", "이름", "생일\n(월/일)", "전화번호", "보호자 전화번호\n(선택)"], [14, 12, 10, 16, 16], [5],
            $"{tenant.Name} · 학생 명단 — 파일 이름이 부서예요({shortName}.xlsx). 올리면 이 부서의 기존 명단과 반 배정을 비우고 이 내용으로 채워요(관리자는 그대로). 회색 '예)' 줄은 읽지 않아요. 반은 목록에서 고르세요(새친구는 '새친구반'). 생일은 월/일(예: 3/15). 회색 머리 칸은 비워도 돼요.",
            [["예) " + (regular.FirstOrDefault() ?? "1반"), "홍길동", "3/15", "010-1234-5678", "010-9876-5432"]]);
        var t = wb.AddWorksheet(TeacherSheet);
        Sheet(t, ["이름", "전화번호", "생일\n(월/일, 선택)", "맡은 반\n(담임, 선택)", "임원 역할"], [12, 16, 12, 14, 12], [3, 4],
            $"{tenant.Name} · 교사 명단 — 담임 선생님은 '맡은 반'을, 임원은 '임원 역할'을 고르세요. 임원도 반을 맡을 수 있어요(선택). 둘 다 비우면 반 없는 교사로 명부에만 들어가요. 회색 '예)' 줄은 읽지 않아요.",
            [["예) 김선생", "010-2345-6789", "5/20", regular.FirstOrDefault() ?? "", ""], ["예) 이부장", "010-3456-7890", "", "", officers.FirstOrDefault(o => o == "부장") ?? officers.FirstOrDefault() ?? ""]]);
        var l = wb.AddWorksheet(ListSheet);
        l.Cell(1, 1).Value = "반";
        l.Cell(1, 2).Value = "임원 역할";
        for (var i = 0; i < names.Count; i++) l.Cell(i + 2, 1).Value = names[i];
        for (var i = 0; i < officers.Count; i++) l.Cell(i + 2, 2).Value = officers[i];
        l.Visibility = XLWorksheetVisibility.Hidden;
        void List(IXLWorksheet ws, string range, string source)
        {
            var v = ws.Range(range).CreateDataValidation();
            v.List(source, true);
            v.IgnoreBlanks = true;
            v.ErrorMessage = "목록에서 골라 주세요.";
        }
        if (names.Count > 0) List(s, "A3:A302", $"'{ListSheet}'!$A$2:$A${names.Count + 1}");
        if (regular.Count > 0) List(t, "D3:D302", $"'{ListSheet}'!$A${names.Count - regular.Count + 2}:$A${names.Count + 1}");
        if (officers.Count > 0) List(t, "E3:E302", $"'{ListSheet}'!$B$2:$B${officers.Count + 1}");
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ($"{shortName}.xlsx", ms.ToArray());
    }

    /// <summary>파일 이름으로 부서를 찾는다: 이름(확장자 뺌)이 부서 이름에 들어 있거나, 부서의 짧은 이름(초등부)이 파일 이름에 들어 있는 부서가 꼭 하나일 때.</summary>
    public static int? MatchTenant(string fileName, IEnumerable<(int Id, string Name)> tenants)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? "").Trim();
        if (stem.Length == 0) return null;
        var hits = tenants.Where(t => t.Name.Contains(stem, StringComparison.OrdinalIgnoreCase) || stem.Contains(ShortName(t.Name), StringComparison.OrdinalIgnoreCase)).Select(t => t.Id).Distinct().ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    [GeneratedRegex(@"^\s*(\d{1,2})\s*(?:[/.\-]|월)\s*(\d{1,2})\s*일?\s*$")]
    private static partial Regex MonthDayRx();

    /// <summary>생일: 3/15 · 3.15 · 3-15 · 3월 15일 · 날짜(엑셀이 날짜로 바꾼 칸). 비면 (null, null, true), 못 읽으면 ok = false.</summary>
    public static (byte? Month, byte? Day, bool Ok) ParseBirthday(IXLCell cell)
    {
        if (cell.IsEmpty()) return (null, null, true);
        if (cell.DataType == XLDataType.DateTime) { var d = cell.GetDateTime(); return ((byte)d.Month, (byte)d.Day, true); }
        var text = cell.GetFormattedString().Trim();
        if (text.Length == 0) return (null, null, true);
        if (DateTime.TryParseExact(text, ["yyyy-MM-dd", "yyyy-M-d", "yyyy.M.d", "yyyy/M/d", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
            return ((byte)full.Month, (byte)full.Day, true);
        var m = MonthDayRx().Match(text);
        if (!m.Success) return (null, null, false);
        int mo = int.Parse(m.Groups[1].Value), da = int.Parse(m.Groups[2].Value);
        if (mo is < 1 or > 12 || da < 1 || da > DateTime.DaysInMonth(2024, mo)) return (null, null, false);   // 2024: 윤년이라 2/29 도 된다
        return ((byte)mo, (byte)da, true);
    }

    /// <summary>전화번호: 숫자만 남겨 010-1234-5678 꼴로. 비면 null, 숫자가 9 ~ 11개가 아니면 ok = false.</summary>
    public static (string? Phone, bool Ok) ParsePhone(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return (null, true);
        if (digits.Length == 10 && !digits.StartsWith("02") && digits[0] == '1') digits = "0" + digits;   // 엑셀이 앞 0 을 지운 경우(1012345678)
        return digits.Length switch
        {
            11 => ($"{digits[..3]}-{digits[3..7]}-{digits[7..]}", true),
            10 when digits.StartsWith("02") => ($"02-{digits[2..6]}-{digits[6..]}", true),
            10 => ($"{digits[..3]}-{digits[3..6]}-{digits[6..]}", true),
            9 when digits.StartsWith("02") => ($"02-{digits[2..5]}-{digits[5..]}", true),
            _ => (null, false),
        };
    }

    /// <summary>머리 줄(이름 칸이 있는 줄) 번호와 칸 번호(머리 글자의 첫 줄로 찾는다). 없으면 null.</summary>
    private static (int Row, Dictionary<string, int> Cols)? Header(IXLWorksheet ws)
    {
        for (var r = 1; r <= Math.Min(10, ws.LastRowUsed()?.RowNumber() ?? 0); r++)
        {
            var cols = new Dictionary<string, int>();
            foreach (var c in ws.Row(r).CellsUsed())
            {
                var key = c.GetString().Split('\n')[0].Replace(" ", "").Trim();
                if (key.Length > 0) cols.TryAdd(key, c.Address.ColumnNumber);
            }
            if (cols.ContainsKey("이름")) return (r, cols);
        }
        return null;
    }

    private static string Text(IXLWorksheet ws, int row, Dictionary<string, int> cols, string key) =>
        cols.TryGetValue(key, out var c) ? ws.Cell(row, c).GetFormattedString().Trim() : "";

    /// <summary>미리 보기: 파일 이름으로 부서를 찾고 두 탭을 읽어 줄마다 검사한다. 넣지 않는다. 오류(부서 · 파일)면 Error.</summary>
    public async Task<(RosterPlan? Plan, string? Error)> PreviewAsync(string actor, string fileName, Stream file, CancellationToken ct = default)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) return (null, "엑셀 파일(.xlsx)만 올릴 수 있어요.");
        await using var ctx = await OpenAsync(actor, ct);
        var tenants = await ctx.Tenants.AsNoTracking().Where(t => !t.IsLobby && t.Status == TenantStatus.Active).Select(t => new { t.Id, t.Name }).ToListAsync(ct);
        if (MatchTenant(fileName, tenants.Select(t => (t.Id, t.Name))) is not int tenantId)
            return (null, $"파일 이름 \"{Path.GetFileName(fileName)}\"로 부서를 정할 수 없어요. 부서 이름으로 파일 이름을 바꿔 주세요(예: {string.Join(" · ", tenants.OrderBy(t => DeptOrder.Key(t.Name)).Take(3).Select(t => ShortName(t.Name) + ".xlsx"))}).");
        var tenantName = tenants.Single(t => t.Id == tenantId).Name;

        XLWorkbook wb;
        try { wb = new XLWorkbook(file); }
        catch (Exception) { return (null, "엑셀 파일을 읽을 수 없어요. 템플릿으로 다시 만들어 주세요."); }
        using (wb)
        {
            if (!wb.TryGetWorksheet(StudentSheet, out var sws) || !wb.TryGetWorksheet(TeacherSheet, out var tws))
                return (null, "\"학생\" 탭과 \"교사\" 탭이 있어야 해요. 템플릿을 받아 채워 주세요.");
            var (_, classes) = await ClassesAsync(ctx, tenantId, ct);
            var classNames = classes.Select(c => c.Name).ToHashSet();
            classNames.Add(ClassService.NewcomerName);
            var officers = (await OfficersAsync(ctx, tenantId, ct)).Select(o => o.Name).ToHashSet();
            var issues = new List<RosterIssue>();
            var students = new List<RosterLine>();
            var teachers = new List<RosterLine>();

            foreach (var (ws, isStudent) in new[] { (sws, true), (tws, false) })
            {
                var sheet = isStudent ? StudentSheet : TeacherSheet;
                if (Header(ws) is not { } h) { issues.Add(new(sheet, 0, "머리 줄(이름 칸)을 찾을 수 없어요.")); continue; }
                var last = Math.Min(ws.LastRowUsed()?.RowNumber() ?? h.Row, h.Row + MaxRows);
                for (var r = h.Row + 1; r <= last; r++)
                {
                    var name = Text(ws, r, h.Cols, "이름");
                    var cls = Text(ws, r, h.Cols, isStudent ? "반" : "맡은반");
                    var first = ws.Row(r).CellsUsed().FirstOrDefault()?.GetFormattedString().Trim() ?? "";
                    if (first.StartsWith("예)", StringComparison.Ordinal)) continue;   // 회색 예 줄
                    var phone = Text(ws, r, h.Cols, "전화번호");
                    var phone2 = isStudent ? Text(ws, r, h.Cols, "보호자전화번호") : "";
                    var officer = isStudent ? "" : Text(ws, r, h.Cols, "임원역할");
                    if (name.Length == 0 && cls.Length == 0 && phone.Length == 0 && phone2.Length == 0 && officer.Length == 0) continue;   // 빈 줄

                    var why = new List<string>();
                    if (name.Length == 0) why.Add("이름이 비었어요");
                    else if (name.Length > 50) why.Add("이름이 너무 길어요(50자까지)");
                    if (isStudent && cls.Length == 0) why.Add("반이 비었어요");
                    else if (cls.Length > 0 && !classNames.Contains(cls)) why.Add($"반 \"{cls}\"이 {ShortName(tenantName)}에 없어요");
                    else if (!isStudent && cls == ClassService.NewcomerName) why.Add("맡은 반에 새친구반은 고를 수 없어요(새친구반 담당은 반 화면에서)");
                    if (officer.Length > 0 && !officers.Contains(officer)) why.Add($"임원 역할 \"{officer}\"이 목록에 없어요");
                    var (bm, bd, bOk) = h.Cols.TryGetValue("생일", out var bc) ? ParseBirthday(ws.Cell(r, bc)) : (null, null, true);
                    if (!bOk) why.Add($"생일 \"{ws.Cell(r, bc).GetFormattedString().Trim()}\"을 읽을 수 없어요(월/일, 예: 3/15)");
                    var (p1, p1Ok) = ParsePhone(phone);
                    if (!p1Ok) why.Add($"전화번호 \"{phone}\"을 읽을 수 없어요");
                    var (p2, p2Ok) = ParsePhone(phone2);
                    if (!p2Ok) why.Add($"보호자 전화번호 \"{phone2}\"을 읽을 수 없어요");
                    if (why.Count > 0) { issues.Add(new(sheet, r, string.Join(" · ", why))); continue; }
                    (isStudent ? students : teachers).Add(new(r, name, cls.Length > 0 ? cls : null, bm, bd, p1, p2, officer.Length > 0 ? officer : null));
                }
            }

            var drop = ctx.People.Where(p => p.TenantId == tenantId && p.UserId == null).Select(p => p.Id);
            var kept = await ctx.People.AsNoTracking().Where(p => p.TenantId == tenantId && p.UserId != null).Select(p => p.Name).ToListAsync(ct);
            return (new RosterPlan(tenantId, tenantName, Path.GetFileName(fileName), students, teachers, issues,
                await drop.CountAsync(ct),
                await ctx.ClassMembers.CountAsync(m => m.TenantId == tenantId, ct),
                await ctx.AttendanceChecks.CountAsync(a => a.TenantId == tenantId && drop.Contains(a.PersonId), ct),
                kept.Order().ToList()), null);
        }
    }

    private static async Task DeleteAsync<T>(AppDbContext ctx, IQueryable<T> q, CancellationToken ct) where T : class
    {
        if (ctx.Database.IsRelational()) await q.ExecuteDeleteAsync(ct);
        else { ctx.Set<T>().RemoveRange(await q.ToListAsync(ct)); await ctx.SaveChangesAsync(ct); }
    }

    /// <summary>
    /// 넣기(R-301 · R-302): 그 부서의 계정 없는 명부 줄(과 그 사람들의 출석 · 새친구 선물 · 신청)과 모든 반 배정을 지우고 미리 보기 내용대로 넣는다.
    /// 계정 있는 명부 줄은 남기고, 엑셀에 같은 이름이 있으면 생일 · 전화번호(비지 않은 칸만) · 반만 고친다(역할은 소속 화면에서). 반 묶음 · 새친구반이 없으면 만든다.
    /// </summary>
    public async Task<RosterResult> ApplyAsync(string actor, RosterPlan plan, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        var tid = plan.TenantId;
        var now = time.GetUtcNow().UtcDateTime;

        var dropIds = await ctx.People.Where(p => p.TenantId == tid && p.UserId == null).Select(p => p.Id).ToListAsync(ct);
        var attendance = await ctx.AttendanceChecks.CountAsync(a => a.TenantId == tid && dropIds.Contains(a.PersonId), ct);
        var gifts = ctx.Gifts.Where(g => g.TenantId == tid && g.PersonId != null && dropIds.Contains(g.PersonId.Value)).Select(g => g.Id);
        await DeleteAsync(ctx, ctx.GiftLogs.Where(l => l.TenantId == tid && gifts.Contains(l.GiftId)), ct);
        await DeleteAsync(ctx, ctx.GiftPointUses.Where(u => u.TenantId == tid && gifts.Contains(u.GiftId)), ct);
        await DeleteAsync(ctx, ctx.Gifts.Where(g => g.TenantId == tid && gifts.Contains(g.Id)), ct);
        await DeleteAsync(ctx, ctx.AttendanceChecks.Where(a => a.TenantId == tid && dropIds.Contains(a.PersonId)), ct);
        await DeleteAsync(ctx, ctx.ClassMembers.Where(m => m.TenantId == tid), ct);
        await DeleteAsync(ctx, ctx.SignupApplications.Where(s => s.TenantId == tid && dropIds.Contains(s.PersonId)), ct);
        await DeleteAsync(ctx, ctx.People.Where(p => p.TenantId == tid && dropIds.Contains(p.Id)), ct);

        // 반 묶음 · 새친구반(없으면 만든다, ClassService 와 같다)
        var term = await ctx.ClassTerms.Where(t => t.TenantId == tid).OrderBy(t => t.Id).FirstOrDefaultAsync(ct);
        if (term is null)
        {
            var tz = await ctx.Tenants.Where(t => t.Id == tid).Select(t => t.TimeZoneId).SingleAsync(ct);
            var today = ChallengeBible.Domain.Time.TenantClock.ToTenantDate(time.GetUtcNow(), tz);
            term = new ClassTerm { TenantId = tid, Year = today.Year, StartDate = today, CreatedUtc = now };
            ctx.ClassTerms.Add(term);
            await ctx.SaveChangesAsync(ct);
        }
        if (!await ctx.Classes.AnyAsync(c => c.TenantId == tid && c.TermId == term.Id && c.IsNewcomer, ct))
        {
            ctx.Classes.Add(new SchoolClass { TenantId = tid, TermId = term.Id, Name = ClassService.NewcomerName, IsNewcomer = true, CreatedUtc = now });
            await ctx.SaveChangesAsync(ct);
        }
        var classIds = await ctx.Classes.Where(c => c.TenantId == tid && c.TermId == term.Id && !c.IsNewcomer).ToDictionaryAsync(c => c.Name, c => c.Id, ct);
        var officerCodes = (await OfficersAsync(ctx, tid, ct)).GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First().Code);
        var linked = await ctx.People.Where(p => p.TenantId == tid && p.UserId != null).ToListAsync(ct);

        var placed = new HashSet<int>();
        var pending = new List<(Person P, int? ClassId, bool Teacher)>();
        int students = 0, teachers = 0, updated = 0;
        foreach (var (line, isStudent) in plan.Students.Select(l => (l, true)).Concat(plan.Teachers.Select(l => (l, false))))
        {
            int? classId = line.Class is { } cn && classIds.TryGetValue(cn, out var id) ? id : null;
            var same = linked.FirstOrDefault(p => p.Name == line.Name);
            if (same is not null)   // 계정 있는 명부 줄(관리자 등): 지우지 않고 빈 칸이 아닌 것만 고친다
            {
                if (line.BirthMonth is not null) { same.BirthMonth = line.BirthMonth; same.BirthDay = line.BirthDay; }
                if (line.Phone1 is not null) same.Phone1 = line.Phone1;
                if (line.Phone2 is not null) same.Phone2 = line.Phone2;
                pending.Add((same, classId, !isStudent));
                updated++;
                continue;
            }
            var role = isStudent
                ? (line.Class == ClassService.NewcomerName ? ClassService.NewcomerCode : UserRole.StudentCode)
                : (line.Officer is { } o && officerCodes.TryGetValue(o, out var oc) ? oc : UserRole.TeacherCode);
            var person = new Person
            {
                TenantId = tid, Name = line.Name, RoleCode = role, BirthMonth = line.BirthMonth, BirthDay = line.BirthDay,
                Phone1 = line.Phone1, Phone2 = line.Phone2, CreatedUtc = now, CreatedByUserName = actor,
            };
            ctx.People.Add(person);
            pending.Add((person, classId, !isStudent));
            if (isStudent) students++; else teachers++;
        }
        await ctx.SaveChangesAsync(ct);
        foreach (var (p, classId, teacher) in pending)
            if (classId is int cid && placed.Add(p.Id))   // 한 사람은 한 반에만(R-232)
                ctx.ClassMembers.Add(new ClassMember { TenantId = tid, TermId = term.Id, ClassId = cid, PersonId = p.Id, IsTeacher = teacher, CreatedUtc = now });

        var result = new RosterResult(students, teachers, updated, dropIds.Count, attendance);
        ctx.AuditLogs.Add(new AuditLog
        {
            OccurredUtc = now, ActorUserName = actor, TenantId = tid, Action = Action,
            Detail = $"file={plan.FileName};students={students};teachers={teachers};updated={updated};dropped={dropIds.Count};attendance={attendance};skipped={plan.Issues.Count}",
        });
        await ctx.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
}
