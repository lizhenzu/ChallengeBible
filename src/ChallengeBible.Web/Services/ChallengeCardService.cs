using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Web.Services;

/// <summary>오늘 미션 한 칸(말씀 · 퀴즈 · 기도 · 암송). 체크 표시 대신 상태 글자를 쓴다(2026-11-23 사용자).</summary>
public sealed record ChallengeChip(string Label, bool Has, bool Done, string Key = "")
{
    /// <summary>완료 · 대기중 · —(그날 콘텐츠 없음).</summary>
    public string State => !Has ? "—" : Done ? "완료" : "대기중";
}

/// <summary>프로그램별 진행 한 줄(성경통독 · 성경필사 · 365축복기도). Started=false 면 아직 시작 전.</summary>
public sealed record ChallengeRow(string Key, string Label, bool Started, double Percent, string Sub);

/// <summary>성장 단계 한 칸(씨앗 · 새싹 · 나무 · 숲). Reached = 지나온 단계(지금 단계 포함), Now = 지금 단계.</summary>
public sealed record GrowthStep(string Name, string Emoji, bool Reached, bool Now);

/// <summary>
/// 나의 성장 단계(2026-11-23 사용자 확정 B안) — 「성장 3단계 · 나무」와 다음 단계까지 남은 점수.
/// Stage 는 1부터 세는 단계 번호, Count 는 전체 단계 수. Done 이면 마지막 단계를 넘었다.
/// Percent 는 이번 단계 안에서 자란 정도(0 ~ 100) — 홈 상단 링의 호가 이 값을 쓴다.
/// </summary>
public sealed record GrowthView(int Stage, int Count, string Name, string Emoji, int Percent, int? NextUntil, int ToNext, bool Done, IReadOnlyList<GrowthStep> Steps);

/// <summary>이번 주 하루(일~토). 새 「나의 도전」 화면의 막대 그래프가 쓴다(ADR 0136).</summary>
public sealed record WeekBar(string Day, int Points, bool AllFour, int Height);

/// <summary>홈 상단 포인트 링(ADR 0136 · 시안 `홈-상단-포인트링-3안.html` B안 — 숫자 링). Href 가 있으면 눌러서 「나의 도전」 화면으로.</summary>
public sealed record PointRing(int Points, GrowthView Growth, string? Href);

/// <summary>
/// "나의 도전 현황" 통합 카드(2026-11-23 사용자 확정: 시안 A — docs/design/시안/홈-나의도전-통합카드-단독시안.html).
/// 오늘 미션 4칸 + 프로그램 진행률 + 나의 포인트 · 성장 단계 + 이번 주 막대를 카드 하나로 모은다.
/// 2026-11-23 사용자 확정(ADR 0136): 이 카드는 홈에서 빼고 `/{부서}/challenge` 화면이 그린다.
/// </summary>
public sealed record ChallengeCard(
    IReadOnlyList<ChallengeChip> Chips, int Done, int Total, IReadOnlyList<ChallengeRow> Rows,
    int Points, GrowthView? Growth, int WeekCount, int Members, int TodayPeople, IReadOnlyList<WeekBar> Week);

/// <summary>
/// 통합 카드 값 만들기. 부서 홈 · 첫 화면(`/`)이 같은 카드를 그린다.
/// 이미 계산한 <see cref="HomeView"/>를 받아 미션 · 점수 · 성장 · 부서 숫자를 다시 세지 않고,
/// 없던 것(필사 · 365축복기도)만 여기서 더한다. 부서가 끈 프로그램은 줄에서 뺀다.
/// </summary>
public sealed class ChallengeCardService(TenantContext tenant, RequestScope scope, CopyService copy, Prayer365Service prayer)
{
    public async Task<ChallengeCard> GetAsync(HomeView data, bool platformAdmin, CancellationToken ct = default)
    {
        var t = tenant.Current;
        var chips = data.Participant && t.MissionEnabled
            ? data.Missions.Select(m => new ChallengeChip(m.Label, m.Has, m.Done, m.Key)).ToList()
            : [];
        var rows = await RowsAsync(t, data, platformAdmin, ct);

        return new(chips, chips.Count(c => c.Done), chips.Count(c => c.Has), rows,
            data.SeasonPoints, GrowthOf(data), data.Week.Count(w => w.MissionPoints > 0),
            data.Dept?.Members ?? 0, data.Dept?.TodayPeople ?? 0, WeekOf(data));
    }

    /// <summary>
    /// 이번 주 막대(일~토) — 점수는 `HomeView.Week` 그대로, 높이는 그 주 최고 점수에 대한 비율(0 ~ 100, 0점은 6).
    /// 참여자가 아니면 빈 목록(그래프를 그리지 않는다).
    /// </summary>
    private static IReadOnlyList<WeekBar> WeekOf(HomeView data)
    {
        if (!data.Participant || data.Week.Count == 0) return [];
        string[] names = ["일", "월", "화", "수", "목", "금", "토"];
        var max = data.Week.Max(w => w.MissionPoints);
        return data.Week.Select(w => new WeekBar(
            names[(int)w.Date.DayOfWeek], w.MissionPoints, w.AllFour,
            w.MissionPoints <= 0 || max <= 0 ? 6 : Math.Max(14, (int)Math.Round(w.MissionPoints * 100.0 / max)))).ToList();
    }

    /// <summary>홈 상단 포인트 링 — 참여자만. 누르면 「나의 도전」 화면(ADR 0136).</summary>
    public static PointRing? RingOf(HomeView data, string slug)
    {
        var g = GrowthOf(data);
        return g is null ? null : new(data.SeasonPoints, g, $"/{slug}/challenge");
    }

    /// <summary>성장 단계 — 값은 규칙(`Rules.GrowthStages`) 그대로. 참여자가 아니면 null.</summary>
    private static GrowthView? GrowthOf(HomeView data)
    {
        var stages = data.Rules.GrowthStages;
        if (!data.Participant || data.Growth is not { } g || stages.Count == 0) return null;
        var i = Math.Clamp(g.Stage, 0, stages.Count - 1);
        var s = stages[i];
        return new(i + 1, stages.Count, s.Name, s.Emoji, g.Percent, g.Done ? null : s.Until, g.ToNext, g.Done,
            stages.Select((x, n) => new GrowthStep(x.Name, x.Emoji, n <= i, n == i)).ToList());
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
            rows.Add(new("prayer", "365축복기도", p.PrayedDays > 0, p.ProgressPct, $"{p.PrayedDays:N0} / {Prayer365Stats.TotalDays}일"));
        }
        return rows;
    }

    /// <summary>화면에서 쓰는 진행 막대 너비(0.1% 단위).</summary>
    public static string Width(double pct) => Math.Clamp(pct, 0, 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
