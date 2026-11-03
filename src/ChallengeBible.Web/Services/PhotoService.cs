using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 얼굴 사진(ADR 0078, R-200 ~ R-203). 계정마다 한 장, 파일은 `App_Data/photos`(설정 `Photos:Dir`), 이름은 `Account.PhotoPath`.
/// 보기 권한: 같은 부서에 승인된 사람끼리(R-201) — 부서 안 조회는 영역 필터가 이 부서로 좁힌다.
/// </summary>
public sealed class PhotoService(IAppDb db, IWebHostEnvironment env, IConfiguration config, TimeProvider time)
{
    public const int MaxBytes = 400 * 1024;
    public const string LogAction = "Photo";

    // 기본 = wwwroot/profile(2026-11-03 사용자, 나중에 사이트 밖으로 옮길 예정). `/profile/…` 주소는 Program.cs 앞단에서 막아
    // 권한을 보는 /{slug}/photo/{계정} 으로만 나간다.
    private string Dir => config["Photos:Dir"] is { Length: > 0 } d ? d : Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "profile");

    /// <summary>머리 바이트로 본 형식(JPEG · PNG · WebP), 아니면 null.</summary>
    public static string? Kind(ReadOnlySpan<byte> b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "jpg"
        : b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "png"
        : b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P' ? "webp"
        : null;

    public static string ContentType(string file) => Path.GetExtension(file) switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };

    /// <summary>계정의 사진 파일 이름(없으면 null).</summary>
    public async Task<string?> FileOfAsync(int accountId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Accounts.Where(a => a.Id == accountId).Select(a => a.PhotoPath).SingleOrDefaultAsync(ct);
    }

    /// <summary>파일 전체 경로(파일이 없으면 null). 이름에 경로 글자가 섞이면 받지 않는다.</summary>
    public string? FullPath(string? file)
    {
        if (string.IsNullOrEmpty(file) || file != Path.GetFileName(file)) return null;
        var p = Path.Combine(Dir, file);
        return File.Exists(p) ? p : null;
    }

    /// <summary>이 부서(요청의 영역)에 승인된 소속이 있는 계정의 사진 파일(R-201). 아니면 null.</summary>
    public async Task<string?> FileInTenantAsync(int accountId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var approved = await ctx.Memberships.AnyAsync(m => m.AccountId == accountId && m.Status == MembershipStatus.Approved, ct);
        return approved ? await ctx.Accounts.Where(a => a.Id == accountId).Select(a => a.PhotoPath).SingleOrDefaultAsync(ct) : null;
    }

    /// <summary>이 부서 소속의 계정(관리자 화면). 다른 부서 소속이면 null.</summary>
    public async Task<int?> AccountOfMembershipAsync(int membershipId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Memberships.Where(m => m.Id == membershipId).Select(m => (int?)m.AccountId).SingleOrDefaultAsync(ct);
    }

    /// <summary>올리기 · 바꾸기. 틀리면 안내 문장. tenantId 를 주면(관리자) 바꾼 기록에 남긴다(R-203).</summary>
    public async Task<string?> SaveAsync(int accountId, IFormFile? file, int actor, int? tenantId = null, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return "사진을 골라 주세요.";
        if (file.Length > MaxBytes) return $"사진이 너무 커요({MaxBytes / 1024}KB 까지).";
        var bytes = new byte[file.Length];
        await using (var s = file.OpenReadStream()) await s.ReadExactlyAsync(bytes, ct);
        if (Kind(bytes) is not { } ext) return "JPEG · PNG · WebP 사진만 올릴 수 있어요.";

        Directory.CreateDirectory(Dir);
        var now = time.GetUtcNow().UtcDateTime;
        var name = $"{accountId}-{now.Ticks}.{ext}";
        await File.WriteAllBytesAsync(Path.Combine(Dir, name), bytes, ct);
        await SetAsync(accountId, name, actor, tenantId, "사진 바꿈", now, ct);
        return null;
    }

    /// <summary>지우기(없으면 아무것도 안 함).</summary>
    public async Task DeleteAsync(int accountId, int actor, int? tenantId = null, CancellationToken ct = default) =>
        await SetAsync(accountId, null, actor, tenantId, "사진 지움", time.GetUtcNow().UtcDateTime, ct);

    private async Task SetAsync(int accountId, string? name, int actor, int? tenantId, string detail, DateTime now, CancellationToken ct)
    {
        await using var ctx = db.Create();
        var a = await ctx.Accounts.SingleAsync(x => x.Id == accountId, ct);
        var old = a.PhotoPath;
        if (old is null && name is null) return;
        a.PhotoPath = name;
        if (tenantId is int t && actor != accountId)
            ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorAccountId = actor, Action = LogAction, TenantId = t, Detail = $"{detail}: {a.DisplayName}" });
        await ctx.SaveChangesAsync(ct);
        if (FullPath(old) is { } p) File.Delete(p);
    }
}
