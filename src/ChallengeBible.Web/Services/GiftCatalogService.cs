using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record GiftItemRow(int Id, string Name, int Price, string Status, string? ReviewReason, string? CoupangUrl, bool HasImage,
    int? TenantId, string? TenantName, int? SharedByTenantId, string? SharedByName, int Picked, DateTime UpdatedUtc);

public sealed record GiftItemCounts(int Active, int Review, int Retired, IReadOnlyList<(string Reason, int Count)> Reasons);

public sealed class GiftItemInput
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Price { get; set; }
    public string? CoupangUrl { get; set; }
}

public enum GiftItemError { None, NotFound, HasPicks, BadImage }

/// <summary>
/// 플랫폼 관리자 선물 상품 관리(ADR 0094 A, R-276 ~ R-279, 확정 시안 플랫폼-선물상품 B 줄 목록).
/// 시스템 연결로 모든 상품을 보고(공통 · 부서), 시작마다 플랫폼 관리자인지 DB 로 다시 확인한다. 바꾼 것은 감사 로그에 남긴다.
/// 사진은 `/images/AwardItem/{번호}.jpg`(R-274 폴더, 설정 Images:Dir) — 화면에서 정사각형으로 잘라 JPG 로 올린다.
/// </summary>
public sealed class GiftCatalogService(ISystemDb systemDb, IConfiguration config, IWebHostEnvironment env, TimeProvider time)
{
    public const int PageSize = 30;
    public const int MaxImageBytes = 600 * 1024;
    public const string LogAction = "GiftItem";

