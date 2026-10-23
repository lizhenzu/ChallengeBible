using ChallengeBible.Domain.Rules;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Antiforgery;

namespace ChallengeBible.Web.Api;

public sealed record PressRequest(DateOnly Date, string[]? Items);
public sealed record QuizRequest(DateOnly Date, int? ChoiceId, string? AnswerText);
public sealed record DateRequest(DateOnly Date);

public sealed record MissionResponse(bool Ok, int PointsEarned, int SeasonTotal, IReadOnlyList<string> CompletedItems,
    IReadOnlyList<int> MilestonesReached, string Message, bool? IsCorrect = null, int? AttemptsLeft = null,
    int? CorrectChoiceId = null, string? Explanation = null);

/// <summary>
/// 미션 버튼 동작 JSON API(API 계약 5-1, ADR 0034). 화면은 Razor Pages, 버튼은 이 API 를 부르고 화면 일부만 바꾼다.
/// 모든 요청: 로그인 → 주소의 부서 → 승인된 소속 + 참여자 역할 → 위조 방지 토큰.
/// </summary>
public static class MissionApi
{
    public static void MapMissionApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/{slug}/missions").RequireAuthorization().AddEndpointFilter<ParticipantEndpointFilter>();

        g.MapPost("/word", (PressRequest r, MissionService s, HttpContext h) => Press(r, Mission.Word, s, h));
        g.MapPost("/prayer", (PressRequest r, MissionService s, HttpContext h) => Press(r, Mission.Prayer, s, h));

        g.MapPost("/recite", async (DateRequest r, MissionService s, HttpContext h) =>
        {
            var pid = (int)h.Items[ParticipantEndpointFilter.ParticipantKey]!;
            var res = await s.PressAsync(pid, MissionItem.Recite, r.Date, h.RequestAborted);
            return Results.Ok(new MissionResponse(res.Ok, res.Added, res.SeasonPoints, res.Ok ? ["Recite"] : [], res.NewMilestones, Msg(res)));
        });

        g.MapPost("/quiz/attempts", async (QuizRequest r, MissionService s, HttpContext h) =>
        {
            var pid = (int)h.Items[ParticipantEndpointFilter.ParticipantKey]!;
            var res = await s.AnswerQuizAsync(pid, r.Date, r.ChoiceId, r.AnswerText, h.RequestAborted);
            var day = await s.GetDayAsync(pid, r.Date, h.RequestAborted);
            var finished = day.QuizState.Solved || day.QuizState.AttemptsUsed >= day.QuizState.MaxAttempts;
            int? correctChoice = null;
            if (finished && day.Quiz?.Format == QuizFormat.Choice) correctChoice = await s.CorrectChoiceAsync(day.Quiz.Id, h.RequestAborted);
            return Results.Ok(new MissionResponse(res.Ok, res.Added, res.SeasonPoints, day.QuizState.Solved ? ["Quiz.Correct"] : [],
                res.NewMilestones, res.Added > 0 ? $"정답! +{res.Added}점 · 올해 총 {res.SeasonPoints:N0}점" : res.Message,
                res.Correct, day.QuizState.MaxAttempts - day.QuizState.AttemptsUsed, correctChoice, finished ? day.Quiz?.Explanation : null));
        });
    }

    private static async Task<IResult> Press(PressRequest r, Mission mission, MissionService s, HttpContext h)
    {
        var pid = (int)h.Items[ParticipantEndpointFilter.ParticipantKey]!;
        var items = (r.Items ?? []).Select(TryParse).ToList();
        if (items.Count == 0) return Results.BadRequest(new { code = "NoItemSelected", message = "체크를 선택해 주세요." });   // R-23
        if (items.Any(i => i is null || i.Value.Mission() != mission)) return Results.BadRequest(new { code = "BadItem", message = "알 수 없는 항목이에요." });

        var added = 0;
        var milestones = new List<int>();
        MissionResult? last = null;
        foreach (var item in items.Select(i => i!.Value).Distinct())
        {
            last = await s.PressAsync(pid, item, r.Date, h.RequestAborted);
            if (!last.Ok) return Results.Ok(new MissionResponse(false, added, last.SeasonPoints, [], milestones, last.Message));
            added += last.Added;
            milestones.AddRange(last.NewMilestones);
        }
        var day = await s.GetDayAsync(pid, r.Date, h.RequestAborted);
        var done = day.Done.Where(d => d.Mission() == mission).Select(d => d.Code()).ToList();
        var msg = added > 0 ? $"+{added}점! 올해 총 {day.SeasonPoints:N0}점" : last!.Message;
        return Results.Ok(new MissionResponse(true, added, day.SeasonPoints, done, milestones, msg));
    }

    private static string Msg(MissionResult r) => r.Ok && r.Added > 0 ? $"+{r.Added}점! 올해 총 {r.SeasonPoints:N0}점" : r.Message;

    private static MissionItem? TryParse(string code)
    {
        try { return MissionItemCodes.Parse(code); } catch (InvalidOperationException) { return null; }
    }
}

/// <summary>부서 결정 + 참여자 확인 + 위조 방지 토큰(헤더 RequestVerificationToken).</summary>
public sealed class ParticipantEndpointFilter(TenantResolver resolver, TenantContext tenant, MissionService missions, IAntiforgery antiforgery) : IEndpointFilter
{
    public const string ParticipantKey = "cb.participantId";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!await antiforgery.IsRequestValidAsync(http)) return Results.BadRequest(new { code = "BadToken", message = "화면을 새로 고쳐 주세요." });
        var slug = http.Request.RouteValues["slug"] as string ?? "";
        var r = await resolver.ResolveAsync(http, slug);
        if (r == TenantResolution.NotFound) return Results.NotFound();
        if (r == TenantResolution.NotMember) return Results.Json(new { code = "NotApprovedMember", message = "아직 승인되지 않았어요" }, statusCode: 403);
        if (!tenant.Current.MissionEnabled) return Results.NotFound();   // R-150
        var pid = await missions.ParticipantIdAsync(http.RequestAborted);
        if (pid is null) return Results.Json(new { code = "NotParticipant", message = "참여자만 미션을 할 수 있어요." }, statusCode: 403);
        http.Items[ParticipantKey] = pid.Value;
        return await next(context);
    }
}
