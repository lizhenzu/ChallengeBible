using Microsoft.Extensions.Caching.Memory;

namespace ChallengeBible.Web.Services;

/// <summary>참여 코드 찾기 횟수 제한 — 계정당 1분 10회(0048 결정 6, 코드 맞히기 막기).</summary>
public sealed class LookupLimiter(IMemoryCache cache)
{
    public const int PerMinute = 10;

    public bool TryTake(int accountId)
    {
        var key = $"join-lookup:{accountId}";
        var count = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1); return new Counter(); })!;
        return Interlocked.Increment(ref count.Value) <= PerMinute;
    }

    private sealed class Counter { public int Value; }
}
