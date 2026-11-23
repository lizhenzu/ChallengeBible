using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>소개 화면 고치기 값(ADR 0140). 사진은 따로(IFormFile).</summary>
public sealed record IntroInput(string? Title, string? Body, bool Active, bool RemovePhoto);

/// <summary>
/// 부서 소개 화면(ADR 0140, R-368 ~ R-371, 2026-11-23 사용자 "유년부 전용 · 처음 들어온 사람에게 먼저"):
/// 줄(app.TenantIntro)이 있는 부서만 소개 화면이 있다 — 줄은 운영 SQL 로 유년부에만 만든다(다른 부서 · 관리자가 스스로 만들지 못함).
/// 「1주일 후 다시 보기」 · 「다시 보지 않기」는 로그인한 계정 기준(app.TenantIntroSeen). 손님은 들어올 때마다 나온다.
/// 사진은 부서 폴더(설정 Intro:Dir, 없으면 App_Data/intro)/{TenantId}/ 에 두고 이 서비스로만 연다. 바꾼 기록은 AuditLog "TenantIntro".
/// </summary>
public sealed class IntroService(IAppDb db, TenantContext tenant, TimeProvider time, IConfiguration config, IWebHostEnvironment env)
{
    public const string LogAction = "TenantIntro";
    /// <summary>「1주일 후 다시 보기」.</summary>
    public const int LaterDays = 7;
    public const long MaxPhotoBytes = 5 * 1024 * 1024;

