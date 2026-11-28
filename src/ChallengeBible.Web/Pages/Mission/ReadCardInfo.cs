namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// 읽음 표시 카드(2026-11-28 사용자 시안 「읽음 표시 카드 B」)에 보일 값. 말씀 미션과 성경 읽기 화면이 같은 카드를 쓴다.
/// Total · Read = 오늘 범위(또는 열어 둔 구절) 전체 절 수 · 읽음 표시한 절 수. PageMode 이면 큰 버튼이 이 페이지(Sc:Sv ~ Ec:Ev)만 읽음 처리한다.
/// Mission = 오늘 말씀 미션으로 연 화면이라 「혼자 / 부모님」 버튼이 있다.
/// </summary>
public sealed record ReadCardInfo(int Total, int Read, bool Mission, bool PageMode = false, int PageTotal = 0, int PageRead = 0,
    byte Book = 0, short Sc = 0, short Sv = 0, short Ec = 0, short Ev = 0)
{
    public bool All => Total > 0 && Read >= Total;
    public bool PageAll => PageMode ? PageTotal > 0 && PageRead >= PageTotal : All;
}
