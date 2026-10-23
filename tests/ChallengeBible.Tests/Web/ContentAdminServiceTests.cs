using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Web;

/// <summary>공용 콘텐츠 편집(R-120 ~ R-124).</summary>
public class ContentAdminServiceTests
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly string _db = InMemoryDb.NewName();
    private readonly Account _op;
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private ContentAdminService Svc => new(new InMemorySystemDb(_db), _time);

    public ContentAdminServiceTests()
    {
        _op = InMemoryDb.SeedAccount(_db, "operator", "운영자");
        using var raw = InMemoryDb.Raw(_db);
        raw.Accounts.Single().IsPlatformAdmin = true;
        raw.SaveChanges();
    }

    private static ContentInput Word(string title = "믿음의 종의 기도") => new()
    {
        Date = Jan5, Title = title, Summary = "종은 먼저 기도했어요.", Book = 1, StartChapter = 24, StartVerse = 1, EndChapter = 24, EndVerse = 9,
    };

    [Fact]
    public async Task 초안은_아이들에게_안_보이고_공개하면_보인다_R123()
    {
        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Word, Word(), publish: false)).Ok);
        var month = await Svc.MonthAsync(_op.Id, ContentKind.Word, 2027, 1);
        Assert.Equal(31, month.Count);
        Assert.Equal(ContentStatus.Draft, month[4].Status);
        Assert.Equal("None", month[3].Status);

        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Word, Word(), publish: true)).Ok);
        using var raw = InMemoryDb.Raw(_db);
        var w = raw.DailyWords.Single();
        Assert.Equal((ContentStatus.Published, 1, (int?)null), (w.Status, w.Revision, w.TenantId));
    }

    [Fact]
    public async Task R124_공개된_것을_고치면_새_개정_예전_기록은_그대로()
    {
        await Svc.SaveAsync(_op.Id, ContentKind.Word, Word("처음 제목"), publish: true);
        int oldId;
        using (var raw = InMemoryDb.Raw(_db)) oldId = raw.DailyWords.Single().Id;

        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Word, Word("고친 제목"), publish: true)).Ok);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var all = raw.DailyWords.OrderBy(x => x.Revision).ToList();
            Assert.Equal(2, all.Count);
            Assert.Equal((false, ContentStatus.Archived), (all[0].IsCurrent, all[0].Status));
            Assert.Equal((true, 2, oldId, "고친 제목"), (all[1].IsCurrent, all[1].Revision, all[1].ReplacesId!.Value, all[1].Title));
        }
        // 공개된 것을 초안으로 되돌릴 수는 없다
        Assert.False((await Svc.SaveAsync(_op.Id, ContentKind.Word, Word(), publish: false)).Ok);
    }

    [Fact]
    public async Task 범위_검사()
    {
        var bad = Word();
        bad.EndChapter = 51;   // 창세기는 50장
        Assert.Contains("50장", (await Svc.SaveAsync(_op.Id, ContentKind.Word, bad, true))["Range"]);
        var back = Word();
        back.EndVerse = 0;
        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Word, back, true)).ContainsKey("Range"));
    }

    [Fact]
    public async Task 퀴즈_객관식은_정답_보기가_있어야_하고_저장하면_미션에서_맞힐_수_있다()
    {
        var q = new ContentInput { Date = Jan5, Question = "종은 먼저 무엇을 했나요?", Choices = ["물을 마셨어요", "기도했어요", "", ""], CorrectIndex = -1 };
        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Quiz, q, true)).ContainsKey("Choices"));
        q.CorrectIndex = 1;
        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Quiz, q, true)).Ok);

        using (var raw = InMemoryDb.Raw(_db))
        {
            var quiz = raw.DailyQuizzes.Include(x => x.Choices).Single();
            Assert.Equal(2, quiz.Choices.Count);
            Assert.Equal("기도했어요", quiz.Choices.Single(c => c.Id == quiz.CorrectChoiceId).Text);
            Assert.Equal(ContentStatus.Published, quiz.Status);
        }

        // 다시 열어 고치면(새 개정) 보기가 그대로 따라온다
        var (loaded, status, _) = await Svc.LoadAsync(_op.Id, ContentKind.Quiz, Jan5);
        Assert.Equal((ContentStatus.Published, 1), (status, loaded.CorrectIndex));
        loaded.Explanation = "창세기 24:12";
        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Quiz, loaded, true)).Ok);

        // 부서 미션 화면에서 맞힌다
        var kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var a = InMemoryDb.SeedAccount(_db, "cheolsu", "김철수");
        var m = InMemoryDb.SeedMembership(_db, kids.Id, a.Id, MembershipStatus.Approved, Roles.Participant);
        int pid, correct;
        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = new Participant { TenantId = kids.Id, MembershipId = m.Id };
            raw.Participants.Add(p);
            raw.SaveChanges();
            pid = p.Id;
            correct = raw.DailyQuizzes.Single(x => x.IsCurrent).CorrectChoiceId!.Value;
        }
        var scope = new RequestScope();
        scope.SetTenant(kids.Id);
        var tctx = new TenantContext();
        tctx.Set(kids, null);
        var missions = new MissionService(new InMemoryDb(_db, scope), tctx, new TenantClock(_time), _time);
        Assert.Equal(1, (await missions.AnswerQuizAsync(pid, Jan5, correct, null)).Added);
    }

    [Fact]
    public async Task 퀴즈_주관식은_맞는_답을_정규화해서_저장()
    {
        var q = new ContentInput { Date = Jan5, Question = "아브라함의 아들 이름은?", Format = QuizFormat.Text, AcceptedAnswers = "이삭\n Isaac " };
        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Quiz, q, true)).Ok);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(["isaac", "이삭"], raw.DailyQuizAcceptedAnswers.Select(x => x.NormalizedText).ToList().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task 기도는_줄마다_문단()
    {
        var p = new ContentInput { Date = Jan5, TraitName = "신뢰", Paragraphs = "하나님, {이름}이(가) 믿게 해 주세요.\n\n두 번째 문단", Book = 20, StartChapter = 3, StartVerse = 5, EndChapter = 3, EndVerse = 6 };
        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Prayer, p, true)).Ok);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal("[\"하나님, {이름}이(가) 믿게 해 주세요.\",\"두 번째 문단\"]", raw.DailyPrayers.Single().Paragraphs);
    }

    [Fact]
    public async Task 공용_암송은_주_번호_1_55_날짜_없음_0052()
    {
        var r = new ContentInput { WeekNo = 56, Title = "하나님은 우리를 얼마나 사랑하실까요?", Book = 43, StartChapter = 3, StartVerse = 16, EndChapter = 3, EndVerse = 16 };
        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Recite, r, true)).ContainsKey("Date"));
        r.WeekNo = 1;
        Assert.True((await Svc.SaveAsync(_op.Id, ContentKind.Recite, r, false)).Ok);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var v = raw.WeeklyVerses.Single();
            Assert.Equal(((byte?)1, (DateOnly?)null, (DateOnly?)null), (v.WeekNo, v.StartDate, v.EndDate));
        }

        var weeks = await Svc.WeeksAsync(_op.Id);
        Assert.Equal(55, weeks.Count);
        Assert.Equal((ContentStatus.Draft, "None"), (weeks[0].Status, weeks[1].Status));
        Assert.Equal((1, 0), await Svc.PublishWeeksAsync(_op.Id, [1]));
        Assert.Equal(ContentStatus.Published, (await Svc.LoadWeekAsync(_op.Id, 1)).Status);
    }

    [Fact]
    public async Task 고른_초안_한꺼번에_공개()
    {
        await Svc.SaveAsync(_op.Id, ContentKind.Word, Word(), publish: false);
        var w6 = Word();
        w6.Date = Jan5.AddDays(1);
        await Svc.SaveAsync(_op.Id, ContentKind.Word, w6, publish: false);
        var (ok, skipped) = await Svc.PublishManyAsync(_op.Id, ContentKind.Word, [Jan5, Jan5.AddDays(1)]);
        Assert.Equal((2, 0), (ok, skipped));
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_못_쓴다()
    {
        var t = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.SaveAsync(t.Id, ContentKind.Word, Word(), true));
    }
}
