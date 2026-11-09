using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>사람 종류: 역할 코드에서 정해진다(유년부 Common/UserRole.cs 와 같다).</summary>
public enum UserRoleKind { Parent = 0, Student = 1, Teacher = 2, Officer = 3, Etc = 4 }

/// <summary>
/// 역할 기준 한 곳(ADR 0090). 역할은 DeptRoleUserAssoc 에서만 읽는다.
/// T1001 학생 · T1002/T1003 교사 · T1099 기타 · 그 밖 임원 · 코드 없음 학부모. 팀 규칙도 여기서 정한다.
/// </summary>
public static class UserRole
{
    public const string StudentCode = "T1001";
    public const string TeacherCode = "T1002";
    public const string TeacherSCode = "T1003";
    public const string EtcCode = "T1099";
    /// <summary>가족팀 관계 번호(DM2_FamilyMember.RoleCode): 0 학생 · 1 엄마 · 2 아빠 · 3 할머니 · 4 할아버지.</summary>
    public const int RelStudent = 0;

    /// <summary>역할 코드 18개(유년부 CodeData T001). 부서를 만들 때 함께 넣는다.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Codes =
    [
        ("T1001", "학생"), ("T1002", "교사"), ("T1003", "교사(S)"), ("T1004", "지도"), ("T1005", "부장"), ("T1006", "부감"),
        ("T1008", "서기"), ("T1009", "회계"), ("T1010", "보조"), ("T1013", "지원"), ("T1015", "율동"), ("T1016", "촬영"),
        ("T1017", "영상"), ("T1018", "반주"), ("T1019", "스텝"), ("T1020", "새친구"), ("T1040", "교장"), ("T1099", "기타"),
    ];

    public static UserRoleKind KindOf(string? roleCode)
    {
        var rc = (roleCode ?? "").Trim();
        if (rc.Length == 0) return UserRoleKind.Parent;
        if (rc == StudentCode) return UserRoleKind.Student;
        if (rc == TeacherCode || rc == TeacherSCode) return UserRoleKind.Teacher;
        if (rc == EtcCode) return UserRoleKind.Etc;
        return UserRoleKind.Officer;
    }

    public static string LabelOf(UserRoleKind k) => k switch
    {
        UserRoleKind.Student => "학생",
        UserRoleKind.Teacher => "교사",
        UserRoleKind.Officer => "임원",
        UserRoleKind.Etc => "기타",
        _ => "학부모",
    };

    /// <summary>신청한 역할(S · T · P · E) → 승인할 때의 기본 역할 코드. 학부모는 코드가 없다.</summary>
    public static string? CodeForKind(string roleKind) => roleKind switch
    {
        SignupRoleKind.Student => StudentCode,
        SignupRoleKind.Teacher => TeacherCode,
        SignupRoleKind.Etc => EtcCode,
        _ => null,
    };

    public static string RoleKindOf(UserRoleKind k) => k switch
    {
        UserRoleKind.Student => SignupRoleKind.Student,
        UserRoleKind.Teacher or UserRoleKind.Officer => SignupRoleKind.Teacher,
        UserRoleKind.Etc => SignupRoleKind.Etc,
        _ => SignupRoleKind.Parent,
    };

    /// <summary>이 소속이 가진 화면 권한(Roles). 모두 미션을 하므로 참여자 권한은 기본이다.</summary>
    public static IEnumerable<string> ScreenRoles(string? roleCode, bool isAdmin)
    {
        yield return Roles.Participant;
        var k = KindOf(roleCode);
        if (k == UserRoleKind.Parent) yield return Roles.Guardian;
        if (k is UserRoleKind.Teacher or UserRoleKind.Officer) yield return Roles.Teacher;
        if (isAdmin) yield return Roles.TenantAdmin;
    }

    // 팀 규칙

    public static bool CanJoinFamily(UserRoleKind k) => k is UserRoleKind.Student or UserRoleKind.Parent or UserRoleKind.Etc;

    public static bool CanJoinFriend(UserRoleKind k) => k == UserRoleKind.Student;

    public static bool RelationAllowed(UserRoleKind k, int relRole) => k switch
    {
        UserRoleKind.Student => relRole == RelStudent,
        UserRoleKind.Parent or UserRoleKind.Etc => relRole is >= 1 and <= 4,
        _ => false,
    };

    /// <summary>가족팀 관계를 줄 수 있는지 — 안 되면 화면 문구, 되면 null. self = 본인이 하는 일(문구만 다르다).</summary>
    public static string? FamilyRelationError(UserRoleKind k, int relRole, bool self)
    {
        if (!CanJoinFamily(k))
            return self ? "교사 · 임원은 가족팀에 들어갈 수 없어요." : $"그분은 {LabelOf(k)}이라 가족팀에 들어올 수 없어요.";
        if (RelationAllowed(k, relRole)) return null;
        if (k == UserRoleKind.Student)
            return self ? "학생은 관계를 '학생'으로만 정할 수 있어요." : "그분은 학생이라 관계를 '학생'으로만 정할 수 있어요.";
        var who = LabelOf(k);
        return self ? $"{who}는 엄마 · 아빠 · 할머니 · 할아버지 중에서 골라 주세요." : $"그분은 {who}라 엄마 · 아빠 · 할머니 · 할아버지 중에서 골라 주세요.";
    }

    /// <summary>한 사람의 역할 종류. 코드가 없고 승인 대기 중이면 신청한 역할로 본다(유년부 GetKindAsync).</summary>
    public static async Task<UserRoleKind> KindAsync(AppDbContext ctx, string userName, CancellationToken ct = default)
    {
        var user = await ctx.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (user is null) return UserRoleKind.Parent;
        var code = await ctx.DeptRoles.IgnoreQueryFilters().Where(d => d.UserId == user && d.RoleCode != null && d.RoleCode != "")
            .Select(d => d.RoleCode).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(code)) return KindOf(code);
        var pending = await ctx.SignupApplications.IgnoreQueryFilters()
            .Where(a => a.UserName == userName && a.Status == SignupStatus.Pending && a.ReqType == SignupReqType.Join)
            .OrderByDescending(a => a.Id).Select(a => a.RoleKind).FirstOrDefaultAsync(ct);
        return pending?.Trim() switch
        {
            SignupRoleKind.Student => UserRoleKind.Student,
            SignupRoleKind.Teacher => UserRoleKind.Teacher,
            SignupRoleKind.Etc => UserRoleKind.Etc,
            _ => UserRoleKind.Parent,
        };
    }
}
