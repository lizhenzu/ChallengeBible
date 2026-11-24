using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Web.Pages.Welcome;

/// <summary>
/// `/{slug}/welcome2` — 웰컴 화면과 같은 화면에서 「오늘의 안내」 6칸만 색 카드 모양(2026-11-24 사용자 요청).
/// 위쪽 「표로 보기 / 카드로 보기」 버튼으로 두 화면을 오간다. 기능 · 데이터 · 보내기 규칙은 모두 IndexModel 과 같다.
/// </summary>
public class Welcome2Model(IntroService intro, WorshipService worship, BulletinService bulletins, TenantContext tenant, TimeProvider time)
    : IndexModel(intro, worship, bulletins, tenant, time);