    /// <summary>선물 이미지 폴더(R-274). Program 의 /images 와 같은 곳.</summary>
    public static string ImagesDir(IConfiguration config, IWebHostEnvironment env) =>
        config["Images:Dir"] is { Length: > 0 } d ? d : Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "images");

    public string ImagePath(int id) => Path.Combine(ImagesDir(config, env), "AwardItem", $"{id}.jpg");

    /// <summary>쿠팡 상품 주소인가(R-278): http(s) 이고 주소가 coupang.com 이거나 그 아래.</summary>
    public static bool IsCoupang(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
        && (u.Host.Equals("coupang.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".coupang.com", StringComparison.OrdinalIgnoreCase));

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

    public async Task<GiftItemCounts> CountsAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var by = await ctx.GiftItems.GroupBy(i => i.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var reasons = await ctx.GiftItems.Where(i => i.Status == GiftItemStatus.Review).GroupBy(i => i.ReviewReason ?? "")
            .Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        int N(string s) => by.FirstOrDefault(x => x.Key == s)?.N ?? 0;
        return new(N(GiftItemStatus.Active), N(GiftItemStatus.Review), N(GiftItemStatus.Retired),
            reasons.OrderByDescending(x => x.N).Select(x => (x.Key, x.N)).ToList());
    }

    /// <summary>목록(시안 B): 상태 · 찾기(이름 · 번호) · 가격대(min ~ max) · 까닭(삭제 대상). 번호 순, take 개까지(+ 더 있는지).</summary>
    public async Task<(IReadOnlyList<GiftItemRow> Rows, bool More)> ListAsync(string actor, string status, string? q, int? min, int? max, string? reason, int take, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var items = ctx.GiftItems.AsNoTracking().Where(i => i.Status == status);
        var text = q?.Trim();
        if (!string.IsNullOrEmpty(text))
            items = int.TryParse(text, out var no) ? items.Where(i => i.Id == no || i.Name.Contains(text)) : items.Where(i => i.Name.Contains(text));
        if (min is int lo) items = items.Where(i => i.Price >= lo);
        if (max is int hi) items = items.Where(i => i.Price <= hi);
        if (!string.IsNullOrEmpty(reason)) items = items.Where(i => i.ReviewReason == reason);
        var rows = await (from i in items
                          join t in ctx.Tenants on i.TenantId equals t.Id into tt
                          from t in tt.DefaultIfEmpty()
                          join s in ctx.Tenants on i.SharedByTenantId equals s.Id into ss
                          from s in ss.DefaultIfEmpty()
                          orderby i.Id
                          select new GiftItemRow(i.Id, i.Name, i.Price, i.Status, i.ReviewReason, i.CoupangUrl, i.HasImage, i.TenantId, t == null ? null : t.Name,
                              i.SharedByTenantId, s == null ? null : s.Name, ctx.Gifts.Count(g => g.GiftItemId == i.Id), i.UpdatedUtc))
                         .Take(take + 1).ToListAsync(ct);
        return (rows.Take(take).ToList(), rows.Count > take);
    }

    public async Task<(GiftItem? Item, int Picked, string? TenantName, string? SharedByName)> GetAsync(string actor, int id, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var i = await ctx.GiftItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return (null, 0, null, null);
        var picked = await ctx.Gifts.CountAsync(g => g.GiftItemId == id, ct);
        var tn = i.TenantId is int t ? await ctx.Tenants.Where(x => x.Id == t).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var sn = i.SharedByTenantId is int s ? await ctx.Tenants.Where(x => x.Id == s).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return (i, picked, tn, sn);
    }

    /// <summary>
    /// 등록(id = null) · 고치기(R-278). 이름 1 ~ 100자, 가격 0 이상 정수, 쿠팡 링크 필수. 플랫폼이 새로 만드는 상품은 공통.
    /// 삭제 대상을 쿠팡 링크까지 바르게 고쳐 저장하면 판매 중으로 살아난다(R-279). 목록에서 내린 상품은 내린 채로 고친다.
    /// </summary>
    public async Task<(Dictionary<string, string> Errors, int Id)> SaveAsync(string actor, int? id, GiftItemInput input, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) errors["Name"] = "상품 이름을 써 주세요.";
        else if (name.Length > 100) errors["Name"] = "상품 이름은 100자까지예요.";
        var priceText = (input.Price ?? "").Replace(",", "").Replace("원", "").Trim();
        if (!int.TryParse(priceText, out var price) || price < 0) errors["Price"] = "가격은 0 이상 숫자로 써 주세요.";
        var url = (input.CoupangUrl ?? "").Trim();
        if (!IsCoupang(url)) errors["CoupangUrl"] = "쿠팡 상품 주소(coupang.com)를 넣어 주세요.";
        else if (url.Length > 1000) errors["CoupangUrl"] = "주소가 너무 길어요.";
        var desc = (input.Description ?? "").Trim();
        if (desc.Length > 500) errors["Description"] = "설명은 500자까지예요.";
        if (errors.Count > 0) return (errors, id ?? 0);

        await using var ctx = await OpenAsync(actor, ct);
        var now = time.GetUtcNow().UtcDateTime;
        GiftItem item;
        if (id is int existing)
        {
            item = await ctx.GiftItems.SingleOrDefaultAsync(x => x.Id == existing, ct) ?? throw new KeyNotFoundException();
        }
        else
        {
            item = new GiftItem { Name = name, CreatedUtc = now };
            ctx.GiftItems.Add(item);
        }
        item.Name = name;
        item.Description = desc.Length == 0 ? null : desc;
        item.Price = price;
        item.CoupangUrl = url;
        if (item.Status == GiftItemStatus.Review) { item.Status = GiftItemStatus.Active; item.ReviewReason = null; }
        item.UpdatedUtc = now;
        item.UpdatedByUserName = actor;
        await ctx.SaveChangesAsync(ct);
        Log(ctx, actor, id is null ? "등록" : "고침", item);
        await ctx.SaveChangesAsync(ct);
        return (errors, item.Id);
    }

    /// <summary>사진 올리기(R-278): 화면에서 정사각형으로 자른 JPG. JPG 가 아니거나 너무 크면 받지 않는다.</summary>
    public async Task<GiftItemError> SaveImageAsync(string actor, int id, Stream data, long length, CancellationToken ct = default)
    {
        if (length <= 0 || length > MaxImageBytes) return GiftItemError.BadImage;
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (PhotoService.Kind(bytes) != "jpg") return GiftItemError.BadImage;
        await using var ctx = await OpenAsync(actor, ct);
        var item = await ctx.GiftItems.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return GiftItemError.NotFound;
        var path = ImagePath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
        item.HasImage = true;
        item.UpdatedUtc = time.GetUtcNow().UtcDateTime;
        item.UpdatedByUserName = actor;
        Log(ctx, actor, "사진", item);
        await ctx.SaveChangesAsync(ct);
        return GiftItemError.None;
    }

    /// <summary>목록에서 내리기(R-279): 아이에게 안 보이고, 이력 · 총비용의 이름 · 사진 · 금액은 그대로.</summary>
    public Task<GiftItemError> RetireAsync(string actor, int id, CancellationToken ct = default) => SetStatusAsync(actor, id, GiftItemStatus.Retired, "내림", ct);

    /// <summary>다시 올리기: 내린 상품을 판매 중으로(쿠팡 링크가 바를 때만, 아니면 삭제 대상으로).</summary>
    public Task<GiftItemError> RestoreAsync(string actor, int id, CancellationToken ct = default) => SetStatusAsync(actor, id, GiftItemStatus.Active, "다시 올림", ct);

    private async Task<GiftItemError> SetStatusAsync(string actor, int id, string status, string what, CancellationToken ct)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var item = await ctx.GiftItems.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return GiftItemError.NotFound;
        if (status == GiftItemStatus.Active && !IsCoupang(item.CoupangUrl)) { status = GiftItemStatus.Review; item.ReviewReason = "no link"; }
        item.Status = status;
        if (status != GiftItemStatus.Review) item.ReviewReason = null;
        item.UpdatedUtc = time.GetUtcNow().UtcDateTime;
        item.UpdatedByUserName = actor;
        Log(ctx, actor, what, item);
        await ctx.SaveChangesAsync(ct);
        return GiftItemError.None;
    }

    /// <summary>삭제(R-279): 고른 기록이 있으면 하지 않는다(대신 목록에서 내리기). 없으면 상품 · 숨김 줄 · 서버 사진을 지운다.</summary>
    public async Task<GiftItemError> DeleteAsync(string actor, int id, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var item = await ctx.GiftItems.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return GiftItemError.NotFound;
        if (await ctx.Gifts.AnyAsync(g => g.GiftItemId == id, ct)) return GiftItemError.HasPicks;
        ctx.GiftItemsHidden.RemoveRange(await ctx.GiftItemsHidden.Where(h => h.GiftItemId == id).ToListAsync(ct));
        ctx.GiftItems.Remove(item);
        Log(ctx, actor, "삭제", item);
        await ctx.SaveChangesAsync(ct);
        var path = ImagePath(id);
        if (File.Exists(path)) File.Delete(path);
        return GiftItemError.None;
    }

    private void Log(AppDbContext ctx, string actor, string what, GiftItem item) =>
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = LogAction, TenantId = item.TenantId, Detail = $"{what} · {item.Id} {item.Name}" });

    /// <summary>삭제 대상 까닭(가져오기 SQL 의 영어 값) → 화면 글.</summary>
    public static string ReasonText(string? reason) => reason switch
    {
        "hidden in YuYunBu" => "유년부 숨김",
        "no link" => "링크 없음",
        "not coupang" => "쿠팡 아님",
        null or "" => "까닭 없음",
        _ => reason,
    };
}
