using ChallengeBible.Infrastructure.Data;

namespace ChallengeBible.Tests.Support;

/// <summary>확정 시안과 같은 예시 콘텐츠(2027-01-05: 믿음의 종의 기도 · 신뢰 · 요한복음 3:16).</summary>
public static class ContentSeed
{
    /// <summary>시안 예시 구절의 본문(가짜 문구): 창세기 24:1~9 · 잠언 3:5~6 · 요한복음 3:16, 번역 3개(KRV 1 · KLB 2 · NIV 3).</summary>
    public static void Bible(string db)
    {
        using var raw = InMemoryDb.Raw(db);
        void Add(byte book, short ch, short from, short to)
        {
            for (short v = from; v <= to; v++)
                foreach (var (id, tag) in new (byte, string)[] { (1, "KRV"), (2, "KLB"), (3, "NIV") })
                    raw.BibleVerses.Add(new BibleVerse { TranslationId = id, BookId = book, Chapter = ch, Verse = v, Text = $"{tag} {book}:{ch}:{v}" });
        }
        Add(1, 24, 1, 9); Add(20, 3, 5, 6); Add(43, 3, 16, 16);
        raw.SaveChanges();
    }

    public static DailyWord Word(string db, DateOnly date, int? tenantId = null, string title = "믿음의 종의 기도", string status = ContentStatus.Published)
    {
        using var raw = InMemoryDb.Raw(db);
        var w = new DailyWord { TenantId = tenantId, ExposeDate = date, Status = status, Title = title, Summary = "아브라함의 종은 먼저 기도했어요.",
            BookId = 1, StartChapter = 24, StartVerse = 1, EndChapter = 24, EndVerse = 9 };
        raw.DailyWords.Add(w);
        raw.SaveChanges();
        return w;
    }

    public static DailyPrayer Prayer(string db, DateOnly date)
    {
        using var raw = InMemoryDb.Raw(db);
        var p = new DailyPrayer { ExposeDate = date, Status = ContentStatus.Published, TraitName = "신뢰", TraitNameEn = "Trust",
            Paragraphs = "[\"하나님, {이름}이(가) 하나님을 온 마음으로 믿게 해 주세요.\"]", BookId = 20, StartChapter = 3, StartVerse = 5, EndChapter = 3, EndVerse = 6 };
        raw.DailyPrayers.Add(p);
        raw.SaveChanges();
        return p;
    }

    public static DailyQuiz ChoiceQuiz(string db, DateOnly date)
    {
        using var raw = InMemoryDb.Raw(db);
        var q = new DailyQuiz { ExposeDate = date, Status = ContentStatus.Published, Question = "종은 우물가에서 가장 먼저 무엇을 했나요?",
            Explanation = "먼저 하나님께 기도했어요(창 24:12)." };
        q.Choices = [new() { Text = "물을 마셨어요", SortOrder = 1 }, new() { Text = "하나님께 기도했어요", SortOrder = 2 }, new() { Text = "잠을 잤어요", SortOrder = 3 }];
        raw.DailyQuizzes.Add(q);
        raw.SaveChanges();
        q.CorrectChoiceId = q.Choices[1].Id;
        raw.SaveChanges();
        return q;
    }

    public static DailyQuiz TextQuiz(string db, DateOnly date, params string[] accepted)
    {
        using var raw = InMemoryDb.Raw(db);
        var q = new DailyQuiz { ExposeDate = date, Status = ContentStatus.Published, Question = "아브라함의 아들 이름은?", Format = QuizFormat.Text,
            AcceptedAnswers = accepted.Select(a => new DailyQuizAcceptedAnswer { NormalizedText = ChallengeBible.Domain.Missions.QuizPolicy.Normalize(a) }).ToList() };
        raw.DailyQuizzes.Add(q);
        raw.SaveChanges();
        return q;
    }

    public static WeeklyVerse Verse(string db, DateOnly start)
    {
        using var raw = InMemoryDb.Raw(db);
        var v = new WeeklyVerse { StartDate = start, EndDate = start.AddDays(6), Status = ContentStatus.Published, Title = "하나님은 우리를 얼마나 사랑하실까요?",
            BookId = 43, StartChapter = 3, StartVerse = 16, EndChapter = 3, EndVerse = 16 };
        raw.WeeklyVerses.Add(v);
        raw.SaveChanges();
        return v;
    }
}
