using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>생일 화면 한 사람. Next = 다가오는 생일(부서 날짜), DaysLeft = 오늘부터 남은 날(오늘 0). Gift = 그 해 생일 선물 상태(없으면 null). MyClass = 내가 담임인 반 아이.</summary>
public sealed record BirthdayRow(int PersonId, string Name, string? ClassText, string RoleLabel, bool Linked, byte Month, byte Day, DateOnly Next, int DaysLeft, string? Gift, bool MyClass)
{
    public BirthdayDetail? Detail { get; init; }
}

/// <summary>생일 카드의 선물(그 해, ADR 0122): 상태 · 한도 · 기한 · 링크(없음/만듦/부모님이 열어 봄) · 고른 상품 · 선생님이 골랐으면 그 이름.</summary>
public sealed record BirthdayGiftCard(long Id, string State, int MaxAmount, DateOnly? OpenUntil, bool HasLink, bool Viewed,
    int? ItemId, string? ItemName, int? ItemPrice, bool HasImage, string? CoupangUrl, string? StaffPicker);

/// <summary>생일 카드 상세(유년부 카드 개념, ADR 0122): 계정 · 사진 · 전화 · 출석(올해 / 새친구는 처음 온 날부터) · 담임 · 선물.</summary>
public sealed record BirthdayDetail(string? UserName, string? PhotoV, string? Phone, DateOnly? FirstVisit,
    int AttendDone, int AttendTotal, DateOnly? LastAttend, string? TeacherName, string? TeacherPhone, bool GiftEligible, BirthdayGiftCard? Gift)
{
    /// <summary>3주(21일) 넘게 출석이 없으면 주황(R-345).</summary>
    public bool Absent(DateOnly today) => AttendTotal > 0 && (LastAttend is not { } l || today.DayNumber - l.DayNumber > 21);
}

/// <summary>생일이 비어 있는 사람(생일 없음 탭).</summary>
public sealed record BirthdayMissing(int PersonId, string Name, string? ClassText, string RoleLabel);

public sealed record BirthdayBoard(DateOnly Today, IReadOnlyList<BirthdayRow> All, IReadOnlyList<BirthdayMissing> Missing, int People)
{
    /// <summary>생일 선물이 생기는 때: 생일 며칠 전부터(규칙 BirthdayOpenDays).</summary>
    public int GiftOpenDays { get; init; } = 30;
}

