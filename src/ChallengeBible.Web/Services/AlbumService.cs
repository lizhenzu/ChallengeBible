using System.IO.Compression;
using System.Security.Cryptography;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ChallengeBible.Web.Services;

/// <summary>보는 사람: 로그인 아이디(없으면 손님) · 플랫폼 관리자인가.</summary>
public sealed record AlbumViewer(string? UserName, bool PlatformAdmin)
{
    public bool LoggedIn => UserName is not null;
}

/// <summary>앨범 목록 한 줄: 대표 사진(썸네일 번호) · 사진 수.</summary>
public sealed record AlbumCard(int Id, string Title, DateOnly? EventDate, string Visibility, int Photos, int? CoverPhotoId);

/// <summary>앨범 한 개와 사진들 · 이 사람이 할 수 있는 것.</summary>
public sealed record AlbumPage(Album Album, IReadOnlyList<Photo> Photos, bool CanUpload, bool CanManage, bool CanDownload, Func<Photo, bool> CanDelete);

/// <summary>올린 결과: 성공이면 사진 번호, 아니면 사유.</summary>
public sealed record UploadResult(int? PhotoId, string? Error);

/// <summary>
/// 부서 사진첩(ADR 0118, R-339, 2026-11-20 확정 시안 사진첩): 앨범(1) : 사진(N), 사진만(JPG · PNG · WEBP · GIF, 한 장 20MB), 부서마다 5GB.
/// 부서마다 완전히 따로 — 표는 RLS + 전역 필터, 파일은 Albums:Dir(없으면 App_Data/albums)/{TenantId}/ 에 GUID 이름(원본 + 썸네일 t_*.jpg)으로 두고
/// 이 서비스로만 연다. 보기는 앨범 공개 범위(누구나 · 로그인 · 부서 · 관리자), 내려받기는 볼 수 있는 사람(앨범에서 끌 수 있음).
/// 앨범 만들기 · 고치기 · 지우기는 부서 관리자, 사진 올리기는 교사도, 지우기는 교사는 본인이 올린 것만(관리자는 모두). 플랫폼 관리자는 모든 부서.
/// </summary>
public sealed class AlbumService(IAppDb db, TenantContext tenant, TimeProvider time, IConfiguration config, IWebHostEnvironment env)
{
    public const string LogAction = "Album";
    public const long MaxBytes = 20 * 1024 * 1024;
    public const int MaxPixels = 12000;
    public const int ThumbSize = 480;
    public long QuotaBytes => (config.GetValue<long?>("Albums:QuotaMb") ?? 5120) * 1024 * 1024;

    private string Root => config["Albums:Dir"] is { Length: > 0 } d ? d : Path.Combine(env.ContentRootPath, "App_Data", "albums");
    private string Dir => Path.Combine(Root, tenant.Current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    // ── 권한 ──
    public bool IsManager(AlbumViewer v) => v.PlatformAdmin || (v.LoggedIn && tenant.IsAdmin);
    public bool IsUploader(AlbumViewer v) => v.PlatformAdmin || (v.LoggedIn && tenant.IsStaff);

    public bool CanSee(Album a, AlbumViewer v) => a.Visibility switch
    {
        AlbumVisibility.Public => true,
        AlbumVisibility.Login => v.LoggedIn,
        AlbumVisibility.Dept => v.PlatformAdmin || (v.LoggedIn && tenant.IsApproved),
        _ => IsManager(v),
    };

    public bool CanDownload(Album a, AlbumViewer v) => CanSee(a, v) && (a.AllowDownload || IsManager(v));

    public bool CanDeletePhoto(Photo p, AlbumViewer v) => IsManager(v) || (IsUploader(v) && p.UploadedByUserName == v.UserName);

    public static string VisibilityLabel(string v) => v switch
    {
        AlbumVisibility.Public => "누구나", AlbumVisibility.Login => "로그인한 사람 모두", AlbumVisibility.Dept => "부서 사람만", _ => "관리자만",
    };

    // ── 보기 ──

    /// <summary>볼 수 있는 앨범(q 가 있으면 제목 · 설명에 든 것). sort = new(촬영일 최신) · old · title.</summary>
    public async Task<IReadOnlyList<AlbumCard>> ListAsync(AlbumViewer v, string? q = null, string? sort = null, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var albums = await ctx.Albums.AsNoTracking().ToListAsync(ct);
        var counts = await ctx.Photos.AsNoTracking().GroupBy(p => p.AlbumId).Select(g => new { g.Key, N = g.Count(), First = g.Min(p => p.Id) }).ToListAsync(ct);
        q = q?.Trim();
        var list = albums.Where(a => CanSee(a, v))
            .Where(a => string.IsNullOrEmpty(q) || a.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || (a.Description ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                        || (a.EventDate?.ToString("yyyy.MM.dd") ?? "").Contains(q))
            .Select(a => { var c = counts.FirstOrDefault(x => x.Key == a.Id); return new AlbumCard(a.Id, a.Title, a.EventDate, a.Visibility, c?.N ?? 0, a.CoverPhotoId ?? c?.First); });
        return (sort switch
        {
            "old" => list.OrderBy(a => a.EventDate ?? DateOnly.MaxValue).ThenBy(a => a.Id),
            "title" => list.OrderBy(a => a.Title, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false)),
            _ => list.OrderByDescending(a => a.EventDate ?? DateOnly.MinValue).ThenByDescending(a => a.Id),
        }).ToList();
    }

    /// <summary>앨범 하나(볼 수 없으면 null — 없는 것처럼).</summary>
    public async Task<AlbumPage?> GetAsync(int id, AlbumViewer v, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var a = await ctx.Albums.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null || !CanSee(a, v)) return null;
        var photos = await ctx.Photos.AsNoTracking().Where(p => p.AlbumId == id).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync(ct);
        return new(a, photos, IsUploader(v), IsManager(v), CanDownload(a, v), p => CanDeletePhoto(p, v));
    }