    private string Root => config["Intro:Dir"] is { Length: > 0 } d ? d : Path.Combine(env.ContentRootPath, "App_Data", "intro");
    private string Dir => Path.Combine(Root, tenant.Current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>이 부서의 소개 줄(숨긴 것은 includeHidden 일 때만). 없으면 null — 소개 화면이 없는 부서.</summary>
    public async Task<TenantIntro?> GetAsync(bool includeHidden = false, CancellationToken ct = default)
    {
        if (tenant.IsLobby) return null;
        await using var ctx = db.Create();
        return await ctx.TenantIntros.AsNoTracking().SingleOrDefaultAsync(i => includeHidden || i.IsActive, ct);
    }

    /// <summary>들어올 때 소개 화면을 먼저 보여 줄까: 줄이 켜져 있고, 손님이거나 이 계정이 「다시 보지 않기」 · 「1주일 후」를 고른 게 아닐 때.</summary>
    public async Task<bool> ShouldShowAsync(string? userName, CancellationToken ct = default)
    {
        if (await GetAsync(ct: ct) is null) return false;
        if (userName is null) return true;
        await using var ctx = db.Create();
        var seen = await ctx.TenantIntroSeens.AsNoTracking().SingleOrDefaultAsync(s => s.UserName == userName, ct);
        if (seen is null) return true;
        if (seen.Never) return false;
        return seen.HideUntilUtc is not { } until || until <= time.GetUtcNow().UtcDateTime;
    }

    /// <summary>「1주일 후 다시 보기」: 오늘부터 7일 뒤까지 숨긴다(이전에 「다시 보지 않기」였어도 이 선택이 이긴다).</summary>
    public Task LaterAsync(string userName, CancellationToken ct = default) =>
        Remember(userName, s => (s.HideUntilUtc, s.Never) = (time.GetUtcNow().UtcDateTime.AddDays(LaterDays), false), ct);

    /// <summary>「다시 보지 않기」: 계정이 바뀌기 전까지 소개 화면이 먼저 나오지 않는다(☰ 메뉴에서는 언제든 본다).</summary>
    public Task NeverAsync(string userName, CancellationToken ct = default) =>
        Remember(userName, s => (s.HideUntilUtc, s.Never) = (null, true), ct);

    private async Task Remember(string userName, Action<TenantIntroSeen> set, CancellationToken ct)
    {
        if (tenant.IsLobby || await GetAsync(ct: ct) is null) return;
        await using var ctx = db.Create();
        var s = await ctx.TenantIntroSeens.SingleOrDefaultAsync(x => x.UserName == userName, ct);
        if (s is null) { s = new TenantIntroSeen { TenantId = tenant.Current.Id, UserName = userName }; ctx.TenantIntroSeens.Add(s); }
        set(s);
        s.UpdatedUtc = time.GetUtcNow().UtcDateTime;
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>글 · 사진 고치기(부서 관리자만). 줄이 없는 부서는 KeyNotFoundException(= 소개 화면이 없는 부서).
    /// 사진은 내용으로 종류를 확인한다(JPG · PNG, 5MB까지).</summary>
    public async Task<FieldErrors> SaveAsync(string actor, IntroInput input, IFormFile? photo, CancellationToken ct = default)
    {
        if (!tenant.IsAdmin) throw new UnauthorizedAccessException("부서 관리자만 소개 화면을 고칠 수 있어요.");
        var errors = new FieldErrors();
        var title = input.Title?.Trim();
        var body = string.IsNullOrWhiteSpace(input.Body) ? null : input.Body.Trim();
        if (string.IsNullOrEmpty(title)) errors["Title"] = "제목을 넣어 주세요.";
        else if (title.Length > 60) errors["Title"] = "제목은 60자까지예요.";
        if (body is { Length: > 1000 }) errors["Body"] = "소개 글은 1000자까지예요.";

        byte[]? bytes = null; (string Ext, string Type)? kind = null;
        if (photo is { Length: > 0 })
        {
            if (photo.Length > MaxPhotoBytes) errors["Photo"] = "사진이 너무 커요(5MB까지).";
            else
            {
                bytes = new byte[photo.Length];
                await using (var s = photo.OpenReadStream()) await s.ReadExactlyAsync(bytes, ct);
                kind = BulletinService.Kind(bytes);
                if (kind is null || kind.Value.Type == "application/pdf") errors["Photo"] = "사진(JPG · PNG)만 올릴 수 있어요.";
            }
        }
        if (!errors.Ok) return errors;

        await using var ctx = db.Create();
        var row = await ctx.TenantIntros.SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        var now = time.GetUtcNow().UtcDateTime;
        string? oldPhoto = null;
        if (bytes is not null && kind is { } k)
        {
            Directory.CreateDirectory(Dir);
            var stored = $"{Guid.NewGuid():N}.{k.Ext}";
            await File.WriteAllBytesAsync(Path.Combine(Dir, stored), bytes, ct);
            oldPhoto = row.PhotoStoredName;
            (row.PhotoStoredName, row.PhotoContentType) = (stored, k.Type);
        }
        else if (input.RemovePhoto && row.PhotoStoredName is not null)
        {
            oldPhoto = row.PhotoStoredName;
            (row.PhotoStoredName, row.PhotoContentType) = (null, null);
        }
        (row.Title, row.Body, row.IsActive, row.UpdatedUtc, row.UpdatedByUserName) = (title!, body, input.Active, now, actor);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = LogAction, TenantId = tenant.Current.Id,
            Detail = $"제목 \"{title}\" · {(input.Active ? "켬" : "끔")} · 사진 {(bytes is not null ? "바꿈" : input.RemovePhoto ? "뺌" : "그대로")}" });
        await ctx.SaveChangesAsync(ct);
        if (oldPhoto is not null) { var p = Path.Combine(Dir, oldPhoto); if (File.Exists(p)) File.Delete(p); }
        return errors;
    }

    /// <summary>대표 사진 열기: 이 부서의 것만(RLS + 전역 필터). 줄이 꺼져 있어도 관리자는 미리 볼 수 있게 숨김도 연다.</summary>
    public async Task<(string Path, string Type)?> OpenPhotoAsync(CancellationToken ct = default)
    {
        var row = await GetAsync(includeHidden: tenant.IsAdmin, ct);
        if (row?.PhotoStoredName is not { } name || row.PhotoContentType is not { } type) return null;
        var path = Path.Combine(Dir, name);
        return File.Exists(path) ? (path, type) : null;
    }
}
