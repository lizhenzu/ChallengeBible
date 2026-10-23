using ChallengeBible.Domain.Missions;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Tests.Web;

/// <summary>미션 수행 규칙(03 문서 2 · 3 · 7절). 오늘 = 2027-01-05(화, 서울).</summary>
public class MissionServiceTests
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly int _pid;
    private readonly FixedTime _time = FixedTime.SeoulMorning();

    public MissionServiceTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var a = InMemoryDb.SeedAccount(_db, "cheolsu", "김철수");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, a.Id, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        var p = new Participant { TenantId = _kids.Id, MembershipId = m.Id };
        raw.Participants.Add(p);
        raw.SaveChanges();
        _pid = p.Id;
    }

    private MissionService Svc(Tenant? t = null)
    {
        t ??= _kids;
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        ctx.Set(t, null);
        return new MissionService(new InMemoryDb(_db, scope), ctx, new TenantClock(_time), _time);
    }

    private int Ledger() { using var raw = InMemoryDb.Raw(_db); return raw.PointLedgers.Sum(l => l.Points); }

    [Fact]
    public async Task R20_R21_말씀_항목은_한_번만_점수()
    {
        ContentSeed.Word(_db, Jan5);
        var first = await Svc().PressAsync(_pid, MissionItem.WordMe, Jan5);
        Assert.Equal((true, 1, 1), (first.Ok, first.Added, first.SeasonPoints));
        var again = await Svc().PressAsync(_pid, MissionItem.WordMe, Jan5);
        Assert.Equal((true, 0, 1), (again.Ok, again.Added, again.SeasonPoints));
        var parent = await Svc().PressAsync(_pid, MissionItem.WordParent, Jan5);
        Assert.Equal(2, parent.SeasonPoints);
        Assert.Equal(2, Ledger());
    }

    [Fact]
    public async Task R22_그날_말씀이_없으면_못_한다()
    {
        var r = await Svc().PressAsync(_pid, MissionItem.WordMe, Jan5);
        Assert.False(r.Ok);
        Assert.Contains("말씀이 없어요", r.Message);
    }

    [Fact]
    public async Task R123_초안_콘텐츠는_없는_것과_같다()
    {
        ContentSeed.Word(_db, Jan5, status: ContentStatus.Draft);
        Assert.False((await Svc().PressAsync(_pid, MissionItem.WordMe, Jan5)).Ok);
        Assert.Null((await Svc().GetDayAsync(_pid, Jan5)).Word);
    }

    [Fact]
    public async Task R120_내_부서_덮어쓰기가_공용보다_먼저()
    {
        ContentSeed.Word(_db, Jan5, title: "공용 말씀");
        var mine = ContentSeed.Word(_db, Jan5, tenantId: _kids.Id, title: "유년부 말씀");
        var day = await Svc().GetDayAsync(_pid, Jan5);
        Assert.Equal(("유년부 말씀", mine.Id), (day.Word!.Title, day.Word.Id));
    }

    [Fact]
    public async Task 다른_부서의_덮어쓰기는_안_보인다_격리()
    {
        var other = InMemoryDb.SeedTenant(_db, "saebit-kids", "SBK2X9");
        ContentSeed.Word(_db, Jan5, title: "공용 말씀");
        ContentSeed.Word(_db, Jan5, tenantId: other.Id, title: "새빛 말씀");
        Assert.Equal("공용 말씀", (await Svc().GetDayAsync(_pid, Jan5)).Word!.Title);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-2, true)]
    [InlineData(-3, false)]   // R-10: 오늘 포함 3일
    [InlineData(1, false)]    // R-11: 미래
    public async Task R10_R11_인정_기간(int days, bool ok)
    {
        var date = Jan5.AddDays(days);
        ContentSeed.Word(_db, date);
        Assert.Equal(ok, (await Svc().PressAsync(_pid, MissionItem.WordMe, date)).Ok);
    }

    [Fact]
    public async Task R12_지난_날짜도_보기는_된다()
    {
        var old = new DateOnly(2027, 1, 1);
        ContentSeed.Word(_db, old);
        var day = await Svc().GetDayAsync(_pid, old);
        Assert.NotNull(day.Word);
        Assert.Equal(WindowResult.TooOld, day.Window);
    }

    [Fact]
    public async Task 시즌_시작일_전에는_못_한다_0028()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.TenantSeasons.Add(new TenantSeason { TenantId = _kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 10), ChangedByAccountId = 1 });
            raw.SaveChanges();
        }
        ContentSeed.Word(_db, Jan5);
        var r = await Svc().PressAsync(_pid, MissionItem.WordMe, Jan5);
        Assert.False(r.Ok);
        Assert.Contains("곧 시작해요", r.Message);
    }

    [Fact]
    public async Task R30_기도는_두_항목_따로()
    {
        ContentSeed.Prayer(_db, Jan5);
        Assert.Equal(1, (await Svc().PressAsync(_pid, MissionItem.PrayerMe, Jan5)).Added);
        Assert.Equal(1, (await Svc().PressAsync(_pid, MissionItem.PrayerOthers, Jan5)).Added);
        var day = await Svc().GetDayAsync(_pid, Jan5);
        Assert.Equal(2, day.DayPoints);
        Assert.Contains("{이름}", day.Prayer!.Paragraphs[0]);   // 표시 바꾸기는 화면에서(R-32)
    }

    [Theory]
    [InlineData("**{이름}(이)**가 자유롭게 해 주세요.", "<b class=\"nm\">○○(이)</b>가 자유롭게 해 주세요.")]
    [InlineData("하나님, {이름}을 지켜 주세요.", "하나님, <b class=\"nm\">○○</b>을 지켜 주세요.")]
    [InlineData("<script>", "&lt;script&gt;")]
    public void R32_0054_기도문_이름_자리는_동그라미_두개로_강조(string text, string html) =>
        Assert.Equal(html, ChallengeBible.Web.Pages.Mission.PrayerModel.Paragraph(text).ToString());

    [Fact]
    public void R32_0054_이름_자리_있는_판만_보인다()
    {
        string[] both = ["하나님, {이름}(이)가 자유롭게 해 주세요. 아멘!", "하나님, 제가 자유롭게 해 주세요. 아멘!"];
        Assert.Equal([both[0]], ChallengeBible.Web.Pages.Mission.PrayerModel.Shown(both));
        string[] plain = ["첫 문단", "둘째 문단"];
        Assert.Equal(plain, ChallengeBible.Web.Pages.Mission.PrayerModel.Shown(plain));
    }

    [Fact]
    public async Task R30_0053_공용_기도는_날짜_번호로_매달_반복()
    {
        ContentSeed.Prayer(_db, (byte)5, "신뢰");
        ContentSeed.Prayer(_db, (byte)31, "감사");
        var s = Svc();
        Assert.Equal("신뢰", (await s.GetDayAsync(_pid, Jan5)).Prayer!.Trait);
        Assert.Equal("신뢰", (await s.GetDayAsync(_pid, new DateOnly(2027, 2, 5))).Prayer!.Trait);   // 다음 달 5일도 5번
        Assert.Equal("감사", (await s.GetDayAsync(_pid, new DateOnly(2026, 12, 31))).Prayer!.Trait);
        Assert.Null((await s.GetDayAsync(_pid, new DateOnly(2027, 1, 6))).Prayer);   // 6번이 없음
    }

    [Fact]
    public async Task R120_0053_부서_기도는_그날에만_먼저()
    {
        ContentSeed.Prayer(_db, Jan5, "공용 5일");
        var mine = ContentSeed.TenantPrayer(_db, _kids.Id, Jan5, "유년부 기도");
        var s = Svc();
        Assert.Equal("유년부 기도", (await s.GetDayAsync(_pid, Jan5)).Prayer!.Trait);
        Assert.Equal("공용 5일", (await s.GetDayAsync(_pid, new DateOnly(2027, 2, 5))).Prayer!.Trait);   // 다음 달은 공용으로

        await s.PressAsync(_pid, MissionItem.PrayerMe, Jan5);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(mine.Id, raw.Activities.Single().DailyPrayerId);
    }

    [Fact]
    public async Task R40_R41_R44_퀴즈_오답_두_번이면_끝()
    {
        var q = ContentSeed.ChoiceQuiz(_db, Jan5);
        var wrong = q.Choices[0].Id;
        var r1 = await Svc().AnswerQuizAsync(_pid, Jan5, wrong, null);
        Assert.Equal((false, "오답이에요 (1/2) 한 번 더 도전해요."), (r1.Correct!.Value, r1.Message));
        var r2 = await Svc().AnswerQuizAsync(_pid, Jan5, wrong, null);
        Assert.Contains("2번 도전했어요", r2.Message);
        var r3 = await Svc().AnswerQuizAsync(_pid, Jan5, q.CorrectChoiceId, null);
        Assert.False(r3.Ok);
        Assert.Equal(0, Ledger());
        Assert.NotNull((await Svc().GetDayAsync(_pid, Jan5)).Quiz!.Explanation);   // 끝나면 해설
    }

    [Fact]
    public async Task R40_퀴즈_정답이면_1점_다시는_못_푼다()
    {
        var q = ContentSeed.ChoiceQuiz(_db, Jan5);
        var r = await Svc().AnswerQuizAsync(_pid, Jan5, q.CorrectChoiceId, null);
        Assert.Equal((true, 1, "정답! +1점"), (r.Correct!.Value, r.Added, r.Message));
        Assert.Equal(0, (await Svc().AnswerQuizAsync(_pid, Jan5, q.CorrectChoiceId, null)).Added);
        Assert.Equal(1, Ledger());
    }

    [Fact]
    public async Task R43_주관식은_정규화해서_맞춘다()
    {
        ContentSeed.TextQuiz(_db, Jan5, "이삭");
        Assert.True((await Svc().AnswerQuizAsync(_pid, Jan5, null, " 이 삭 ")).Correct);
    }

    [Fact]
    public async Task R50_R51_R53_암송은_주_1회_점수는_한_날에()
    {
        ContentSeed.Verse(_db, 1);
        var first = await Svc().PressAsync(_pid, MissionItem.Recite, new DateOnly(2027, 1, 4));
        Assert.Equal(1, first.Added);
        var again = await Svc().PressAsync(_pid, MissionItem.Recite, Jan5);
        Assert.Equal((0, "이번 주 암송은 이미 완료했어요."), (again.Added, again.Message));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(new DateOnly(2027, 1, 4), raw.PointLedgers.Single().MissionDate);   // 실제 수행일(R-53)
        Assert.Equal(new DateOnly(2027, 1, 3), raw.Activities.Single().ReciteStartDate);   // 실제 주 시작일(0052)
        Assert.True((await Svc().GetDayAsync(_pid, Jan5)).RecitedThisWeek);
    }

    [Fact]
    public async Task R50_0052_공용_암송은_주_번호로_매년_반복()
    {
        ContentSeed.Verse(_db, 1, "1주 구절");
        ContentSeed.Verse(_db, 52, "52주 구절");
        var s = Svc();
        // 2027-01-03(일)이 1주, 2028-01-02(일)도 1주
        var d27 = await s.GetDayAsync(_pid, Jan5);
        Assert.Equal(("1주 구절", new DateOnly(2027, 1, 3), new DateOnly(2027, 1, 9)), (d27.Recite!.Title, d27.Recite.Start, d27.Recite.End));
        var d28 = await s.GetDayAsync(_pid, new DateOnly(2028, 1, 4));
        Assert.Equal(("1주 구절", new DateOnly(2028, 1, 2)), (d28.Recite!.Title, d28.Recite.Start));
        // 2027-01-01(금) · 01-02(토)는 2026-12-27(일)에 시작한 2026년 마지막(52) 주
        var jan1 = await s.GetDayAsync(_pid, new DateOnly(2027, 1, 1));
        Assert.Equal(("52주 구절", new DateOnly(2026, 12, 27)), (jan1.Recite!.Title, jan1.Recite.Start));
    }

    [Fact]
    public async Task R120_0052_부서_암송은_그해_그_주에만_먼저()
    {
        ContentSeed.Verse(_db, 1, "공용 1주");
        var mine = ContentSeed.TenantVerse(_db, _kids.Id, new DateOnly(2027, 1, 3), "유년부 1주");
        var s = Svc();
        Assert.Equal("유년부 1주", (await s.GetDayAsync(_pid, Jan5)).Recite!.Title);
        Assert.Equal("공용 1주", (await s.GetDayAsync(_pid, new DateOnly(2028, 1, 4))).Recite!.Title);   // 새해에는 공용으로

        await s.PressAsync(_pid, MissionItem.Recite, Jan5);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(mine.Id, raw.Activities.Single().WeeklyVerseId);
    }

    [Fact]
    public async Task R64_원장에는_그때_점수가_남는다()
    {
        ContentSeed.Word(_db, Jan5);
        await Svc().PressAsync(_pid, MissionItem.WordMe, Jan5);
        using var raw = InMemoryDb.Raw(_db);
        var act = raw.Activities.Single();
        var led = raw.PointLedgers.Single();
        Assert.Equal((act.Id, 1, "Word.Me"), (led.ActivityId, led.Points, led.MissionItem));
    }

    [Fact]
    public async Task R101_단계를_넘으면_한_번만_도달()
    {
        // 2,999점까지 원장을 채워 두고 1점을 더 얻는다
        using (var raw = InMemoryDb.Raw(_db))
        {
            var filler = new Activity { TenantId = _kids.Id, ParticipantId = _pid, MissionItem = "Word.Parent", MissionDate = new DateOnly(2027, 1, 2), DailyWordId = 999, Points = 2999 };
            raw.Activities.Add(filler);
            raw.SaveChanges();
            raw.PointLedgers.Add(new PointLedger { TenantId = _kids.Id, ParticipantId = _pid, MissionDate = filler.MissionDate, MissionItem = filler.MissionItem, Points = 2999, ActivityId = filler.Id });
            raw.SaveChanges();
        }
        ContentSeed.Word(_db, Jan5);
        var r = await Svc().PressAsync(_pid, MissionItem.WordMe, Jan5);
        Assert.Equal(3000, r.SeasonPoints);
        Assert.Equal([3000], r.NewMilestones);
        ContentSeed.Prayer(_db, Jan5);
        Assert.Empty((await Svc().PressAsync(_pid, MissionItem.PrayerMe, Jan5)).NewMilestones);
    }

    [Fact]
    public async Task R72_R73_완주일은_네_미션을_모두_한_날_이번_달은_오늘까지()
    {
        var jan4 = new DateOnly(2027, 1, 4);
        foreach (var d in new[] { jan4, Jan5 }) { ContentSeed.Word(_db, d); ContentSeed.Prayer(_db, d); }
        var q4 = ContentSeed.ChoiceQuiz(_db, jan4);
        ContentSeed.Verse(_db, 1);
        var s = Svc();
        await s.PressAsync(_pid, MissionItem.WordMe, jan4);
        await s.PressAsync(_pid, MissionItem.PrayerMe, jan4);
        await s.AnswerQuizAsync(_pid, jan4, q4.CorrectChoiceId, null);
        await s.PressAsync(_pid, MissionItem.Recite, Jan5);   // 암송은 5일에 했지만 그 주라 4일도 완주(R-72)
        await s.PressAsync(_pid, MissionItem.WordMe, Jan5);

        var month = await s.MonthAsync(_pid, 2027, 1);
        Assert.Equal(5, month.Count);   // 1일 ~ 오늘(5일)
        var d4 = month.Single(x => x.Date == jan4);
        Assert.Equal((1, 1, 1, 0, 3, true), (d4.Word, d4.Prayer, d4.Quiz, d4.Recite, d4.Total, d4.Complete));
        var d5 = month.Single(x => x.Date == Jan5);
        Assert.Equal((2, false), (d5.Total, d5.Complete));
        Assert.Empty(await s.MonthAsync(_pid, 2027, 2));   // 아직 오지 않은 달
    }

    [Fact]
    public async Task 승인하면_참여자_행이_생기고_역할을_빼면_멈춘다()
    {
        var admin = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
        var kid = InMemoryDb.SeedAccount(_db, "minjun", "최민준");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, kid.Id, MembershipStatus.Requested, Roles.Participant);
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        var tctx = new TenantContext();
        tctx.Set(_kids, null);
        var members = new MemberAdminService(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), tctx, _time);

        await members.ApproveAsync(admin.Id, m.Id, [Roles.Participant], null);
        using (var raw = InMemoryDb.Raw(_db)) Assert.True(raw.Participants.Single(p => p.MembershipId == m.Id).IsActive);
        await members.UpdateAsync(admin.Id, m.Id, [Roles.Guardian], null);
        using (var raw = InMemoryDb.Raw(_db)) Assert.False(raw.Participants.Single(p => p.MembershipId == m.Id).IsActive);
    }
}
