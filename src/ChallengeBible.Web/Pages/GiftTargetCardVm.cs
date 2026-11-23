using ChallengeBible.Web.Services;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// 홈 "선물 대상자와 상태" 카드 값(2026-11-23 사용자 확정 C안 · ADR 0134).
/// 부서 홈(`/{slug}`)과 첫 화면(`/`)이 같은 카드를 그린다. 손님이면 개인 이름 대신 안내.
/// </summary>
public sealed record GiftTargetCardVm(GiftTargetBoard? Board, string Slug, bool Guest, string? LoginTo)
{
    /// <summary>전체 명단 화면(`/{slug}/gift-board`) — 볼 것이 있을 때만.</summary>
    public string? MoreHref => Guest || Board is null || Board.Total == 0 ? null : $"/{Slug}/gift-board";

    public bool Empty => !Guest && (Board is null || Board.Total == 0);
}
