using System.Globalization;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>인명 검색 결과 한 사람(유년부 AddressBookPersonCardVM 과 같은 칸). Student = 학생 · 새친구(꼬리표는 반, 없으면 "학생").</summary>
public sealed record PeopleHit(int PersonId, string FullName, string? UserName, string? Email, string? PhoneNumber, int? BMonth, int? BDay,
    string? ClassText, bool Student, string? PhotoUrl);

/// <summary>
/// 인명 검색(R-358, 2026-11-21 사용자 · 확정 시안 인명검색): 유년부 /AddressBook/Search 를 그대로 옮겼다 —
/// 이 부서 명부(탈퇴한 줄 빼고)에서 이름 · 아이디에 검색어가 들어간 사람, 2글자부터, 이름 · 아이디가 검색어로 시작하는 사람 먼저 · 그다음 가나다순, 기본 30명 · 최대 50명.
/// 교사 · 관리자만 부른다(화면에서 막는다).
/// </summary>
public sealed class PeopleSearchService(PeopleService people, IAppDb db)
{
    public const int MinLength = 2, DefaultTake = 30, MaxTake = 50;
    private static readonly StringComparer Ko = StringComparer.Create(CultureInfo.GetCultureInfo("ko-KR"), false);

    public async Task<IReadOnlyList<PeopleHit>> SearchAsync(string? q, int take, string slug, CancellationToken ct = default)
    {
        q = (q ?? "").Trim();
        if (q.Length < MinLength) return [];
        take = Math.Clamp(take, 1, MaxTake);
        static bool Has(string? s, string t) => s is not null && s.Contains(t, StringComparison.OrdinalIgnoreCase);
        static bool Starts(string? s, string t) => s is not null && s.StartsWith(t, StringComparison.OrdinalIgnoreCase);
        var rows = (await people.RosterAsync(null, null, ct))
            .Where(r => r.LeftOn is null && (Has(r.Name, q) || Has(r.UserName, q)))
            .OrderBy(r => Starts(r.Name, q) || Starts(r.UserName, q) ? 0 : 1).ThenBy(r => r.Name, Ko)
            .Take(take).ToList();
        var names = rows.Select(r => r.UserName).OfType<string>().ToList();
        await using var ctx = db.Create();
        var photos = names.Count == 0 ? [] : await ctx.UserSettings.AsNoTracking().Where(s => names.Contains(s.UserName) && s.PhotoPath != null)
            .ToDictionaryAsync(s => s.UserName, s => s.PhotoPath!, ct);
        return rows.Select(r => new PeopleHit(r.PersonId, r.Name, r.UserName, r.Email, r.Phone1 ?? r.Phone2, r.BirthMonth, r.BirthDay, r.ClassText,
            ClassService.IsStudent(r),
            r.UserName is { } un && photos.TryGetValue(un, out var pv) ? $"/{slug}/photo/{Uri.EscapeDataString(un)}?v={Uri.EscapeDataString(pv)}" : null)).ToList();
    }

    /// <summary>쪽지 받을 사람이 이 부서 명부에 있는 계정인지(다른 부서 사람에게는 이 화면에서 보내지 않는다).</summary>
    public async Task<bool> InDeptAsync(string userName, CancellationToken ct = default) =>
        (await people.RosterAsync(null, null, ct)).Any(r => r.LeftOn is null && r.UserName == userName);
}
