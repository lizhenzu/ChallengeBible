namespace ChallengeBible.Infrastructure.Data;

// 표 하나에 클래스 하나. 열 이름 · 형식은 물리 설계 참조 DDL 과 맞춘다.

/// <summary>영역 범위 표의 표시. 전역 필터 · RLS 대상.</summary>
public interface ITenantOwned
{
    int TenantId { get; }
}

public sealed class Tenant
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public required string JoinCode { get; set; }
    public required string TimeZoneId { get; set; }
    public required string ThemeColor { get; set; }
    public string? AppName { get; set; }
    public string? AppShortName { get; set; }
    public bool MissionEnabled { get; set; }
    public bool ReadingEnabled { get; set; }
    public bool ReadingBoardPublic { get; set; } = true;
    public string Status { get; set; } = TenantStatus.Active;
    public DateTime CreatedUtc { get; set; }
}

public static class TenantStatus
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
}

public sealed class Account
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string DisplayName { get; set; }
    public string? PhotoPath { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public bool MustChangePassword { get; set; }
    public int? FamilyTenantId { get; set; }
    public int? LastTenantId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedUtc { get; set; }
}

public sealed class Membership : ITenantOwned
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int AccountId { get; set; }
    public string Status { get; set; } = MembershipStatus.Requested;
    public string? GroupLabel { get; set; }
    public DateTime RequestedUtc { get; set; }
    public DateTime? DecidedUtc { get; set; }
    public int? DecidedByAccountId { get; set; }
    public List<MembershipRole> Roles { get; set; } = [];
}

public static class MembershipStatus
{
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Suspended = "Suspended";
    public const string Released = "Released";
    public const string Rejected = "Rejected";
    public const string PromotionPending = "PromotionPending";
    public static readonly string[] All = [Requested, Approved, Suspended, Released, Rejected, PromotionPending];
}

public sealed class MembershipRole : ITenantOwned
{
    public int TenantId { get; set; }
    public int MembershipId { get; set; }
    public required string Role { get; set; }
}

public static class Roles
{
    public const string Participant = "Participant";
    public const string Guardian = "Guardian";
    public const string Teacher = "Teacher";
    public const string TenantAdmin = "TenantAdmin";
    public static readonly string[] All = [Participant, Guardian, Teacher, TenantAdmin];
}

/// <summary>영역의 그해 시즌 시작일(0028). 행이 없으면 1월 1일.</summary>
public sealed class TenantSeason : ITenantOwned
{
    public int TenantId { get; set; }
    public short Year { get; set; }
    public DateOnly StartDate { get; set; }
    public DateTime ChangedUtc { get; set; }
    public int ChangedByAccountId { get; set; }
}
