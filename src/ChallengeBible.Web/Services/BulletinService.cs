using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>주보 목록 한 줄: 번호 · 날짜 · 제목 · 사진 수 · PDF 수.</summary>
public sealed record BulletinRow(int Id, DateOnly Date, string? Title, int Photos, int Pdfs);

/// <summary>주보 한 건과 파일(차례대로).</summary>
public sealed record BulletinView(Bulletin Item, IReadOnlyList<BulletinFile> Files);

/// <summary>
/// 부서 주보(ADR 0117, R-338, 2026-11-20 확정 시안 주보): 날짜 · 제목 · 내용 + 사진(JPG · PNG) · PDF 여러 개.
/// 누구나 보고, 그 부서 교사 · 관리자가 올리고 고치고 지운다. 부서마다 완전히 따로 — 표는 RLS + 전역 필터, 파일은 부서 폴더
/// (설정 Bulletins:Dir, 없으면 App_Data/bulletins)/{TenantId}/ 에 GUID 이름으로 두고 이 서비스로만 연다(wwwroot 밖이라 주소로 바로 못 연다).
/// </summary>
public sealed class BulletinService(IAppDb db, TenantContext tenant, TimeProvider time, IConfiguration config, IWebHostEnvironment env)
{
    public const string LogAction = "Bulletin";
    public const int MaxFiles = 10;
    public const long MaxBytes = 10 * 1024 * 1024;

    private string Root => config["Bulletins:Dir"] is { Length: > 0 } d ? d : Path.Combine(env.ContentRootPath, "App_Data", "bulletins");
    private string Dir => Path.Combine(Root, tenant.Current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>올리고 고칠 수 있나: 그 부서 교사 · 관리자(2026-11-20 사용자 "관리자 + 교사").</summary>
    public bool CanEdit => !tenant.IsLobby && tenant.IsStaff;

    public static bool IsPdf(BulletinFile f) => f.ContentType == "application/pdf";

    /// <summary>지난 주보까지 날짜 최근 순.</summary>
    public async Task<IReadOnlyList<BulletinRow>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var rows = await ctx.Bulletins.AsNoTracking().OrderByDescending(b => b.Date).ThenByDescending(b => b.Id).Select(b => new { b.Id, b.Date, b.Title }).ToListAsync(ct);
        var files = await ctx.BulletinFiles.AsNoTracking().Select(f => new { f.BulletinId, f.ContentType }).ToListAsync(ct);
        return rows.Select(b => new BulletinRow(b.Id, b.Date, b.Title,
            files.Count(f => f.BulletinId == b.Id && f.ContentType != "application/pdf"), files.Count(f => f.BulletinId == b.Id && f.ContentType == "application/pdf"))).ToList();
    }

