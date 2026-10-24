using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Tests.Web;

/// <summary>통독 참여자 현황(R-159) · 관리자 통독 현황(0040 규칙 7). 점수 없이 진도율만(R-157). 오늘 = 2027-01-05 화요일(서울).</summary>
public class ReadingBoardServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly int _me, _hee, _teacher, _outsider, _waiting;
    private readonly Membership _meM, _heeM, _teacherM;

    public ReadingBoardServiceTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _me = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").Id;
        _hee = InMemoryDb.SeedAccount(_db, "younghee@example.com", "김영희").Id;
        _teacher = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜").Id;
        _outsider = InMemoryDb.SeedAccount(_db, "soyoung@example.com", "박소영").Id;
        _waiting = InMemoryDb.SeedAccount(_db, "minjun@example.com", "최민준").Id;
        _meM = InMemoryDb.SeedMembership(_db, _kids.Id, _me, MembershipStatus.Approved, Roles.Participant);
        _heeM = InMemoryDb.SeedMembership(_db, _kids.Id, _hee, MembershipStatus.Approved, Roles.Guardian);
        _teacherM = InMemoryDb.SeedMembership(_db, _kids.Id, _teacher, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _elem.Id, _outsider, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _kids.Id, _waiting, MembershipStatus.Requested, Roles.Participant);

        var now = _time.GetUtcNow().UtcDateTime;
        using var raw = InMemoryDb.Raw(_db);
        raw.Memberships.Single(m => m.Id == _meM.Id).GroupLabel = "사랑반";
        // 김철수: 1회차 2장(오늘) · 김영희: 1회차 완독 + 2회차 3장(10일 전) · 박소영(다른 부서) · 최민준(신청 중)도 읽었다
        raw.ReadingRounds.AddRange(
            new ReadingRound { AccountId = _me, RoundNo = 1, StartedUtc = now.AddDays(-1) },
            new ReadingRound { AccountId = _hee, RoundNo = 1, StartedUtc = now.AddDays(-400), CompletedUtc = now.AddDays(-20) },
            new ReadingRound { AccountId = _hee, RoundNo = 2, StartedUtc = now.AddDays(-20) },
            new ReadingRound { AccountId = _outsider, RoundNo = 1, StartedUtc = now.AddDays(-3) },
            new ReadingRound { AccountId = _waiting, RoundNo = 1, StartedUtc = now.AddDays(-3) });
        void Read(int who, int round, byte book, short ch, DateTime at) =>
            raw.ChapterReads.Add(new ChapterRead { AccountId = who, RoundNo = round, BookId = book, Chapter = ch, ReadUtc = at });
        Read(_me, 1, 1, 1, now.AddHours(-1));
        Read(_me, 1, 1, 2, now);
        Read(_hee, 1, 66, 22, now.AddDays(-20));
        for (short c = 1; c <= 3; c++) Read(_hee, 2, 40, c, now.AddDays(-10));
        Read(_outsider, 1, 1, 1, now);
        Read(_waiting, 1, 1, 1, now);
        raw.SaveChanges();
    }

    private ReadingBoardService As(int accountId, Membership? me, bool? boardPublic = null)
    {
        if (boardPublic is bool p)
        {
            using var raw = InMemoryDb.Raw(_db);
            raw.Tenants.Single(t => t.Id == _kids.Id).ReadingBoardPublic = p;
            raw.SaveChanges();
        }
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        scope.SetAccount(accountId);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(t => t.Id == _kids.Id), me);
        return new ReadingBoardService(new InMemoryDb(_db, scope), tc, scope, new TenantClock(_time));
    }

    [Fact]
    public async Task R159_같은_부서_승인_소속원만_보인다()
    {
        var b = await As(_me, _meM, boardPublic: true).GetAsync();
        Assert.True(b.CanSee);
        Assert.Equal(["김영희", "김철수", "이은혜"], b.Rows.Select(r => r.Name).OrderBy(n => n).ToArray());   // 다른 부서 · 신청 중은 없다
        Assert.Equal(3, b.Members);
        Assert.Equal(2, b.Readers);                                       // 이은혜는 아직 시작 전
        Assert.True(b.Rows.Single(r => r.AccountId == _me).Me);
    }

    [Fact]
    public async Task R159_공개를_끄면_참여자는_못_보고_관리자_교사는_본다()
    {
        var kid = await As(_me, _meM, boardPublic: false).GetAsync();
        Assert.False(kid.CanSee);
        Assert.Empty(kid.Rows);
        var staff = await As(_teacher, _teacherM).GetAsync();
        Assert.True(staff.CanSee);
        Assert.False(staff.Public);
        Assert.Equal(3, staff.Rows.Count);
    }

    [Fact]
    public async Task R157_진도율_완독_명예의_전당_이번_주_통계()
    {
        var b = await As(_me, _meM, boardPublic: true).GetAsync();
        var me = b.Rows.Single(r => r.AccountId == _me);
        Assert.Equal((1, 0, 2), (me.RoundNo, me.Completed, me.ReadChapters));
        Assert.Equal(ReadingSummary.Percent(2, 1189), me.Percent);
        Assert.Equal(((byte?)1, (short?)2, (int?)0), (me.LastBook, me.LastChapter, me.DaysAgo));
        var hee = b.Rows.Single(r => r.AccountId == _hee);
        Assert.Equal((2, 1, 3, 10), (hee.RoundNo, hee.Completed, hee.ReadChapters, hee.DaysAgo!.Value));   // 진도는 지금 회차만
        Assert.Equal("김영희", Assert.Single(b.HallOfFame).Name);
        Assert.Equal(1, b.Finishers);
        Assert.Equal(1, b.TotalCompleted);
        Assert.Equal(1, b.ReadThisWeek);                                  // 이번 주(일요일부터)는 김철수만
        Assert.Equal(Math.Round((me.Percent + hee.Percent) / 2, 1), b.AvgPercent);
        Assert.Null(b.Rows.Single(r => r.AccountId == _teacher).DaysAgo);
    }

    [Fact]
    public async Task 진도율순은_완독_횟수가_먼저_최근순은_마지막_읽은_때()
    {
        var b = await As(_me, _meM, boardPublic: true).GetAsync();
        Assert.Equal(["김영희", "김철수", "이은혜"], ReadingBoardService.Sort(b.Rows, "progress", null).Select(r => r.Name).ToArray());
        Assert.Equal(["김철수", "김영희", "이은혜"], ReadingBoardService.Sort(b.Rows, "recent", null).Select(r => r.Name).ToArray());
        Assert.Equal(["김영희"], ReadingBoardService.Sort(b.Rows, "progress", " 영희").Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task 배지는_반_이름_없으면_역할()
    {
        var b = await As(_teacher, _teacherM).GetAsync();
        Assert.Equal("사랑반", b.Rows.Single(r => r.AccountId == _me).Badge);
        Assert.Equal("보호자", b.Rows.Single(r => r.AccountId == _hee).Badge);
        Assert.Equal("교사 · 관리자", b.Rows.Single(r => r.AccountId == _teacher).Badge);
    }

    [Fact]
    public async Task 관리자가_공개를_켜고_끈다()
    {
        await As(_teacher, _teacherM, boardPublic: true).SetPublicAsync(false);
        using var raw = InMemoryDb.Raw(_db);
        Assert.False(raw.Tenants.Single(t => t.Id == _kids.Id).ReadingBoardPublic);
    }
}
