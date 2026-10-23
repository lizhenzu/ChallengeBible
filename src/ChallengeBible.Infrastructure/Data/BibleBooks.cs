namespace ChallengeBible.Infrastructure.Data;

/// <summary>성경 66권(개역개정 기준 이름 · 장 수, 합계 1,189장 — 0040). 마이그레이션 시드로 넣는다.</summary>
public static class BibleBooks
{
    public static readonly (string Name, string Short, short Chapters)[] Old =
    [
        ("창세기", "창", 50), ("출애굽기", "출", 40), ("레위기", "레", 27), ("민수기", "민", 36), ("신명기", "신", 34),
        ("여호수아", "수", 24), ("사사기", "삿", 21), ("룻기", "룻", 4), ("사무엘상", "삼상", 31), ("사무엘하", "삼하", 24),
        ("열왕기상", "왕상", 22), ("열왕기하", "왕하", 25), ("역대상", "대상", 29), ("역대하", "대하", 36), ("에스라", "스", 10),
        ("느헤미야", "느", 13), ("에스더", "에", 10), ("욥기", "욥", 42), ("시편", "시", 150), ("잠언", "잠", 31),
        ("전도서", "전", 12), ("아가", "아", 8), ("이사야", "사", 66), ("예레미야", "렘", 52), ("예레미야애가", "애", 5),
        ("에스겔", "겔", 48), ("다니엘", "단", 12), ("호세아", "호", 14), ("요엘", "욜", 3), ("아모스", "암", 9),
        ("오바댜", "옵", 1), ("요나", "욘", 4), ("미가", "미", 7), ("나훔", "나", 3), ("하박국", "합", 3),
        ("스바냐", "습", 3), ("학개", "학", 2), ("스가랴", "슥", 14), ("말라기", "말", 4),
    ];

    public static readonly (string Name, string Short, short Chapters)[] New =
    [
        ("마태복음", "마", 28), ("마가복음", "막", 16), ("누가복음", "눅", 24), ("요한복음", "요", 21), ("사도행전", "행", 28),
        ("로마서", "롬", 16), ("고린도전서", "고전", 16), ("고린도후서", "고후", 13), ("갈라디아서", "갈", 6), ("에베소서", "엡", 6),
        ("빌립보서", "빌", 4), ("골로새서", "골", 4), ("데살로니가전서", "살전", 5), ("데살로니가후서", "살후", 3), ("디모데전서", "딤전", 6),
        ("디모데후서", "딤후", 4), ("디도서", "딛", 3), ("빌레몬서", "몬", 1), ("히브리서", "히", 13), ("야고보서", "약", 5),
        ("베드로전서", "벧전", 5), ("베드로후서", "벧후", 3), ("요한일서", "요일", 5), ("요한이서", "요이", 1), ("요한삼서", "요삼", 1),
        ("유다서", "유", 1), ("요한계시록", "계", 22),
    ];

    public static IEnumerable<BibleBook> All() =>
        Old.Select((b, i) => new BibleBook { Id = (byte)(i + 1), Name = b.Name, ShortName = b.Short, Testament = "OT", ChapterCount = b.Chapters })
           .Concat(New.Select((b, i) => new BibleBook { Id = (byte)(i + 40), Name = b.Name, ShortName = b.Short, Testament = "NT", ChapterCount = b.Chapters }));

    public static string NameOf(byte id) => id <= 39 ? Old[id - 1].Name : New[id - 40].Name;

    /// <summary>"창세기 24장 1~9절", "요한복음 3:16" 같은 사람이 읽는 범위.</summary>
    public static string Range(byte book, short sc, short sv, short ec, short ev)
    {
        var n = NameOf(book);
        if (sc == ec && sv == ev) return $"{n} {sc}:{sv}";
        if (sc == ec) return $"{n} {sc}장 {sv}~{ev}절";
        return $"{n} {sc}:{sv} ~ {ec}:{ev}";
    }
}