    /// <summary>가장 최근 주보 날짜(부서 홈 [주보 보기] 버튼). 없으면 null.</summary>
    public async Task<DateOnly?> LatestDateAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby) return null;
        await using var ctx = db.Create();
        return await ctx.Bulletins.AsNoTracking().OrderByDescending(b => b.Date).ThenByDescending(b => b.Id).Select(b => (DateOnly?)b.Date).FirstOrDefaultAsync(ct);
    }

    /// <summary>주보 한 건(id 가 없으면 가장 최근). 없으면 null.</summary>
    public async Task<BulletinView?> GetAsync(int? id, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var q = ctx.Bulletins.AsNoTracking();
        var b = id is int i ? await q.SingleOrDefaultAsync(x => x.Id == i, ct) : await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (b is null) return null;
        var files = await ctx.BulletinFiles.AsNoTracking().Where(f => f.BulletinId == b.Id).OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync(ct);
        return new(b, files);
    }

    /// <summary>
    /// 올리기 · 고치기: 날짜 · 제목 · 내용 + 새 파일(여러 개) − 뺄 파일. 틀리면 안내 문장(아무것도 저장 안 함).
    /// 파일은 내용으로 종류를 확인한다(JPG · PNG · PDF만, 하나 10MB, 한 주보에 10개까지).
    /// </summary>
    public async Task<(int? Id, string? Error)> SaveAsync(string actor, int? id, DateOnly? date, string? title, string? body,
        IReadOnlyList<IFormFile> files, IReadOnlyCollection<int> remove, CancellationToken ct = default)
    {
        if (!CanEdit) throw new UnauthorizedAccessException("교사 · 관리자만 주보를 올릴 수 있어요.");
        if (date is not { } d) return (null, "날짜를 골라 주세요.");
        title = Clean(title, 100);
        body = Clean(body, 4000);
        var incoming = files.Where(f => f.Length > 0).ToList();
        if (incoming.Count > MaxFiles) return (null, $"파일은 한 번에 {MaxFiles}개까지 올릴 수 있어요.");
        var read = new List<(IFormFile File, byte[] Bytes, string Ext, string Type)>();
        foreach (var f in incoming)
        {
            if (f.Length > MaxBytes) return (null, $"\"{f.FileName}\"이(가) 너무 커요(파일마다 10MB까지).");
            var bytes = new byte[f.Length];
            await using (var s = f.OpenReadStream()) await s.ReadExactlyAsync(bytes, ct);
            if (Kind(bytes) is not { } k) return (null, $"\"{f.FileName}\"은(는) 올릴 수 없어요. 사진(JPG · PNG)과 PDF만 돼요.");
            read.Add((f, bytes, k.Ext, k.Type));
        }

        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        Bulletin b;
        if (id is int i)
        {
            b = await ctx.Bulletins.SingleOrDefaultAsync(x => x.Id == i, ct) ?? throw new KeyNotFoundException();
        }
        else
        {
            b = new Bulletin { TenantId = tenant.Current.Id, CreatedUtc = now, CreatedByUserName = actor };
            ctx.Bulletins.Add(b);
        }
        var have = id is null ? [] : await ctx.BulletinFiles.Where(f => f.BulletinId == b.Id).ToListAsync(ct);
        var gone = have.Where(f => remove.Contains(f.Id)).ToList();
        if (have.Count - gone.Count + read.Count > MaxFiles) return (null, $"한 주보에 파일은 {MaxFiles}개까지예요.");
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body) && have.Count - gone.Count + read.Count == 0) return (null, "제목 · 내용 · 파일 중 하나는 넣어 주세요.");

        (b.Date, b.Title, b.Body, b.UpdatedUtc, b.UpdatedByUserName) = (d, title, body, now, actor);
        ctx.BulletinFiles.RemoveRange(gone);
        await ctx.SaveChangesAsync(ct);   // 새 주보 번호

        Directory.CreateDirectory(Dir);
        var order = have.Except(gone).Select(f => f.SortOrder).DefaultIfEmpty(0).Max();
        foreach (var (f, bytes, ext, type) in read)
        {
            var stored = $"{Guid.NewGuid():N}.{ext}";
            await File.WriteAllBytesAsync(Path.Combine(Dir, stored), bytes, ct);
            ctx.BulletinFiles.Add(new BulletinFile
            {
                TenantId = tenant.Current.Id, BulletinId = b.Id, FileName = Clean(Path.GetFileName(f.FileName), 200) ?? stored,
                StoredName = stored, ContentType = type, Size = bytes.LongLength, SortOrder = ++order,
            });
        }
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = LogAction, TenantId = tenant.Current.Id,
            Detail = $"{(id is null ? "올림" : "고침")} {d:yyyy-MM-dd} · 파일 +{read.Count} −{gone.Count}" });
        await ctx.SaveChangesAsync(ct);
        foreach (var g in gone) DeleteFile(g.StoredName);
        return (b.Id, null);
    }

    /// <summary>주보 지우기(파일도). 교사 · 관리자만.</summary>
    public async Task<bool> DeleteAsync(string actor, int id, CancellationToken ct = default)
    {
        if (!CanEdit) throw new UnauthorizedAccessException();
        await using var ctx = db.Create();
        var b = await ctx.Bulletins.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return false;
        var files = await ctx.BulletinFiles.Where(f => f.BulletinId == id).ToListAsync(ct);
        ctx.BulletinFiles.RemoveRange(files);
        ctx.Bulletins.Remove(b);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = LogAction, TenantId = tenant.Current.Id, Detail = $"지움 {b.Date:yyyy-MM-dd}" });
        await ctx.SaveChangesAsync(ct);
        foreach (var f in files) DeleteFile(f.StoredName);
        return true;
    }

    /// <summary>파일 열기: 이 부서의 파일 줄일 때만(다른 부서 번호면 null — RLS · 전역 필터가 못 보게 한다).</summary>
    public async Task<(string Path, string Type, string Name)?> OpenAsync(int fileId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var f = await ctx.BulletinFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId, ct);
        if (f is null || f.TenantId != tenant.Current.Id) return null;
        var path = Path.Combine(Dir, f.StoredName);
        return File.Exists(path) ? (path, f.ContentType, f.FileName) : null;
    }

    private void DeleteFile(string stored)
    {
        var p = Path.Combine(Dir, stored);
        if (File.Exists(p)) File.Delete(p);
    }

    private static string? Clean(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim() is var t && t.Length > max ? t[..max] : s.Trim();

    /// <summary>파일 앞부분으로 종류를 본다(이름 · 브라우저가 말한 종류는 믿지 않는다).</summary>
    public static (string Ext, string Type)? Kind(byte[] b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? ("jpg", "image/jpeg")
        : b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? ("png", "image/png")
        : b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D ? ("pdf", "application/pdf")
        : null;
}