    /// <summary>이 부서가 쓴 용량(바이트).</summary>
    public async Task<long> UsedAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Photos.AsNoTracking().SumAsync(p => (long?)p.Size, ct) ?? 0;
    }

    /// <summary>사진 파일 열기: 볼 수 있는 앨범의 이 부서 사진만. thumb 이면 썸네일, download 면 내려받기 허용일 때만.</summary>
    public async Task<(string Path, string Type, string Name)?> OpenAsync(int photoId, bool thumb, bool download, AlbumViewer v, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var p = await ctx.Photos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == photoId, ct);
        if (p is null || p.TenantId != tenant.Current.Id) return null;
        var a = await ctx.Albums.AsNoTracking().SingleOrDefaultAsync(x => x.Id == p.AlbumId, ct);
        if (a is null || !CanSee(a, v) || (download && !CanDownload(a, v))) return null;
        var path = Path.Combine(Dir, thumb ? p.ThumbName : p.StoredName);
        return File.Exists(path) ? (path, thumb ? "image/jpeg" : p.ContentType, p.FileName) : null;
    }

    /// <summary>ZIP 으로 내려받기(ids 가 비면 앨범 전체): 메모리에 모으지 않고 바로 써 내려간다. 같은 이름은 (2) · (3) 을 붙인다.</summary>
    public async Task<bool> ZipAsync(int albumId, IReadOnlyCollection<int> ids, AlbumViewer v, Stream output, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var a = await ctx.Albums.AsNoTracking().SingleOrDefaultAsync(x => x.Id == albumId, ct);
        if (a is null || !CanDownload(a, v)) return false;
        var photos = await ctx.Photos.AsNoTracking().Where(p => p.AlbumId == albumId && (ids.Count == 0 || ids.Contains(p.Id))).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync(ct);
        if (photos.Count == 0) return false;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var p in photos)
        {
            var path = Path.Combine(Dir, p.StoredName);
            if (!File.Exists(path)) continue;
            var name = p.FileName;
            for (var n = 2; !used.Add(name); n++) name = $"{Path.GetFileNameWithoutExtension(p.FileName)} ({n}){Path.GetExtension(p.FileName)}";
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);   // 사진은 이미 압축돼 있다
            await using var es = entry.Open();
            await using var fs = File.OpenRead(path);
            await fs.CopyToAsync(es, ct);
        }
        return true;
    }

    // ── 앨범 관리(부서 관리자) ──

    public async Task<(int? Id, string? Error)> SaveAlbumAsync(AlbumViewer v, int? id, string? title, string? description, DateOnly? eventDate, string? visibility, bool allowDownload, CancellationToken ct = default)
    {
        if (!IsManager(v)) throw new UnauthorizedAccessException("부서 관리자만 앨범을 만들고 고쳐요.");
        title = title?.Trim();
        if (string.IsNullOrEmpty(title)) return (null, "앨범 이름을 넣어 주세요.");
        if (title.Length > 100) return (null, "앨범 이름은 100자까지예요.");
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim() is var d && d.Length > 2000 ? d[..2000] : description.Trim();
        if (!AlbumVisibility.All.Contains(visibility)) visibility = AlbumVisibility.Login;
        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        Album a;
        if (id is int i) a = await ctx.Albums.SingleOrDefaultAsync(x => x.Id == i, ct) ?? throw new KeyNotFoundException();
        else { a = new Album { TenantId = tenant.Current.Id, Title = title, CreatedUtc = now, CreatedByUserName = v.UserName }; ctx.Albums.Add(a); }
        (a.Title, a.Description, a.EventDate, a.Visibility, a.AllowDownload, a.UpdatedUtc, a.UpdatedByUserName) = (title, description, eventDate, visibility!, allowDownload, now, v.UserName);
        await ctx.SaveChangesAsync(ct);
        Log(ctx, v, $"{(id is null ? "앨범 만듦" : "앨범 고침")} {a.Title} · {VisibilityLabel(a.Visibility)}{(a.AllowDownload ? "" : " · 내려받기 막음")}");
        await ctx.SaveChangesAsync(ct);
        return (a.Id, null);
    }

    /// <summary>앨범 지우기(사진 · 파일 함께). 지운 사진 수를 돌려준다(없으면 -1).</summary>
    public async Task<int> DeleteAlbumAsync(AlbumViewer v, int id, CancellationToken ct = default)
    {
        if (!IsManager(v)) throw new UnauthorizedAccessException();
        await using var ctx = db.Create();
        var a = await ctx.Albums.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return -1;
        var photos = await ctx.Photos.Where(p => p.AlbumId == id).ToListAsync(ct);
        ctx.Photos.RemoveRange(photos);
        await ctx.SaveChangesAsync(ct);
        ctx.Albums.Remove(a);
        Log(ctx, v, $"앨범 지움 {a.Title} · 사진 {photos.Count}");
        await ctx.SaveChangesAsync(ct);
        foreach (var p in photos) DeleteFiles(p);
        return photos.Count;
    }

    /// <summary>대표 사진 정하기(부서 관리자).</summary>
    public async Task<bool> SetCoverAsync(AlbumViewer v, int albumId, int photoId, CancellationToken ct = default)
    {
        if (!IsManager(v)) throw new UnauthorizedAccessException();
        await using var ctx = db.Create();
        var a = await ctx.Albums.SingleOrDefaultAsync(x => x.Id == albumId, ct);
        if (a is null || !await ctx.Photos.AnyAsync(p => p.Id == photoId && p.AlbumId == albumId, ct)) return false;
        a.CoverPhotoId = photoId;
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    // ── 사진 ──

    /// <summary>
    /// 사진 한 장 올리기(화면은 한 장씩 차례로 보내 진행률 · 실패만 다시를 한다). 내용으로 종류를 보고(JPG · PNG · WEBP · GIF),
    /// 20MB · 가로세로 12,000 · 부서 용량 · 같은 앨범의 같은 사진을 막는다. 원본은 그대로, 썸네일은 EXIF 회전을 반영해 480px JPEG.
    /// </summary>
    public async Task<UploadResult> UploadAsync(AlbumViewer v, int albumId, IFormFile? file, CancellationToken ct = default)
    {
        if (!IsUploader(v)) return new(null, "교사 · 관리자만 사진을 올릴 수 있어요.");
        if (file is null || file.Length == 0) return new(null, "빈 파일이에요.");
        if (file.Length > MaxBytes) return new(null, "20MB 를 넘어요.");
        var bytes = new byte[file.Length];
        await using (var s = file.OpenReadStream()) await s.ReadExactlyAsync(bytes, ct);
        var (ext, type) = Kind(bytes) is { } k ? k : (null, null);
        if (ext is null) return new(null, "사진 파일이 아니에요(JPG · PNG · WEBP · GIF).");

        await using var ctx = db.Create();
        if (!await ctx.Albums.AnyAsync(a => a.Id == albumId, ct)) return new(null, "앨범이 없어요.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (await ctx.Photos.AnyAsync(p => p.AlbumId == albumId && p.Hash == hash, ct)) return new(null, "이미 이 앨범에 있는 사진이에요.");
        var used = await ctx.Photos.SumAsync(p => (long?)p.Size, ct) ?? 0;
        if (used + bytes.LongLength > QuotaBytes) return new(null, $"부서 사진 용량({QuotaBytes / 1024 / 1024 / 1024}GB)이 가득 찼어요. 플랫폼 관리자에게 알려 주세요.");

        byte[] thumb; int w, h;
        try
        {
            var info = Image.Identify(bytes);
            if (info.Width > MaxPixels || info.Height > MaxPixels) return new(null, "사진이 너무 커요(가로 · 세로 12,000 픽셀까지).");
            using var img = Image.Load(bytes);
            img.Mutate(x => x.AutoOrient());
            (w, h) = (img.Width, img.Height);
            img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(ThumbSize, ThumbSize), Mode = ResizeMode.Max }));
            using var ms = new MemoryStream();
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = 80 });
            thumb = ms.ToArray();
        }
        catch (Exception e) when (e is ImageFormatException or InvalidImageContentException or UnknownImageFormatException)
        {
            return new(null, "사진을 읽을 수 없어요(깨진 파일).");
        }

        Directory.CreateDirectory(Dir);
        var g = Guid.NewGuid().ToString("N");
        var stored = $"{g}.{ext}";
        var thumbName = $"t_{g}.jpg";
        await File.WriteAllBytesAsync(Path.Combine(Dir, stored), bytes, ct);
        await File.WriteAllBytesAsync(Path.Combine(Dir, thumbName), thumb, ct);
        var order = (await ctx.Photos.Where(p => p.AlbumId == albumId).MaxAsync(p => (int?)p.SortOrder, ct) ?? 0) + 1;
        var photo = new Photo
        {
            TenantId = tenant.Current.Id, AlbumId = albumId, FileName = Clean(file.FileName) ?? stored, StoredName = stored, ThumbName = thumbName,
            ContentType = type!, Size = bytes.LongLength, Width = w, Height = h, Hash = hash, SortOrder = order,
            UploadedByUserName = v.UserName, UploadedUtc = time.GetUtcNow().UtcDateTime,
        };
        ctx.Photos.Add(photo);
        try { await ctx.SaveChangesAsync(ct); }
        catch { File.Delete(Path.Combine(Dir, stored)); File.Delete(Path.Combine(Dir, thumbName)); throw; }
        return new(photo.Id, null);
    }

    /// <summary>사진 지우기(하나 · 여러 개): 지울 수 있는 것만 지우고 지운 수를 돌려준다(교사는 본인이 올린 것만).</summary>
    public async Task<int> DeletePhotosAsync(AlbumViewer v, int albumId, IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (!IsUploader(v) || ids.Count == 0) return 0;
        await using var ctx = db.Create();
        var photos = (await ctx.Photos.Where(p => p.AlbumId == albumId && ids.Contains(p.Id)).ToListAsync(ct)).Where(p => CanDeletePhoto(p, v)).ToList();
        if (photos.Count == 0) return 0;
        var a = await ctx.Albums.SingleAsync(x => x.Id == albumId, ct);
        if (a.CoverPhotoId is int c && photos.Any(p => p.Id == c)) a.CoverPhotoId = null;
        ctx.Photos.RemoveRange(photos);
        Log(ctx, v, $"사진 지움 {a.Title} · {photos.Count}장");
        await ctx.SaveChangesAsync(ct);
        foreach (var p in photos) DeleteFiles(p);
        return photos.Count;
    }

    private void Log(AppDbContext ctx, AlbumViewer v, string detail) =>
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = v.UserName ?? "?", Action = LogAction, TenantId = tenant.Current.Id, Detail = detail });

    private void DeleteFiles(Photo p)
    {
        foreach (var n in new[] { p.StoredName, p.ThumbName })
        {
            var f = Path.Combine(Dir, n);
            if (File.Exists(f)) File.Delete(f);
        }
    }

    private static string? Clean(string? name)
    {
        var n = Path.GetFileName(name ?? "").Trim();
        return n.Length == 0 ? null : n.Length > 200 ? n[^200..] : n;
    }

    /// <summary>파일 앞부분으로 종류를 본다(이름 · 브라우저가 말한 종류는 믿지 않는다).</summary>
    public static (string Ext, string Type)? Kind(byte[] b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? ("jpg", "image/jpeg")
        : b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? ("png", "image/png")
        : b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50 ? ("webp", "image/webp")
        : b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38 ? ("gif", "image/gif")
        : null;
}
