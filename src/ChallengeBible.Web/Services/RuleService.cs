using System.Text.Json;
using System.Text.Json.Serialization;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChallengeBible.Web.Services;

public sealed record RuleVersion(int VersionNo, DateOnly EffectiveFrom, RuleSet Rules, DateTime? ChangedUtc, int? ChangedByAccountId);

/// <summary>
/// 점수 · 수행 규칙 읽기(ADR 0060). 어떤 날의 규칙 = 적용 시작일이 그날 이전인 버전 가운데 번호가 가장 큰 것.
/// 표가 비어 있으면 코드 기본값(<see cref="RuleSet.Default"/>, v1). 규칙 표는 플랫폼 표라 영역과 상관없이 읽는다.
/// 자주 읽으므로 잠깐 기억해 두고, 저장하면 <see cref="Forget"/> 로 지운다.
/// </summary>
public sealed class RuleService(IAppDb db, IMemoryCache cache)
{
    private const string CacheKey = "rules:versions";
    public static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static RuleVersion Initial { get; } = new(RuleSet.Default.Version, DateOnly.MinValue, RuleSet.Default, null, null);

    public static string ToJson(RuleSet r) => JsonSerializer.Serialize(r, Json);
    public static RuleSet FromJson(string json) => JsonSerializer.Deserialize<RuleSet>(json, Json)!;

    public static void Forget(IMemoryCache cache) => cache.Remove(CacheKey);

    /// <summary>모든 버전(번호 순). 맨 앞은 늘 코드 기본값 v1.</summary>
    public async Task<IReadOnlyList<RuleVersion>> VersionsAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<RuleVersion>? hit) && hit is not null) return hit;
        await using var ctx = db.Create();
        var rows = await ctx.PlatformRuleSetVersions.AsNoTracking().OrderBy(v => v.VersionNo).ToListAsync(ct);
        IReadOnlyList<RuleVersion> list = [Initial, .. rows.Select(v => new RuleVersion(v.VersionNo, v.EffectiveFrom, FromJson(v.Values) with { Version = v.VersionNo },
            v.ChangedUtc, v.ChangedByAccountId))];
        cache.Set(CacheKey, list, TimeSpan.FromMinutes(5));
        return list;
    }

    public static RuleSet Pick(IReadOnlyList<RuleVersion> versions, DateOnly date) =>
        versions.Where(v => v.EffectiveFrom <= date).MaxBy(v => v.VersionNo)?.Rules ?? RuleSet.Default;

    /// <summary>그날(미션 날짜)에 적용되는 규칙.</summary>
    public async Task<RuleSet> ForDateAsync(DateOnly date, CancellationToken ct = default) => Pick(await VersionsAsync(ct), date);
}
