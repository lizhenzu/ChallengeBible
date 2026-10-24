using ChallengeBible.Infrastructure.Data;

namespace ChallengeBible.Web.Tenancy;

/// <summary>
/// 부서 주소(`/{slug}/…`)로 정한 이 요청의 부서와 내 소속(1-2 요청 처리 순서: 쿠키 → 주소 → 소속 · 역할 → 권한).
/// <see cref="TenantPageFilter"/> 가 채운다.
/// </summary>
public sealed class TenantContext
{
    public Tenant? Tenant { get; private set; }
    public int? MembershipId { get; private set; }
    public string? MembershipStatus { get; private set; }
    public IReadOnlySet<string> Roles { get; private set; } = new HashSet<string>();

    public Tenant Current => Tenant ?? throw new InvalidOperationException("부서 주소가 아닌 요청이에요.");
    public bool IsApproved => MembershipStatus == Infrastructure.Data.MembershipStatus.Approved;
    public bool Has(string role) => IsApproved && Roles.Contains(role);
    public bool IsAdmin => Has(Infrastructure.Data.Roles.TenantAdmin);
    public bool IsStaff => IsAdmin || Has(Infrastructure.Data.Roles.Teacher);
    public string AppName => Tenant?.AppName ?? Services.BrandingService.DefaultName;   // 0038 — 비어 있으면 기본 이름
    public string AppShortName => Tenant?.AppShortName ?? Services.BrandingService.DefaultShortName;

    internal void Set(Tenant tenant, Membership? me)
    {
        Tenant = tenant;
        MembershipId = me?.Id;
        MembershipStatus = me?.Status;
        Roles = me?.Roles.Select(r => r.Role).ToHashSet() ?? [];
    }
}

/// <summary>이 화면을 쓸 수 있는 역할(하나라도 있으면 통과). 붙이지 않으면 승인된 소속이면 된다(R-130).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TenantRolesAttribute(params string[] roles) : Attribute
{
    public IReadOnlyList<string> Roles { get; } = roles;
}
