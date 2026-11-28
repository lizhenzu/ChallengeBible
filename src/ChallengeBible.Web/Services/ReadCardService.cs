using ChallengeBible.Infrastructure.Data;

namespace ChallengeBible.Web.Services;

/// <summary>읽음 표시 카드의 숫자: 구절 범위의 전체 절 수와 내가 읽음 표시한 절 수.</summary>
public sealed record ReadState(int Total, int Read)
{
    public bool Complete => Total > 0 && Read >= Total;
}

/// <summary>
/// 읽음 표시 카드(2026-11-28 사용자 시안 「읽음 표시 카드 B」, R-113 개정)의 공용 계산.
/// 말씀 미션과 성경 읽기 화면이 같은 읽음 기록(BibleMark)과 같은 미션 기록(MissionActivity)을 쓴다.
/// </summary>
public sealed class ReadCardService(BibleTextService bible, BibleMarkService marks)
{
    public async Task<ReadState> StateAsync(BibleRef range, CancellationToken ct = default)
    {
        if (!range.IsValid) return new ReadState(0, 0);
        var all = await bible.RangeAsync(range, [BibleTranslation.Krv], ct);
        if (all.Count == 0) return new ReadState(0, 0);
        var m = await marks.MarksAsync(range, ct);
        return new ReadState(all.Count, all.Count(l => m.Read.Contains((l.Chapter, l.Verse))));
    }
}