/// <summary>
/// 생일(ADR 0121, R-344, 확정 시안 생일-시안): 교사 · 관리자가 보는 관리 화면. 예전 유년부 "월별 생일자 · 미지정 · 연간"은 개념만 가져와 새로 만들었다.
/// 자료는 명부(<see cref="PeopleService.RosterAsync"/>) — 탈퇴한 줄은 빼고, 반은 지금 반. 선물은 상태만 보여 주고 처리는 기존 선물 처리 화면에서 한다(겹치지 않게).
/// "오늘"은 부서 시간대. 2월 29일생은 평년에 2월 28일로 본다.
/// </summary>
public sealed class BirthdayListService(PeopleService people, IAppDb db, TenantContext tenant, TenantClock clock, BirthdayGiftService gifts, RuleService rules)
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

    public async Task<BirthdayBoard> BoardAsync(string? myUserId, CancellationToken ct = default, bool detail = false)
    {
        var today = Today;
        if (detail) await gifts.EnsureAsync(ct);   // 생일 30일 전 안에 든 아이의 선물을 만든다(선물 처리 화면과 같다)
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
        var giftRows = await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Birthday)
            .Select(g => new { g.PersonId, g.UserName, g.Year, g.State, g.CreatedUtc }).ToListAsync(ct);
        string? GiftOf(RosterRow r, int year) => giftRows
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
        var openDays = (await rules.ForDateAsync(today, ct)).Gifts.BirthdayOpenDays;
        if (detail) all = await DetailAsync(ctx, all, roster, today, ct);
        return new BirthdayBoard(today, all, missing.OrderBy(x => x.ClassText is null).ThenBy(x => x.ClassText).ThenBy(x => x.Name).ToList(), roster.Count) { GiftOpenDays = openDays };
    }

    /// <summary>카드 상세(ADR 0122): 사진 · 전화 · 출석 · 담임 · 그 해 선물.</summary>
    private async Task<List<BirthdayRow>> DetailAsync(AppDbContext ctx, List<BirthdayRow> rows, List<RosterRow> roster, DateOnly today, CancellationToken ct)
    {
        var ids = rows.Select(r => r.PersonId).ToList();
        var byId = roster.ToDictionary(r => r.PersonId);
        var persons = await ctx.People.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Phone1, p.NewcomerFirstVisit }).ToDictionaryAsync(p => p.Id, ct);
        var names = roster.Where(r => r.UserName != null).Select(r => r.UserName!).ToList();
        var photos = await ctx.UserSettings.AsNoTracking().Where(s => names.Contains(s.UserName) && s.PhotoPath != null).ToDictionaryAsync(s => s.UserName, s => s.PhotoPath!, ct);
        // 출석: 올해(새친구는 처음 온 날부터) 주일
        var jan1 = new DateOnly(today.Year, 1, 1);
        var checks = (await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId) && a.Sunday >= jan1 && a.Sunday <= today)
            .Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct)).GroupBy(a => a.PersonId).ToDictionary(g => g.Key, g => g.Select(x => x.Sunday).ToList());
        static int Sundays(DateOnly from, DateOnly to)
        {
            var first = from.AddDays((7 - (int)from.DayOfWeek) % 7);
            return first > to ? 0 : (to.DayNumber - first.DayNumber) / 7 + 1;
        }
        // 담임: 가장 최근 반 묶음에서 그 반의 선생님(첫 사람)
        var term = await ctx.ClassTerms.OrderByDescending(t => t.Id).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        var cm = term is int tid ? await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == tid).Select(m => new { m.ClassId, m.PersonId, m.IsTeacher, m.Id }).ToListAsync(ct) : [];
        var classOf = cm.Where(m => !m.IsTeacher).GroupBy(m => m.PersonId).ToDictionary(g => g.Key, g => g.First().ClassId);
        var teacherOf = cm.Where(m => m.IsTeacher).GroupBy(m => m.ClassId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First().PersonId);
        var tpids = teacherOf.Values.Distinct().ToList();
        var teachers = await ctx.People.AsNoTracking().Where(p => tpids.Contains(p.Id)).Select(p => new { p.Id, p.Name, p.Phone1, p.UserId }).ToDictionaryAsync(p => p.Id, ct);
        var tNames = roster.Where(r => tpids.Contains(r.PersonId)).ToDictionary(r => r.PersonId, r => r.Name);   // 계정 있는 선생님은 계정 이름
        // 그 해 선물(생일): 상태 · 링크 · 상품 · 선생님이 골랐는지
        var years = rows.Select(r => (short)r.Next.Year).Distinct().ToList();
        var userNames = rows.Select(r => byId[r.PersonId].UserName).OfType<string>().ToList();
        var gl = await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Birthday && years.Contains(g.Year) && ((g.PersonId != null && ids.Contains(g.PersonId.Value)) || (g.UserName != null && userNames.Contains(g.UserName))))
            .Select(g => new { g.Id, g.PersonId, g.UserName, g.Year, g.State, g.MaxAmount, g.OpenUntil, g.LinkToken, g.GiftItemId, g.ItemName, g.ItemPrice, g.ItemHasImage, g.CreatedUtc }).ToListAsync(ct);
        var gids = gl.Select(g => g.Id).ToList();
        var logs = await ctx.GiftLogs.AsNoTracking().Where(l => gids.Contains(l.GiftId) && (l.Action == GiftActions.LinkView || l.Action == GiftActions.Pick || l.Action == GiftActions.Change))
            .Select(l => new { l.GiftId, l.Id, l.Action, l.Detail }).ToListAsync(ct);
        var itemIds = gl.Select(g => g.GiftItemId).OfType<int>().Distinct().ToList();
        var urls = await ctx.GiftItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.CoupangUrl }).ToDictionaryAsync(i => i.Id, i => i.CoupangUrl, ct);

        return rows.Select(r =>
        {
            var rr = byId[r.PersonId];
            var p = persons.GetValueOrDefault(r.PersonId);
            var first = p?.NewcomerFirstVisit is { } fv && fv > jan1 ? fv : jan1;
            var mine = checks.GetValueOrDefault(r.PersonId)?.Where(d => d >= first).ToList() ?? [];
            string? tName = null, tPhone = null;
            if (classOf.TryGetValue(r.PersonId, out var cid) && teacherOf.TryGetValue(cid, out var tp) && tp != r.PersonId && teachers.TryGetValue(tp, out var t))
                (tName, tPhone) = (tNames.GetValueOrDefault(tp) ?? t.Name, t.Phone1);
            var g = gl.Where(x => x.Year == r.Next.Year && (x.PersonId == r.PersonId || (rr.UserName != null && x.UserName == rr.UserName))).OrderByDescending(x => x.CreatedUtc).FirstOrDefault();
            BirthdayGiftCard? card = null;
            if (g is not null)
            {
                var gLogs = logs.Where(l => l.GiftId == g.Id).ToList();
                var lastPick = gLogs.Where(l => l.Action != GiftActions.LinkView).OrderByDescending(l => l.Id).FirstOrDefault();
                var staff = lastPick?.Detail is { } d && d.StartsWith(BirthdayGiftService.StaffPrefix, StringComparison.Ordinal) ? d[BirthdayGiftService.StaffPrefix.Length..] : null;
                card = new BirthdayGiftCard(g.Id, g.State, g.MaxAmount, g.OpenUntil, g.LinkToken != null, gLogs.Any(l => l.Action == GiftActions.LinkView),
                    g.GiftItemId, g.ItemName, g.ItemPrice, g.ItemHasImage == true, g.GiftItemId is int ii ? urls.GetValueOrDefault(ii) : null,
                    g.State is GiftStates.Picked or GiftStates.Purchased or GiftStates.Delivered ? staff : null);
            }
            return r with { Detail = new BirthdayDetail(rr.UserName, rr.UserName is { } un ? photos.GetValueOrDefault(un) : null, p?.Phone1, p?.NewcomerFirstVisit,
                mine.Count, Sundays(first, today), mine.Count == 0 ? null : mine.Max(), tName, tPhone,
                BirthdayGiftService.BirthdayRoles.Contains(rr.RoleCode), card) };
        }).ToList();
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
