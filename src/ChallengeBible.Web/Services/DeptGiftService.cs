using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record DeptGiftRow(int Id, string Name, int Price, bool HasImage, DateTime UpdatedUtc, bool Common, bool Hidden, string? SharedByName, string Status);

public sealed record DeptGiftCounts(int All, int Shown, int Hidden);

/// <summary>금액 · 점수 화면 값. 빈 칸(null) = 공통 기본값(규칙).</summary>
public sealed class DeptGiftMoney
{
    public int? PersonalCap { get; set; }
    public int? NewcomerMin { get; set; }
    public int? NewcomerMax { get; set; }
    public int? InviteMin { get; set; }
    public int? InviteMax { get; set; }
    public int? BirthdayMin { get; set; }
    public int? BirthdayMax { get; set; }
    /// <summary>크레딧 → 금액 표(크레딧 오름차순).</summary>
    public List<(int Credits, int Amount)> Rates { get; set; } = [];
    /// <summary>완독 회차별 금액: 1독 · 2독 · 3독부터(빈 칸 = 규칙).</summary>
    public int?[] Reading { get; set; } = new int?[3];
}

/// <summary>금액 · 점수 화면에서 받은 글(쉼표 · "원" 허용).</summary>
public sealed class DeptGiftMoneyInput
{
    public string? PersonalCap { get; set; }
    public string? NewcomerMin { get; set; }
    public string? NewcomerMax { get; set; }
    public string? InviteMin { get; set; }
    public string? InviteMax { get; set; }
    public string? BirthdayMin { get; set; }
    public string? BirthdayMax { get; set; }
    public List<string?> RateCredits { get; set; } = [];
    public List<string?> RateAmounts { get; set; } = [];
    public List<string?> Reading { get; set; } = [];
}

/// <summary>
/// 부서 관리자 선물 설정(ADR 0094, 확정 시안 관리-선물 ①②③): 선물 목록(공통 숨기기, R-276) · 우리 부서 상품(GiftCatalogService 로) · 금액 · 점수(R-280 · R-283 · R-286 · R-288 · R-289).
/// 이 요청의 부서 연결(전역 필터 + RLS)로 읽고 쓴다.
/// </summary>
public sealed class DeptGiftService(IAppDb db, TenantContext tenant, RuleService rules, TimeProvider time)
{
    public const int MaxAmount = 1_000_000;
    public const int MaxCredits = 1_000_000;

    private int T => tenant.Current.Id;

