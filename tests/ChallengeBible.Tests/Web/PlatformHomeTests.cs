using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>플랫폼 홈(확정 시안: 플랫폼-홈). 오늘 = 2027-01-05 화요일(서울).</summary>
public class PlatformHomeTests
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem, _gone;
    private readonly int _op, _admin;

    public PlatformHomeTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _gone = InMemoryDb.SeedTenant(_db, "old-dept", "OLDDEP", TenantStatus.Suspended);
        _op = InMemoryDb.SeedAccount(_db, "op@example.com", "운영자").Id;
        _admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜").Id;
        var kid = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").Id;
        var kid2 = InMemoryDb.SeedAccount(_db, "seoyeon@example.com", "박서연").Id;
        var waiting = InMemoryDb.SeedAccount(_db, "minjun@example.com", "최민준").Id;
        InMemoryDb.SeedMembership(_db, _kids.Id, _admin, MembershipStatus.Approved, Roles.TenantAdmin);
        var km = InMemoryDb.SeedMembership(_db, _kids.Id, kid, MembershipStatus.Approved, Roles.Participant);
        var km2 = InMemoryDb.SeedMembership(_db, _kids.Id, kid2, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _kids.Id, waiting, MembershipStatus.Requested, Roles.Participant);

        var now = _time.GetUtcNow().UtcDateTime;
        using var raw = InMemoryDb.Raw(_db);
        raw.Accounts.Single(a => a.Id == _op).IsPlatformAdmin = true;
        foreach (var a in raw.Accounts) a.CreatedUtc = now.AddDays(-30);
        raw.Accounts.Single(a => a.Id == kid2).CreatedUtc = now.AddDays(-2);           // 이번 주 새 계정
        raw.Memberships.Single(m => m.AccountId == waiting).RequestedUtc = now.AddDays(-5);   // 오래 기다리는 신청
        var p1 = new Participant { TenantId = _kids.Id, MembershipId = km.Id };
        var p2 = new Participant { TenantId = _kids.Id, MembershipId = km2.Id };
        raw.Participants.AddRange(p1, p2);
        raw.SaveChanges();
        raw.Activities.Add(new Activity { TenantId = _kids.Id, ParticipantId = p1.Id, MissionItem = "Word.Me", MissionDate = Jan5 });
        raw.Activities.Add(new Activity { TenantId = _kids.Id, ParticipantId = p1.Id, MissionItem = "Prayer.Me", MissionDate = Jan5 });
        raw.Activities.Add(new Activity { TenantId = _kids.Id, ParticipantId = p2.Id, MissionItem = "Word.Me", MissionDate = Jan5.AddDays(-1) });   // 어제
        raw.ChapterReads.Add(new ChapterRead { AccountId = kid, RoundNo = 1, BookId = 1, Chapter = 1, ReadUtc = now });
        raw.ChapterReads.Add(new ChapterRead { AccountId = kid, RoundNo = 1, BookId = 1, Chapter = 2, ReadUtc = now });
        raw.ChapterReads.Add(new ChapterRead { AccountId = kid2, RoundNo = 1, BookId = 1, Chapter = 1, ReadUtc = now.AddDays(-1) });
        raw.AuditLogs.Add(new AuditLog { OccurredUtc = now.AddHours(-1), ActorAccountId = _admin, Action = SeasonService.LogAction, TenantId = _kids.Id, Detail = "2027 시즌 시작일 1월 1일 → 1월 3일" });
        raw.SaveChanges();
    }

    private PlatformHomeService Service() => new(new InMemorySystemDb(_db), new TenantClock(_time));

    [Fact]
    public async Task 숫자_4칸은_부서마다_오늘_미션_참여와_한국_시간_오늘_통독()
    {
        var h = await Service().GetAsync(_op);
        Assert.Equal((2, 1), (h.ActiveTenants, h.SuspendedTenants));
        Assert.Equal((5, 1), (h.Accounts, h.NewAccounts));
        Assert.Equal(1, h.TodayParticipants);                            // 오늘 한 사람(두 미션이어도 1명)
        Assert.Equal((1, 2), (h.TodayReaders, h.TodayChapters));
        var kids = h.Tenants.Single(t => t.Id == _kids.Id);
        Assert.Equal((1, 2, 1, 1), (kids.TodayParticipants, kids.Participants, kids.Readers, kids.Pending));
        Assert.True(h.Tenants.Single(t => t.Id == _gone.Id).Suspended);
    }

    [Fact]
    public async Task 챙길_일은_빈_공용_콘텐츠_관리자_없는_부서_오래_기다리는_신청()
    {
        ContentSeed.Word(_db, Jan5, status: ContentStatus.Draft);
        var h = await Service().GetAsync(_op);
        var empty = h.Todos.Single(t => t.Title == "곧 비는 공용 콘텐츠");
        Assert.Equal(14 * 4, empty.Count);                                 // 공용 콘텐츠가 하나도 공개되지 않았다
        Assert.StartsWith("1월 5일(화) 말씀 · 오늘", empty.Text);
        Assert.Equal("/platform/content/word?month=2027-01&date=2027-01-05", empty.Href);
        Assert.Contains("말씀 1건(1/5~)", h.Todos.Single(t => t.Title == "공개 전 공용 초안").Text);
        Assert.Equal("jamsil-elem 부서", h.Todos.Single(t => t.Title == "관리자가 없는 부서").Text);   // 정지한 부서는 빼고
        Assert.Equal(1, h.Todos.Single(t => t.Title.Contains("기다리는 참여 신청")).Count);
    }

    [Fact]
    public async Task 최근_관리_기록은_모든_부서에서_부서_이름과_함께()
    {
        var l = Assert.Single((await Service().GetAsync(_op)).Log);
        Assert.Equal(("이은혜", _kids.Name, "2027 시즌 시작일 1월 1일 → 1월 3일"), (l.Actor, l.Tenant, l.Text));
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_거절()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().GetAsync(_admin));
    }
}
