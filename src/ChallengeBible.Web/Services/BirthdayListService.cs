using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>생일 화면 한 사람. Next = 다가오는 생일(부서 날짜), DaysLeft = 오늘부터 남은 날(오늘 0). Gift = 그 해 생일 선물 상태(없으면 null). MyClass = 내가 담임인 반 아이.</summary>
public sealed record BirthdayRow(int PersonId, string Name, string? ClassText, string RoleLabel, bool Linked, byte Month, byte Day, DateOnly Next, int DaysLeft, string? Gift, bool MyClass);

/// <summary>생일이 비어 있는 사람(생일 없음 탭).</summary>
public sealed record BirthdayMissing(int PersonId, string Name, string? ClassText, string RoleLabel);

public sealed record BirthdayBoard(DateOnly Today, IReadOnlyList<BirthdayRow> All, IReadOnlyList<BirthdayMissing> Missing, int People);

/// <summary>
/// 생일(ADR 0121, R-344, 확정 시안 생일-시안): 교사 · 관리자가 보는 관리 화면. 예전 유년부 "월별 생일자 · 미지정 · 연간"은 개념만 가져와 새로 만들었다.
/// 자료는 명부(<see cref="PeopleService.RosterAsync"/>) — 탈퇴한 줄은 빼고, 반은 지금 반. 선물은 상태만 보여 주고 처리는 기존 선물 처리 화면에서 한다(겹치지 않게).
/// "오늘"은 부서 시간대. 2월 29일생은 평년에 2월 28일로 본다.
/// </summary>
public sealed class BirthdayListService(PeopleService people, IAppDb db, TenantContext tenant, TenantClock clock)
{
    public static readonly int[] DayChoices = [7, 14, 30, 60];
    public const int DefaultDays = 30;

    public DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    /// <summary>그 해의 생일 날짜. 2월 29일은 평년에 2월 28일, 달에 없는 날은 그 달 마지막 날.</summary>
    public static DateOnly Occur(int year, byte month, byte day) => new(year, month, Math.Min(day, (byte)DateTime.DaysInMonth(year, month)));

    /// <summary>오늘이거나 오늘 뒤의 가장 가까운 생일.</summary>
    public static DateOnly NextFrom(DateOnly today, byte month, byte day)
    {
        var d = Occur(today.Year, month, day);
        return d < today ? Occur(today.Year + 1, month, day) : d;
    }

    public static string GiftLabel(string? state) => state switch
    {
        GiftStates.Open or GiftStates.Expired => "고르는 중",
        GiftStates.Picked => "골랐음",
        GiftStates.Purchased => "샀음",
        GiftStates.Delivered => "전달함",
        _ => "선물 없음",
    };

    public static string GiftTone(string? state) => state switch
    {
        GiftStates.Open or GiftStates.Expired => "g1",
        GiftStates.Picked or GiftStates.Purchased => "g2",
        GiftStates.Delivered => "g3",
        _ => "g0",
    };

    public async Task<BirthdayBoard> BoardAsync(string? myUserId, CancellationToken ct = default)
    {
        var today = Today;
        var roster = (await people.RosterAsync(null, null, ct)).Where(r => r.LeftOn is null).ToList();
        await using var ctx = db.Create();
        // 내가 담임인 반(가장 최근 반 묶음)의 아이들
        var mine = new HashSet<int>();
        if (myUserId is not null)
        {
            var term = await ctx.ClassTerms.OrderByDescending(t => t.Id).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
            if (term is int tid)
            {
                var myPeople = ctx.People.Where(p => p.UserId == myUserId).Select(p => p.Id);
                var myClasses = ctx.ClassMembers.Where(m => m.TermId == tid && m.IsTeacher && myPeople.Contains(m.PersonId)).Select(m => m.ClassId);
                mine = (await ctx.ClassMembers.Where(m => m.TermId == tid && !m.IsTeacher && myClasses.Contains(m.ClassId)).Select(m => m.PersonId).ToListAsync(ct)).ToHashSet();
            }
        }
        // 생일 선물(그 해): 계정 있는 사람은 아이디로, 명부만 있는 사람은 줄 번호로
        var gifts = await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Birthday)
            .Select(g => new { g.PersonId, g.UserName, g.Year, g.State, g.CreatedUtc }).ToListAsync(ct);
        string? GiftOf(RosterRow r, int year) => gifts
            .Where(g => g.Year == year && ((g.PersonId == r.PersonId) || (r.UserName != null && g.UserName == r.UserName)))
            .OrderByDescending(g => g.CreatedUtc).Select(g => g.State).FirstOrDefault();

        var all = new List<BirthdayRow>();
        var missing = new List<BirthdayMissing>();
        foreach (var r in roster)
        {
            if (r is { BirthMonth: byte m and >= 1 and <= 12, BirthDay: byte d and >= 1 and <= 31 })
            {
                var next = NextFrom(today, m, d);
                all.Add(new BirthdayRow(r.PersonId, r.Name, r.ClassText, r.RoleLabel, r.Linked, m, d, next, next.DayNumber - today.DayNumber, GiftOf(r, next.Year), mine.Contains(r.PersonId)));
            }
            else missing.Add(new BirthdayMissing(r.PersonId, r.Name, r.ClassText, r.RoleLabel));
        }
        return new BirthdayBoard(today, all, missing.OrderBy(x => x.ClassText is null).ThenBy(x => x.ClassText).ThenBy(x => x.Name).ToList(), roster.Count);
    }

    /// <summary>다가오는 생일: days 일 안(오늘 포함), 날짜순 · 같은 날은 내 반 먼저 · 이름순.</summary>
    public static IReadOnlyList<BirthdayRow> Upcoming(BirthdayBoard b, int days) =>
        b.All.Where(r => r.DaysLeft <= days).OrderBy(r => r.DaysLeft).ThenByDescending(r => r.MyClass).ThenBy(r => r.Name).ToList();

    /// <summary>그 달 생일자: 날짜순 · 같은 날은 내 반 먼저.</summary>
    public static IReadOnlyList<BirthdayRow> Month(BirthdayBoard b, int month) =>
        b.All.Where(r => r.Month == month).OrderBy(r => r.Day).ThenByDescending(r => r.MyClass).ThenBy(r => r.Name).ToList();

    /// <summary>생일 없음 탭에서 생일만 넣기(교사 · 관리자). 이름 · 연락처 · 역할은 그대로.</summary>
    public async Task<PeopleError> SetBirthdayAsync(int personId, int? month, int? day, CancellationToken ct = default)
    {
        if (month is null || day is null || !PeopleService.Birthday(month, day, out var bm, out var bd) || bm is null) return PeopleError.BadBirthday;
        await using var ctx = db.Create();
        var p = await ctx.People.SingleOrDefaultAsync(x => x.Id == personId && x.LeftOn == null, ct);
        if (p is null) return PeopleError.NotFound;
        (p.BirthMonth, p.BirthDay) = (bm, bd);
        await ctx.SaveChangesAsync(ct);
        return PeopleError.None;
    }
}
