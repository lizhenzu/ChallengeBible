using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace ChallengeBible.Web.Pages.Mission;

// 확정 시안: 말씀-미션홈 · 말씀-말씀미션 · 말씀-퀴즈미션 · 말씀-기도미션 · 말씀-암송미션

/// <summary>_MissionHead 모델.</summary>
public sealed record MissionHead(MissionPageBase Page, string Label, string Title, string? Ref, string? TitleEn = null);

/// <summary>_PickSection 모델: "어떻게 했나요?" 선택 구역(말씀 · 기도).</summary>
public sealed record PickSection(MissionPageBase Page, string No, string Title, ChallengeBible.Domain.Rules.Mission Mission, string Api);

/// <summary>_MissionNav 모델.</summary>
public sealed record MissionNav(MissionPageBase Page, string Current);

public class HomeModel(MissionService missions, TenantContext tenant) : MissionPageBase(missions, tenant)
{
    public override string Title => "미션 홈";
    public IReadOnlyList<(DateOnly Date, int Points)> Week { get; private set; } = [];
    public int? NextMilestone { get; private set; }
    public DateOnly SeasonStart { get; private set; }

    /// <summary>`?month=yyyy-MM` 달력에 보일 달(없으면 보고 있는 날의 달).</summary>
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    public DateOnly FirstOfMonth { get; private set; }
    public IReadOnlyList<DayScore> MonthDays { get; private set; } = [];
    public IReadOnlySet<DateOnly> CompleteDates { get; private set; } = new HashSet<DateOnly>();
    public int Streak { get; private set; }

    protected override async Task LoadMoreAsync(CancellationToken ct)
    {
        Week = ParticipantId is int p ? await Missions.RecentDaysAsync(p, Day, ct) : [];
        NextMilestone = MissionService.NextMilestone(Data!.SeasonPoints);
        SeasonStart = await Missions.SeasonStartAsync(Data.Today.Year, ct);

        FirstOfMonth = DateOnly.TryParseExact(Month + "-01", "yyyy-MM-dd", out var m) ? m : new DateOnly(Day.Year, Day.Month, 1);
        if (FirstOfMonth > new DateOnly(Data.Today.Year, Data.Today.Month, 1)) FirstOfMonth = new DateOnly(Data.Today.Year, Data.Today.Month, 1);
        if (ParticipantId is int pid)
        {
            MonthDays = await Missions.MonthAsync(pid, FirstOfMonth.Year, FirstOfMonth.Month, ct);
            // 최근 7일 막대의 "네 가지 다 한 날" 표시: 보고 있는 날의 달과 그 앞 달에서 찾는다
            var dayMonth = new DateOnly(Day.Year, Day.Month, 1);
            var complete = new HashSet<DateOnly>();
            foreach (var first in new[] { dayMonth, dayMonth.AddMonths(-1) })
            {
                var days = first == FirstOfMonth ? MonthDays : await Missions.MonthAsync(pid, first.Year, first.Month, ct);
                foreach (var s in days.Where(s => s.Complete)) complete.Add(s.Date);
            }
            CompleteDates = complete;
            Streak = await Missions.StreakAsync(pid, Data.Today, ct);
        }
    }

    public DayScore? ScoreOf(DateOnly d) => MonthDays.FirstOrDefault(s => s.Date == d);

    /// <summary>일요일부터 시작하는 6주 42칸.</summary>
    public IEnumerable<DateOnly> Cells()
    {
        var start = FirstOfMonth.AddDays(-(int)FirstOfMonth.DayOfWeek);
        return Enumerable.Range(0, 42).Select(i => start.AddDays(i));
    }

    public bool HasPrevMonth => FirstOfMonth > new DateOnly(SeasonStart.Year, SeasonStart.Month, 1);
    public bool HasNextMonth => FirstOfMonth < new DateOnly(Data!.Today.Year, Data.Today.Month, 1);

    /// <summary>미션 홈 주소: 보는 날(`date`)과 달력 달(`month`)을 함께 싣는다.</summary>
    public string HomeUrl(DateOnly? date = null, DateOnly? month = null)
    {
        var d = date ?? Day;
        var q = new List<string>();
        if (d != Data!.Today) q.Add($"date={d:yyyy-MM-dd}");
        if (month is DateOnly mm) q.Add($"month={mm:yyyy-MM}");
        return $"/{Slug}/word-mission" + (q.Count == 0 ? "" : "?" + string.Join("&", q));
    }
}

public class WordModel(MissionService missions, TenantContext tenant) : MissionPageBase(missions, tenant)
{
    public override string Title => "말씀 미션";
}

public class QuizModel(MissionService missions, TenantContext tenant) : MissionPageBase(missions, tenant)
{
    public override string Title => "퀴즈 미션";
    public int? CorrectChoiceId { get; private set; }

    protected override async Task LoadMoreAsync(CancellationToken ct)
    {
        var s = Data!.QuizState;
        if (Data.Quiz is { Format: Infrastructure.Data.QuizFormat.Choice } q && (s.Solved || s.AttemptsUsed >= s.MaxAttempts))
            CorrectChoiceId = await Missions.CorrectChoiceAsync(q.Id, ct);
    }
}

public class PrayerModel(MissionService missions, TenantContext tenant) : MissionPageBase(missions, tenant)
{
    public override string Title => "기도 미션";

    /// <summary>R-32: 기도문의 {이름} 자리에 참여자 이름(강조).</summary>
    public Microsoft.AspNetCore.Html.IHtmlContent Paragraph(string text, string name) =>
        new Microsoft.AspNetCore.Html.HtmlString(string.Join($"<b class=\"nm\">{System.Net.WebUtility.HtmlEncode(name)}</b>",
            text.Split("{이름}").Select(System.Net.WebUtility.HtmlEncode)));
}

public class ReciteModel(MissionService missions, TenantContext tenant) : MissionPageBase(missions, tenant)
{
    public override string Title => "암송 미션";
}
