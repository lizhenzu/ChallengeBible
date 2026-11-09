namespace ChallengeBible.Infrastructure.Tenancy;

/// <summary>
/// 이 요청의 영역 · 계정. 웹이 요청 처음에 한 번 정하고(주소 → TenantId, 쿠키 → 아이디),
/// 연결을 열 때 DB 세션 값으로 넣는다(물리 설계 3-1). 한 번 정한 값은 바꿀 수 없다.
/// </summary>
public sealed class RequestScope
{
    public int? TenantId { get; private set; }
    public string? UserName { get; private set; }
    public string? UserId { get; private set; }

    public void SetTenant(int tenantId)
    {
        if (TenantId is not null && TenantId != tenantId) throw new InvalidOperationException("이 요청의 영역은 이미 정해졌어요.");
        TenantId = tenantId;
    }

    public void SetUser(string userName, string userId)
    {
        if (UserName is not null && UserName != userName) throw new InvalidOperationException("이 요청의 사람은 이미 정해졌어요.");
        UserName = userName;
        UserId = userId;
    }
}
