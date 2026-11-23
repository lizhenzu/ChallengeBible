using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Web.Services;

/// <summary>오늘 미션 한 칸(말씀 · 퀴즈 · 기도 · 암송). 체크 표시 대신 상태 글자를 쓴다(2026-11-23 사용자).</summary>
public sealed record ChallengeChip(string Label, bool Has, bool Done)
{
    /// <summary>완료 · 대기중 · —(그날 콘텐츠 없음).</summary>
    public string State => !Has ? "—" : Done ? "완료" : "대기중";
}

/// <summary>프로그램별 진행 한 줄(성경통독 · 성경필사 · 기도365). Started=false 면 아직 시작 전.</summary>
public sealed record ChallengeRow(string Key, string Label, bool Started, double Percent, string Sub);

/// <summary>
/// "나의 도전 현황" 통합 카드(2026-11-23 사용자 확정: 시안 A — docs/design/시안/홈-나의도전-통합카드-단독시안.html).
/// 오늘 미션 4칸 + 프로그램 진행률 + 아래 줄(포인트 · 순위 · 연속)을 카드 하나로 모은다.
/// </summary>
public sealed record ChallengeCard(
    IReadOnlyList<ChallengeChip> Chips, int Done, int Total, IReadOnlyList<ChallengeRow> Rows,
    int Points, int? Rank, int Streak, int WeekCount, int Members, int TodayPeople);

/// <summary>
/// 통합 카드 값 만들기. 부서 홈 · 첫 화면(`/`)이 같은 카드를 그린다.
/// 이미 계산한 <see cref="HomeView"/>를 받아 미션 · 점수 · 연속 · 부서 숫자를 다시 세지 않고,
/// 없던 것(필사 · 기도365 · 순위)만 여기서 더한다. 부서가 끈 프로그램은 줄에서 뺀다.
/// </summary>
public sealed class ChallengeCardService(TenantContext tenant, RequestScope scope, CopyService copy, Prayer365Service prayer, RankingService rankings)
{
    public async Task<ChallengeCard> GetAsync(HomeView data, bool platformAdmin, CancellationToken ct = default)
    {
        var t = tenant.Current;
        var chips = data.Participant && t.MissionEnabled
            ? data.Missions.Select(m => new ChallengeChip(m.Label, m.Has, m.Done)).ToList()
            : [];
        var rows = await RowsAsync(t, data, platformAdmin, ct);

        int? rank = null;
        if (data.Participant && data.SeasonPoints > 0 && data.Rules.Features.Ranking)
        {
            var r = await rankings.GetAsync(ct);
            if (r.Enabled) rank = r.Rows.FirstOrDefault(x => x.Me)?.Rank;
        }

        return new(chips, chips.Count(c => c.Done), chips.Count(c => c.Has), rows,
            data.SeasonPoints, rank, data.Streak, data.Week.Count(w => w.MissionPoints > 0),
            data.Dept?.Members ?? 0, data.Dept?.TodayPeople ?? 0);
    }

    /// <summary>프로그램 진행 줄. 로그인 · 승인된 소속일 때만(개인 기록이라).</summary>
    private async Task<IReadOnlyList<ChallengeRow>> RowsAsync(Tenant t, HomeView data, bool platformAdmin, CancellationToken ct)
    {
        var rows = new List<ChallengeRow>();
        if (!tenant.IsApproved || scope.UserName is null) return rows;

        if (t.ReadingEnabled)
        {
            rows.Add(data.Reading is { } rd
                ? new("reading", "성경통독", true, ReadingSummary.Percent(rd.ReadChapters, ReadingSummary.Total),
                    $"{rd.ReadChapters:N0} / {ReadingSummary.Total:N0}장" + (rd.CompletedRounds > 0 ? $" · {rd.CompletedRounds + 1}독째" : ""))
                : new("reading", "성경통독", false, 0, "아직 시작 전"));
        }
        if (t.CopyEnabled)
        {
            var c = await copy.SummaryAsync(ct);
            rows.Add(new("copy", "성경필사", c.Done > 0, c.All, $"{c.Done:N0} / {c.Total:N0}절"));
        }
        if (prayer.CanSee(platformAdmin))
        {
            var p = await prayer.MyStatsAsync(ct);
            rows.Add(new("prayer", "기도365", p.PrayedDays > 0, p.ProgressPct, $"{p.PrayedDays:N0} / {Prayer365Stats.TotalDays}일"));
        }
        return rows;
    }

    /// <summary>화면에서 쓰는 진행 막대 너비(0.1% 단위).</summary>
    public static string Width(double pct) => Math.Clamp(pct, 0, 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
