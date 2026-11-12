using Microsoft.Extensions.Caching.Memory;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 관리자 로그인(비밀번호만) 틀린 횟수 제한. 아이디를 묻지 않아 비밀번호만 맞히면 되므로 더 세게 막는다:
/// 같은 접속 주소 15분에 5번, 전체 1시간에 20번 틀리면 그동안 막는다.
/// </summary>
public sealed class AdminLoginLimiter(IMemoryCache cache)
{
    public const int PerAddress = 5, Total = 20;

    public bool Blocked(string address) => Count($"admin-login:ip:{address}") >= PerAddress || Count("admin-login:all") >= Total;

    public void Failed(string address)
    {
        Bump($"admin-login:ip:{address}", TimeSpan.FromMinutes(15));
        Bump("admin-login:all", TimeSpan.FromHours(1));
    }

    private int Count(string key) => cache.TryGetValue<Counter>(key, out var c) ? c!.Value : 0;

    private void Bump(string key, TimeSpan life)
    {
        var c = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = life; return new Counter(); })!;
        Interlocked.Increment(ref c.Value);
    }

    private sealed class Counter { public int Value; }
}
