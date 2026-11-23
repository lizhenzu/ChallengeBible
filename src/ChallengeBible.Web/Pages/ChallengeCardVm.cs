using ChallengeBible.Web.Services;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// "나의 도전 현황" 통합 카드 부품 값(부서 홈 · 첫 화면 공용, 2026-11-23 사용자 확정).
/// 카드 자체는 <see cref="ChallengeCardService"/> 가 만들고, 이 껍데기는 화면에 필요한 링크만 더한다.
/// </summary>
public sealed record ChallengeCardVm(ChallengeCard? Card, string Slug, bool Guest, string? LoginTo)
{
    /// <summary>카드 머리의 "더보기" — 손님에게는 없다.</summary>
    public string? MoreHref => Guest || Card is null ? null : $"/{Slug}/points";

    /// <summary>진행 줄을 누르면 그 프로그램 화면으로.</summary>
    public string Href(string key) => $"/{Slug}/" + (key switch
    {
        "reading" => "reading",
        "copy" => "copy",
        _ => "prayer365",
    });
}