    public async Task<GiftRules> RulesAsync(CancellationToken ct = default) =>
        (await rules.ForDateAsync(TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId), ct)).Gifts;

    /// <summary>① 공통 상품 탭: 판매 중인 공통 상품만(부서 상품은 ② 부서 상품 탭), 숨김 표시. show = all · shown · hidden. (2026-11-14 사용자: 탭 이름 "선물 목록" → "공통 상품")</summary>
    public async Task<(IReadOnlyList<DeptGiftRow> Rows, bool More, DeptGiftCounts Counts)> ListAsync(string show, string? q, int? min, int? max, int take, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var hidden = (await ctx.GiftItemsHidden.Select(h => h.GiftItemId).ToListAsync(ct)).ToHashSet();
        var items = ctx.GiftItems.AsNoTracking().Where(i => i.Status == GiftItemStatus.Active && i.TenantId == null);
        var all = await items.Select(i => new { i.Id, i.TenantId }).ToListAsync(ct);
        var counts = new DeptGiftCounts(all.Count, all.Count(i => !hidden.Contains(i.Id)), all.Count(i => hidden.Contains(i.Id)));
        var text = q?.Trim();
        if (!string.IsNullOrEmpty(text))
            items = int.TryParse(text, out var no) ? items.Where(i => i.Id == no || i.Name.Contains(text)) : items.Where(i => i.Name.Contains(text));
        if (min is int lo) items = items.Where(i => i.Price >= lo);
        if (max is int hi) items = items.Where(i => i.Price <= hi);
        var hiddenIds = hidden.ToList();
        if (show == "hidden") items = items.Where(i => hiddenIds.Contains(i.Id));
        else if (show == "shown") items = items.Where(i => !hiddenIds.Contains(i.Id));
        var rows = await (from i in items
                          join s in ctx.Tenants on i.SharedByTenantId equals s.Id into ss
                          from s in ss.DefaultIfEmpty()
                          orderby i.Id
                          select new { i.Id, i.Name, i.Price, i.HasImage, i.UpdatedUtc, i.TenantId, i.Status, Shared = s == null ? null : s.Name })
                         .Take(take + 1).ToListAsync(ct);
        return (rows.Take(take).Select(r => new DeptGiftRow(r.Id, r.Name, r.Price, r.HasImage, r.UpdatedUtc, r.TenantId == null, r.TenantId == null && hidden.Contains(r.Id), r.Shared, r.Status)).ToList(),
            rows.Count > take, counts);
    }

    /// <summary>공통 상품 숨기기 · 보이기(R-276). 우리 부서 상품은 숨기지 않는다(내리기는 상품 화면에서).</summary>
    public async Task<bool> SetHiddenAsync(string actor, int itemId, bool hide, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (!await ctx.GiftItems.AnyAsync(i => i.Id == itemId && i.TenantId == null, ct)) return false;
        var row = await ctx.GiftItemsHidden.SingleOrDefaultAsync(h => h.GiftItemId == itemId, ct);
        if (hide && row is null) ctx.GiftItemsHidden.Add(new GiftItemHidden { TenantId = T, GiftItemId = itemId, HiddenUtc = time.GetUtcNow().UtcDateTime, HiddenByUserName = actor });
        else if (!hide && row is not null) ctx.GiftItemsHidden.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>② 부서 상품 탭: 우리 부서 상품(모든 상태) + 우리가 공통에 올린 상품.</summary>
    public async Task<IReadOnlyList<DeptGiftRow>> OursAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return (await ctx.GiftItems.AsNoTracking().Where(i => i.TenantId == T || i.SharedByTenantId == T).OrderBy(i => i.TenantId == null).ThenBy(i => i.Id)
                .Select(i => new { i.Id, i.Name, i.Price, i.HasImage, i.UpdatedUtc, i.TenantId, i.Status }).ToListAsync(ct))
            .Select(i => new DeptGiftRow(i.Id, i.Name, i.Price, i.HasImage, i.UpdatedUtc, i.TenantId == null, false, null, i.Status)).ToList();
    }

    /// <summary>③ 금액 · 점수: 저장된 값(없으면 null = 규칙 값).</summary>
    public async Task<DeptGiftMoney> MoneyAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var s = await ctx.GiftSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        var rates = await ctx.GiftCreditRates.AsNoTracking().OrderBy(r => r.Credits).Select(r => new { r.Credits, r.Amount }).ToListAsync(ct);
        var reading = await ctx.GiftReadingAmounts.AsNoTracking().ToListAsync(ct);
        return new DeptGiftMoney
        {
            PersonalCap = s?.PersonalCap, NewcomerMin = s?.NewcomerMin, NewcomerMax = s?.NewcomerMax, InviteMin = s?.InviteMin, InviteMax = s?.InviteMax,
            BirthdayMin = s?.BirthdayMin, BirthdayMax = s?.BirthdayMax,
            Rates = rates.Select(r => (r.Credits, r.Amount)).ToList(),
            Reading = [.. Enumerable.Range(1, 3).Select(n => reading.FirstOrDefault(r => r.RoundNo == n)?.Amount)],
        };
    }

    /// <summary>"12,000원" → 12000, 빈 칸 → null(규칙 값), 틀리면 ok = false.</summary>
    public static (bool Ok, int? Value) Parse(string? s, int max)
    {
        var t = (s ?? "").Replace(",", "").Replace("원", "").Replace("크레딧", "").Trim();
        if (t.Length == 0) return (true, null);
        return int.TryParse(t, System.Globalization.NumberStyles.None, null, out var n) && n <= max ? (true, n) : (false, null);
    }

    /// <summary>
    /// ③ 저장. 금액은 0 ~ 100만 원, 범위는 최소 ≤ 최대(빈 칸은 규칙 값으로 비교), 크레딧은 1,000 단위 · 겹치지 않음. 틀린 칸이 있으면 아무것도 저장하지 않는다.
    /// 저장하면 앞으로 생기는 선물부터 적용된다(이미 생긴 선물은 생길 때 적어 둔 금액).
    /// </summary>
    public async Task<Dictionary<string, string>> SaveMoneyAsync(string actor, DeptGiftMoneyInput input, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var r = await RulesAsync(ct);
        int? Num(string key, string? v, int max)
        {
            var (ok, n) = Parse(v, max);
            if (!ok) errors[key] = "0 이상 숫자로 써 주세요.";
            return n;
        }
        var cap = Num("PersonalCap", input.PersonalCap, MaxCredits);
        var nMin = Num("NewcomerMin", input.NewcomerMin, MaxAmount); var nMax = Num("NewcomerMax", input.NewcomerMax, MaxAmount);
        var iMin = Num("InviteMin", input.InviteMin, MaxAmount); var iMax = Num("InviteMax", input.InviteMax, MaxAmount);
        var bMin = Num("BirthdayMin", input.BirthdayMin, MaxAmount); var bMax = Num("BirthdayMax", input.BirthdayMax, MaxAmount);
        void Range(string key, int? lo, int? hi, AmountRange d)
        {
            if (!errors.ContainsKey(key + "Min") && !errors.ContainsKey(key + "Max") && (lo ?? d.Min) > (hi ?? d.Max)) errors[key + "Max"] = "최대 금액이 최소 금액보다 작아요.";
        }
        Range("Newcomer", nMin, nMax, r.Newcomer); Range("Invite", iMin, iMax, r.Invite); Range("Birthday", bMin, bMax, r.Birthday);

        var rates = new List<(int Credits, int Amount)>();
        for (var k = 0; k < Math.Max(input.RateCredits.Count, input.RateAmounts.Count); k++)
        {
            var cText = k < input.RateCredits.Count ? input.RateCredits[k] : null;
            var aText = k < input.RateAmounts.Count ? input.RateAmounts[k] : null;
            var (cOk, c) = Parse(cText, MaxCredits);
            var (aOk, a) = Parse(aText, MaxAmount);
            if (c is null && a is null && cOk && aOk) continue;   // 빈 줄은 건너뛴다
            if (!cOk || c is null || c <= 0 || c % 1000 != 0) { errors[$"Rate{k}"] = "크레딧은 1,000 단위로 써 주세요."; continue; }
            if (!aOk || a is null) { errors[$"Rate{k}"] = "금액을 0 이상 숫자로 써 주세요."; continue; }
            if (rates.Any(x => x.Credits == c)) { errors[$"Rate{k}"] = "같은 크레딧이 두 줄 있어요."; continue; }
            rates.Add((c.Value, a.Value));
        }
        var reading = new int?[3];
        for (var k = 0; k < 3; k++) reading[k] = Num($"Reading{k}", k < input.Reading.Count ? input.Reading[k] : null, MaxAmount);
        if (errors.Count > 0) return errors;

        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        var s = await ctx.GiftSettings.SingleOrDefaultAsync(ct);
        if (s is null) ctx.GiftSettings.Add(s = new GiftSetting { TenantId = T });
        (s.PersonalCap, s.NewcomerMin, s.NewcomerMax, s.InviteMin, s.InviteMax, s.BirthdayMin, s.BirthdayMax) = (cap, nMin, nMax, iMin, iMax, bMin, bMax);
        s.UpdatedUtc = now;
        s.UpdatedByUserName = actor;
        ctx.GiftCreditRates.RemoveRange(await ctx.GiftCreditRates.ToListAsync(ct));
        ctx.GiftCreditRates.AddRange(rates.OrderBy(x => x.Credits).Select(x => new GiftCreditRate { TenantId = T, Credits = x.Credits, Amount = x.Amount }));
        ctx.GiftReadingAmounts.RemoveRange(await ctx.GiftReadingAmounts.ToListAsync(ct));
        for (var k = 0; k < 3; k++)
            if (reading[k] is int a) ctx.GiftReadingAmounts.Add(new GiftReadingAmount { TenantId = T, RoundNo = k + 1, Amount = a });
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = "GiftSetting", TenantId = T, Detail = "선물 금액 · 점수 저장" });
        await ctx.SaveChangesAsync(ct);
        return errors;
    }
}
