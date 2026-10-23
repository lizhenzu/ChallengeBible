using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Tests.Web;

/// <summary>부서 관리 홈(확정 시안: 관리-홈): 오늘 참여 · 최근 7일 · 오늘 안 한 친구 · 할 일. 오늘 = 2027-01-05 (서울).</summary>
public class AdminHomeServiceTests
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;

    public AdminHomeServiceTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
    }

    private AdminHomeService In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        ctx.Set(t, null);
        return new AdminHomeService(new InMemoryDb(_db, scope), ctx, new TenantClock(FixedTime.SeoulMorning()));
    }

    /// <summary>참여자 한 명(승인된 소속 + 참여자 + 반).</summary>
    private int Kid(Tenant t, string name, string? group = null, string status = MembershipStatus.Approved)
    {
        var a = InMemoryDb.SeedAccount(_db, name + "@example.com", name);
        var m = InMemoryDb.SeedMembership(_db, t.Id, a.Id, status, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        if (group is not null) { raw.Memberships.Single(x => x.Id == m.Id).GroupLabel = group; }
        var p = new Participant { TenantId = t.Id, MembershipId = m.Id };
        raw.Participants.Add(p);
        raw.SaveChanges();
        return p.Id;
    }

    private void Did(Tenant t, int participantId, DateOnly date, MissionItem item)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.Activities.Add(new Activity { TenantId = t.Id, ParticipantId = participantId, MissionItem = item.Code(), MissionDate = date, Points = 1, PerformedUtc = DateTime.UtcNow });
        raw.SaveChanges();
    }

    [Fact]
    public async Task 오늘_참여는_미션을_하나라도_한_친구_수이고_항목별로_센다()
    {
        var a = Kid(_kids, "김철수"); var b = Kid(_kids, "김영희"); Kid(_kids, "이은우");
        Did(_kids, a, Jan5, MissionItem.WordMe); Did(_kids, a, Jan5, MissionItem.WordParent); Did(_kids, a, Jan5, MissionItem.PrayerMe);
        Did(_kids, b, Jan5, MissionItem.PrayerMe);
        var h = await In(_kids).GetAsync();
        Assert.Equal(3, h.Members);
        Assert.Equal(2, h.TodayCount);                     // 철수 · 영희 (철수는 여러 번 해도 1명)
        Assert.Equal(1, h.TodayByMission[Mission.Word]);
        Assert.Equal(2, h.TodayByMission[Mission.Prayer]);
        Assert.Equal(0, h.TodayByMission[Mission.Quiz]);
    }

    [Fact]
    public async Task 암송은_이번_주_누적으로_센다()
    {
        var a = Kid(_kids, "김철수"); var b = Kid(_kids, "김영희");
        // 2027-01-05(화)의 주는 1/3(일) 시작. 1/4(월)에 철수가 외웠고, 지난주 1/1(금)에 영희가 외웠다.
        Did(_kids, a, new DateOnly(2027, 1, 4), MissionItem.Recite);
        Did(_kids, b, new DateOnly(2027, 1, 1), MissionItem.Recite);
        var h = await In(_kids).GetAsync();
        Assert.Equal(new DateOnly(2027, 1, 3), h.WeekStart);
        Assert.Equal(1, h.TodayByMission[Mission.Recite]);
        Assert.Equal(0, h.TodayCount);                     // 오늘은 아무도 안 함
    }

    [Fact]
    public async Task 정지_신청_중인_소속은_소속_참여자에_넣지_않는다()
    {
        Kid(_kids, "김철수");
        Kid(_kids, "정지된친구", status: MembershipStatus.Suspended);
        Kid(_kids, "신청중친구", status: MembershipStatus.Requested);
        var h = await In(_kids).GetAsync();
        Assert.Equal(1, h.Members);
        Assert.Single(h.Missing);
    }

    [Fact]
    public async Task 최근_7일은_날짜별_참여한_친구_수다_오늘을_포함한다()
    {
        var a = Kid(_kids, "김철수"); var b = Kid(_kids, "김영희");
        Did(_kids, a, Jan5, MissionItem.WordMe);
        Did(_kids, a, Jan5.AddDays(-1), MissionItem.WordMe); Did(_kids, b, Jan5.AddDays(-1), MissionItem.WordMe);
        Did(_kids, a, Jan5.AddDays(-6), MissionItem.WordMe);
        Did(_kids, b, Jan5.AddDays(-7), MissionItem.WordMe);   // 7일 밖
        var h = await In(_kids).GetAsync();
        Assert.Equal(7, h.Week.Count);
        Assert.Equal(Jan5.AddDays(-6), h.Week[0].Date);
        Assert.Equal(Jan5, h.Week[6].Date);
        Assert.Equal([1, 0, 0, 0, 0, 2, 1], h.Week.Select(w => w.Count).ToArray());
        Assert.Equal(new DateOnly(2027, 1, 4), h.Best.Date);
        Assert.Equal(2, h.Best.Count);
    }

    [Fact]
    public async Task 오늘_안_한_친구는_오래_안_한_순이고_한_번도_안_한_친구가_맨_앞이다()
    {
        var a = Kid(_kids, "박서연", "사랑반"); var b = Kid(_kids, "최민준", "사랑반"); var c = Kid(_kids, "서예준", "믿음반"); var d = Kid(_kids, "윤지호", "믿음반"); Kid(_kids, "한소율");
        Did(_kids, a, Jan5.AddDays(-1), MissionItem.WordMe);   // 어제
        Did(_kids, b, Jan5.AddDays(-3), MissionItem.WordMe);   // 3일 전
        Did(_kids, c, Jan5.AddDays(-12), MissionItem.WordMe);  // 12일 전
        Did(_kids, d, Jan5, MissionItem.WordMe);               // 오늘 함 → 목록에서 빠짐
        var h = await In(_kids).GetAsync();
        Assert.Equal(["한소율", "서예준", "최민준", "박서연"], h.Missing.Select(m => m.Name).ToArray());
        Assert.Equal([null, 12, 3, 1], h.Missing.Select(m => m.DaysAgo).ToArray());
        Assert.Equal([null, "믿음반", "사랑반", "사랑반"], h.Missing.Select(m => m.Group).ToArray());
    }

    [Fact]
    public async Task 기다리는_신청은_수와_이름을_보여_주고_보호자는_표시한다()
    {
        var a = InMemoryDb.SeedAccount(_db, "a@example.com", "정하늘");
        var g = InMemoryDb.SeedAccount(_db, "g@example.com", "정미영");
        InMemoryDb.SeedMembership(_db, _kids.Id, a.Id, MembershipStatus.Requested, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _kids.Id, g.Id, MembershipStatus.Requested, Roles.Guardian);
        for (var i = 0; i < 3; i++) InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, $"x{i}@example.com", $"신청{i}").Id, MembershipStatus.Requested, Roles.Participant);
        var h = await In(_kids).GetAsync();
        Assert.Equal(5, h.PendingCount);
        Assert.Equal(3, h.Pending.Count);                  // 이름은 앞 3명만
        Assert.Contains(h.Pending, p => p.Name == "정하늘" && !p.IsGuardian);
        Assert.Contains(h.Pending, p => p.Name == "정미영" && p.IsGuardian);
    }

    [Fact]
    public async Task 앞으로_7일_중_말씀_퀴즈가_없는_날을_알려_준다()
    {
        // 1/5 ~ 1/11 중 말씀은 1/5 · 1/6 만, 퀴즈는 1/5 · 1/6 · 1/7 만 있다 → 첫 빈 날은 1/7(말씀)
        ContentSeed.Word(_db, Jan5); ContentSeed.Word(_db, Jan5.AddDays(1));
        ContentSeed.ChoiceQuiz(_db, Jan5); ContentSeed.ChoiceQuiz(_db, Jan5.AddDays(1)); ContentSeed.ChoiceQuiz(_db, Jan5.AddDays(2));
        var e = (await In(_kids).GetAsync()).Empty!;
        Assert.Equal(5, e.Days);                           // 1/7(말씀) · 1/8 · 1/9 · 1/10 · 1/11(말씀 · 퀴즈)
        Assert.Equal(Jan5.AddDays(2), e.First);
        Assert.True(e.Word);
        Assert.False(e.Quiz);
    }

    [Fact]
    public async Task 앞으로_7일_콘텐츠가_다_있으면_할_일이_없다()
    {
        for (var i = 0; i < 7; i++) { ContentSeed.Word(_db, Jan5.AddDays(i)); ContentSeed.ChoiceQuiz(_db, Jan5.AddDays(i)); }
        Assert.Null((await In(_kids).GetAsync()).Empty);
    }

    [Fact]
    public async Task 다른_부서의_참여_기록은_섞이지_않는다()
    {
        var mine = Kid(_kids, "김철수"); var other = Kid(_elem, "이다른");
        Did(_kids, mine, Jan5, MissionItem.WordMe);
        Did(_elem, other, Jan5, MissionItem.WordMe); Did(_elem, other, Jan5, MissionItem.PrayerMe);
        var h = await In(_kids).GetAsync();
        Assert.Equal(1, h.Members);
        Assert.Equal(1, h.TodayCount);
        Assert.Equal(0, h.TodayByMission[Mission.Prayer]);
        Assert.Empty(h.Missing);
    }
}
