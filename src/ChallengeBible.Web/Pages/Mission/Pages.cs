using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Web.Pages.Mission;

// 확정 시안: 말씀-미션홈 · 말씀-말씀미션 · 말씀-퀴즈미션 · 말씀-기도미션 · 말씀-암송미션

/// <summary>_MissionHead 모델.</summary>
public sealed record MissionHead(MissionPageBase Page, string Label, string Title, string? Ref);

/// <summary>_MissionNav 모델.</summary>
public sealed record MissionNav(MissionPageBase Page, string Current);

public class HomeModel(MissionService missions, TenantContext tenant) : MissionPageBase(missions, tenant)
{
    public override string Title => "미션 홈";
    public IReadOnlyList<(DateOnly Date, int Points)> Week { get; private set; } = [];
    public int? NextMilestone { get; private set; }
    public DateOnly SeasonStart { get; private set; }

    protected override async Task LoadMoreAsync(CancellationToken ct)
    {
        Week = ParticipantId is int p ? await Missions.RecentDaysAsync(p, Day, ct) : [];
        NextMilestone = MissionService.NextMilestone(Data!.SeasonPoints);
        SeasonStart = await Missions.SeasonStartAsync(Data.Today.Year, ct);
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
