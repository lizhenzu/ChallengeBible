using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 온가족(ADR 0065, R-175 ~ R-178). 김철수 = 유년부, 김민지 = 초등부, 김영희(엄마) = 유년부 참여자 · 보호자. 운영자 = 플랫폼 관리자.
/// 화면 흐름은 FamilyPageTests, 시상 부서(R-179)는 FamilyTenantTests · FamilyPageTests.
/// </summary>
public class OnFamilyServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem, _lobby;
    private readonly int _op, _cheolsu, _minji, _mom, _outsider;
    private readonly int _cheolsuPid, _minjiPid, _momPid;

    public OnFamilyServiceTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _lobby = InMemoryDb.SeedTenant(_db, "welcome", "WELCOM");
        _op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자").Id;
        _cheolsu = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").Id;
        _minji = InMemoryDb.SeedAccount(_db, "minji@example.com", "김민지").Id;
        _mom = InMemoryDb.SeedAccount(_db, "younghee@example.com", "김영희").Id;
        _outsider = InMemoryDb.SeedAccount(_db, "other@example.com", "박지호").Id;
        var mc = InMemoryDb.SeedMembership(_db, _kids.Id, _cheolsu, MembershipStatus.Approved, Roles.Participant);
        var mm = InMemoryDb.SeedMembership(_db, _elem.Id, _minji, MembershipStatus.Approved, Roles.Participant);
        var mo = InMemoryDb.SeedMembership(_db, _kids.Id, _mom, MembershipStatus.Approved, Roles.Participant, Roles.Guardian);
        var lobbyOld = InMemoryDb.SeedMembership(_db, _lobby.Id, _cheolsu, MembershipStatus.Suspended, Roles.Participant);   // 이미 옮겨 간 임시 부서(R-173)
        var lobbyNow = InMemoryDb.SeedMembership(_db, _lobby.Id, _minji, MembershipStatus.Approved, Roles.Participant);    // 아직 임시 부서에도 있음
        using var raw = InMemoryDb.Raw(_db);
        raw.Accounts.Single(a => a.Id == _op).IsPlatformAdmin = true;
        raw.Tenants.Single(t => t.Id == _lobby.Id).IsLobby = true;
        var pc = new Participant { TenantId = _kids.Id, MembershipId = mc.Id };
        var pm = new Participant { TenantId = _elem.Id, MembershipId = mm.Id };
        var po = new Participant { TenantId = _kids.Id, MembershipId = mo.Id, IsActive = false };   // 역할을 뺀 참여자도 센다
        var pl1 = new Participant { TenantId = _lobby.Id, MembershipId = lobbyOld.Id, IsActive = false };
        var pl2 = new Participant { TenantId = _lobby.Id, MembershipId = lobbyNow.Id };
        raw.Participants.AddRange(pc, pm, po, pl1, pl2);
        raw.SaveChanges();
        (_cheolsuPid, _minjiPid, _momPid) = (pc.Id, pm.Id, po.Id);
        void Pt(int tenant, int pid, int day, int points) => raw.PointLedgers.Add(new PointLedger
            { TenantId = tenant, ParticipantId = pid, MissionDate = new DateOnly(2027, 1, day), MissionItem = "Word.Me", Points = points });
        Pt(_kids.Id, pc.Id, 3, 20); Pt(_kids.Id, pc.Id, 4, 22);   // 철수 42
        Pt(_elem.Id, pm.Id, 2, 30);                               // 민지 30
        Pt(_kids.Id, po.Id, 1, 7);                                // 엄마 7
        Pt(_lobby.Id, pl1.Id, 1, 100);                            // 철수의 옮겨 간 임시 부서 — 빼고 셈
        Pt(_lobby.Id, pl2.Id, 5, 3);                              // 민지의 지금 임시 부서 — 셈
        raw.SaveChanges();
    }

    private OnFamilyService Svc() => new(new InMemorySystemDb(_db), new TenantClock(_time), _time);
    private FamilyAdminService Admin() => new(new InMemorySystemDb(_db), new FamilyTenantService(new InMemorySystemDb(_db), _time), _time);

    private async Task<int> Make(params (int Account, string Relation)[] members)
    {
        var (e, id) = await Admin().CreateAsync(_op, null, null, members, default);
        Assert.Equal(OnFamilyError.None, e);
        return id;
    }

    [Fact]
    public async Task R175_한_계정은_한_온가족에만_묶이고_묶기는_플랫폼_관리자만()
    {
        var id = await Make((_cheolsu, FamilyRelation.Student), (_mom, FamilyRelation.Mother));
        Assert.Equal(OnFamilyError.AlreadyInFamily, (await Admin().CreateAsync(_op, null, null, [(_cheolsu, FamilyRelation.Student), (_minji, FamilyRelation.Student)], default)).Error);
        Assert.Equal(2, InMemoryDb.Raw(_db).FamilyLinks.Count());                           // 하나라도 걸리면 하나도 묶지 않는다
        Assert.Equal(OnFamilyError.None, await Admin().AddAsync(_op, id, _minji, FamilyRelation.Student));
        Assert.Equal(OnFamilyError.AlreadyInFamily, await Admin().AddAsync(_op, id, _minji, FamilyRelation.Student));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Admin().AddAsync(_mom, id, _outsider, FamilyRelation.Other));   // 부모도 직접은 못 묶는다

        // 마지막 사람까지 빼면 온가족도 없어진다
        foreach (var a in new[] { _cheolsu, _mom, _minji }) Assert.True(await Admin().RemoveAsync(_op, id, a));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.Families);
        Assert.Contains(raw.AuditLogs, l => l.Action == "FamilyUnlinked" && l.Detail!.Contains("dissolved"));
    }

    [Fact]
    public async Task R176_부모가_신청하고_플랫폼_관리자가_묶으면_신청은_승인으로_닫힌다()
    {
        Assert.Equal(OnFamilyError.Empty, await Svc().RequestAsync(_mom, "  "));
        Assert.Equal(OnFamilyError.TooLong, await Svc().RequestAsync(_mom, new string('가', OnFamilyService.BodyMax + 1)));
        Assert.Equal(OnFamilyError.None, await Svc().RequestAsync(_mom, "김철수 — 유년부\n김민지 — 초등부"));
        Assert.Equal(OnFamilyError.RequestOpen, await Svc().RequestAsync(_mom, "한 번 더"));     // 처리 안 된 신청은 하나
        var req = Assert.Single(await Admin().RequestsAsync(_op));
        Assert.Equal(("김영희", "김철수 — 유년부\n김민지 — 초등부"), (req.Requester, req.Body));
        Assert.NotNull((await Svc().MyAsync(_mom)).Open);                                            // 신청만으로는 묶이지 않는다
        Assert.Null((await Svc().MyAsync(_mom)).Family);

        var (e, id) = await Admin().CreateAsync(_op, "우리집", req.Id, [(_mom, FamilyRelation.Mother), (_cheolsu, FamilyRelation.Student), (_minji, FamilyRelation.Student)], default);
        Assert.Equal(OnFamilyError.None, e);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var r = raw.FamilyRequests.Single();
            Assert.Equal((FamilyRequestStatus.Approved, (int?)id, (int?)_op), (r.Status, r.FamilyId, r.DecidedByAccountId));
        }
        Assert.Empty(await Admin().RequestsAsync(_op));
        Assert.Equal(OnFamilyError.AlreadyInFamily, await Svc().RequestAsync(_mom, "또"));             // 이미 온가족이면 신청할 필요 없음

        // 다른 사람의 신청은 거절 · 본인은 취소할 수 있다
        Assert.Equal(OnFamilyError.None, await Svc().RequestAsync(_outsider, "박지호 혼자"));
        Assert.True(await Admin().RejectAsync(_op, (await Admin().RequestsAsync(_op)).Single().Id));
        Assert.Equal(OnFamilyError.None, await Svc().RequestAsync(_outsider, "다시"));
        Assert.Equal(OnFamilyError.None, await Svc().CancelAsync(_outsider));
        Assert.Equal(OnFamilyError.NotFound, await Svc().CancelAsync(_outsider));
    }

    [Fact]
    public async Task R177_총점은_모든_부서의_누적_합_옮겨_간_임시_부서는_빼고_보기만()
    {
        await Make((_cheolsu, FamilyRelation.Student), (_minji, FamilyRelation.Student), (_mom, FamilyRelation.Mother));
        var my = await Svc().MyAsync(_cheolsu);
        var f = Assert.IsType<FamilyBoardView>(my.Family);
        Assert.Equal("김철수네 온가족", f.Name);                                                       // 이름이 비면 대표 학생
        var pts = f.People.ToDictionary(p => p.AccountId, p => p.Points);
        Assert.Equal(42, pts[_cheolsu]);              // 멈춘 임시 부서 100점은 빼고(이미 부서로 복사됨, R-174)
        Assert.Equal(33, pts[_minji]);                // 초등부 30 + 아직 있는 임시 부서 3
        Assert.Equal(7, pts[_mom]);                   // 역할을 뺀 참여자의 점수도 그대로
        Assert.Equal(82, f.Total);
        Assert.Equal(new DateOnly(2027, 1, 5), f.Last);
        Assert.Equal((my.Family!.Name, 82), (await Svc().LineAsync(_minji))!.Value);                // 부서 홈 한 줄
        Assert.Null(await Svc().LineAsync(_outsider));

        // 보기만: 가족팀 · 축하 기록에는 아무것도 쓰지 않는다
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.FamilyTeams);
        Assert.Empty(raw.FamilyMilestonesReached);
    }

    [Fact]
    public async Task R178_부서_관리자에게는_다른_부서_구성원_이름을_가리고_점수만_보인다()
    {
        var id = await Make((_cheolsu, FamilyRelation.Student), (_minji, FamilyRelation.Student), (_mom, FamilyRelation.Mother));
        var byAccount = await Svc().ForTenantAsync(_op, _kids.Id);
        Assert.Equal([_cheolsu, _mom], byAccount.Keys.Order());                                     // 유년부 사람만 키
        var f = byAccount[_cheolsu];
        Assert.Equal(id, f.Id);
        var hidden = Assert.Single(f.People, p => p.Hidden);
        Assert.Equal(("", 33), (hidden.Name, hidden.Points));                                         // 민지는 이름 · 부서를 가림
        Assert.Empty(hidden.Depts);
        Assert.Equal(82, f.Total);
        Assert.DoesNotContain(f.People, p => p.Name == "김민지");

        Assert.Empty(await Svc().ForTenantAsync(_op, InMemoryDb.SeedTenant(_db, "empty-dept", "EMPTYD").Id));
    }
}
