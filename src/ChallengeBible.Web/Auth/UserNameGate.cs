using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChallengeBible.Web.Auth;

/// <summary>
/// 쿠키의 아이디가 지금도 그 사람의 아이디인가(ADR 0090). 플랫폼 관리자가 아이디를 바꾸면 옛 쿠키로 옛 아이디에 기록이 쌓이지 않게 다시 로그인시킨다.
/// 요청마다 DB 를 읽지 않도록 1분 기억한다. 아이디를 바꾼 곳이 <see cref="Forget"/> 을 불러 바로 반영한다.
/// </summary>
public sealed class UserNameGate(IAppDb db, IMemoryCache cache)
{
    private static string Key(string userName) => "uname:" + userName;

    public async Task<bool> IsCurrentAsync(string userName, string userId, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(userName), out bool ok)) return ok;
        await using var ctx = db.Create();
        ok = await ctx.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.UserName == userName, ct);
        cache.Set(Key(userName), ok, TimeSpan.FromMinutes(1));
        return ok;
    }

    public void Forget(string userName) => cache.Remove(Key(userName));
}
