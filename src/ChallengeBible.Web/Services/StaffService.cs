using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>임원 및 기타 화면의 사람 한 명. Locked = 어드민 역할(부서 관리자 계정, R-315) — 역할을 바꾸지 않는다.</summary>
public sealed record StaffPerson(int PersonId, string Name, string? RoleCode, string RoleLabel, bool IsAdmin, bool Linked, bool Locked, string? Phone);

/// <summary>직책 칸 하나: 역할 · 그 역할의 사람들 · 한 명만 두는 자리인가 · 관리자가 되는 역할인가.</summary>
public sealed record StaffSlot(string Code, string Label, bool Single, bool MakesAdmin, IReadOnlyList<StaffPerson> People)
{
    public bool Duplicate => Single && People.Count > 1;
    public bool Empty => People.Count == 0;
}

public sealed record StaffBoard(IReadOnlyList<StaffSlot> Officers, IReadOnlyList<StaffSlot> Helpers, int Etc, int Newcomers, IReadOnlyList<StaffPerson> People,
    IReadOnlyList<(string Code, string Label)> Choices)
{
    /// <summary>확인할 것: 한 명 자리에 두 명 이상 · 비어 있는 임원 자리.</summary>
    public int Warnings => Officers.Count(o => o.Duplicate || (o.Single && o.Empty));
}

public enum StaffError { None, NotFound, Locked, BadRole, TeamLocked }

/// <summary>
/// 임원 및 기타(2026-11-21 사용자 "교사와 교사 외로 나누기 · 교사 외는 '임원 및 기타'", 확정 시안 역할-담임-시안2 ②):
/// 임원(부장 · 부감 · 서기 · 회계 · 지도 · 교장) 직책 칸, 봉사, 기타 · 새친구 수. 역할 바꾸기는 계정이 있으면 소속(관리자 규칙 다시 맞춤),
/// 없으면 명부 줄. 어드민(부서 관리자 계정)의 역할은 바꾸지 않는다(R-315). 어드민 역할로 바꾸는 것도 막는다.
/// </summary>
public sealed class StaffService(PeopleService people, MemberAdminService members, IAppDb db)
{
    /// <summary>임원(화면 순서). 지도는 여러 명, 나머지는 한 명 자리.</summary>
    public static readonly string[] OfficerCodes = ["T1005", "T1006", "T1008", "T1009", "T1004", "T1040"];
    public static readonly HashSet<string> SingleCodes = ["T1005", "T1006", "T1008", "T1009", "T1040"];
    /// <summary>봉사: 보조 · 지원 · 율동 · 촬영 · 영상 · 반주 · 스텝.</summary>
    public static readonly string[] HelperCodes = ["T1010", "T1013", "T1015", "T1016", "T1017", "T1018", "T1019"];

    public async Task<StaffBoard> BoardAsync(CancellationToken ct = default)
    {
        var roster = (await people.RosterAsync(null, null, ct)).Where(r => r.LeftOn is null).ToList();
        await using var ctx = db.Create();
        var names = await ctx.CodeData.AsNoTracking().Where(c => c.MajorCD == CodeData.RoleMajor && c.MinorCD != null)
            .ToDictionaryAsync(c => c.MinorCD!.Trim(), c => c.MinorNM ?? c.MinorCD!, ct);
        var adminCodes = (await DeptAdminRuleService.CodesAsync(ctx, ct)).ToHashSet();
        string Label(string code) => names.GetValueOrDefault(code) ?? UserRole.Codes.FirstOrDefault(c => c.Code == code).Name ?? code;
        var all = roster.Select(P).ToList();
        StaffSlot Slot(string code) => new(code, Label(code), SingleCodes.Contains(code), adminCodes.Contains(code),
            all.Where(p => p.RoleCode == code).OrderBy(p => p.Name, StringComparer.Ordinal).ToList());
        var choices = UserRole.Codes.Where(c => c.Code != UserRole.AdminCode).Select(c => (c.Code, Label(c.Code))).ToList();
        return new StaffBoard(OfficerCodes.Select(Slot).ToList(), HelperCodes.Select(Slot).ToList(),
            all.Count(p => p.RoleCode == UserRole.EtcCode), all.Count(p => p.RoleCode == "T1020"), all, choices);
    }

    private static StaffPerson P(RosterRow r)
    {
        var code = r.RoleCode?.Trim();
        return new StaffPerson(r.PersonId, r.Name, string.IsNullOrEmpty(code) ? null : code, r.RoleLabel, r.IsAdmin, r.Linked, code == UserRole.AdminCode, r.Phone1 ?? r.Phone2);
    }

    /// <summary>한 사람의 역할 바꾸기. 코드는 역할 코드 목록 안에서만(어드민 빼고).</summary>
    public async Task<StaffError> ChangeAsync(int personId, string? roleCode, CancellationToken ct = default)
    {
        var code = roleCode?.Trim();
        if (string.IsNullOrEmpty(code) || code == UserRole.AdminCode || UserRole.Codes.All(c => c.Code != code)) return StaffError.BadRole;
        var r = (await people.RosterAsync(null, null, ct)).FirstOrDefault(x => x.PersonId == personId && x.LeftOn is null);
        if (r is null) return StaffError.NotFound;
        if (r.RoleCode?.Trim() == UserRole.AdminCode) return StaffError.Locked;   // 부서 관리자 계정(R-315)
        if (r.Linked)
        {
            if (r.DeptRoleId is not int id) return StaffError.NotFound;
            var e = await members.ChangeRoleAsync(id, code, ct);
            return e switch { MemberError.None => StaffError.None, MemberError.TeamLocked => StaffError.TeamLocked, _ => StaffError.NotFound };
        }
        return await people.SetRoleAsync(personId, code, ct) == PeopleError.None ? StaffError.None : StaffError.NotFound;
    }

    public static string Message(StaffError e) => e switch
    {
        StaffError.Locked => "부서 관리자 계정(어드민)의 역할은 바꾸지 않아요.",
        StaffError.BadRole => "바꿀 역할을 골라 주세요.",
        StaffError.TeamLocked => "가족팀 · 친구팀 시상 기록이 있어 이 역할로 바꿀 수 없어요. 팀에서 먼저 빼 주세요.",
        _ => "사람을 찾을 수 없어요. 새로고침해 주세요.",
    };
}
