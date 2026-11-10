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
    /// <summary>참여 신청 화면의 부서 목록에 보이기(0059). 기본 꺼짐 — 꺼져도 부서 주소 · 코드로는 신청할 수 있다.</summary>
    public bool JoinListed { get; set; }
    /// <summary>
    /// 임시 부서 "처음 오신 분"(0064): 가입하면 승인 없이 바로 참여자가 되는 부서. 하나뿐이다.
    /// 순위 · 참여자 현황 · 가족팀 · "우리 부서" 숫자를 숨기고, 참여 신청 대상이 아니다.
    /// </summary>
    public bool IsLobby { get; set; }
    /// <summary>이 부서의 축하 단계(0077, R-100): "1000,2000,3000"(오름차순). null = 플랫폼 기본 규칙 값.</summary>
    public string? Milestones { get; set; }
    public string Status { get; set; } = TenantStatus.Active;
    public DateTime CreatedUtc { get; set; }
}

public static class TenantStatus
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
}

/// <summary>화면에 보이는 소속 상태: 신청 = SignupApplication PENDING, 승인 = DeptRoleUserAssoc 한 줄, 거절 = SignupApplication REJECTED.</summary>
public static class MembershipStatus
{
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public static readonly string[] All = [Requested, Approved, Rejected];
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
    public required string ChangedByUserName { get; set; }
}
