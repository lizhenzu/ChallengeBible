using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/gifts/team/{id}` 가족팀 · 친구팀 선물 고르기(확정 시안 가족팀-선물고르기 ② ~ ④, R-291). 내 팀 선물만.
/// 금액 · 가격은 보이지 않는다(사용자 질문 1 → ③). 고른 뒤에는 누가 골랐는지 · [다른 선물로 바꾸기](구매 전까지 구성원 누구나).
/// </summary>
public class TeamGiftModel(TeamGiftService gifts, FamilyTeamService teams, TenantContext tenant) : PageModel, ITitled
{
    public string Title => Gift?.Friend == true ? "친구팀 선물 고르기" : "가족 선물 고르기";
    public string Slug => tenant.Current.Slug;
    [BindProperty(SupportsGet = true)] public long Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool Change { get; set; }
    [TempData] public string? Msg { get; set; }
    public TeamGiftView? Gift { get; private set; }
    public IReadOnlyList<TeamPickItem> Items { get; private set; } = [];
    public IReadOnlyList<string> Members { get; private set; } = [];
    public DateOnly Today { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        await gifts.EnsureAsync(ct);
        Gift = await gifts.GetAsync(Id, ct);
        if (Gift is null) return NotFound();
        if (Gift.State == "Open" || (Change && Gift.State == "Picked")) Items = await gifts.ItemsAsync(Gift.MaxAmount, ct);
        Members = Gift.TeamId is int tid ? await teams.MemberNamesAsync(tid, ct) : [];
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int item, CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        var e = await gifts.PickAsync(Id, item, ct);
        if (e == TeamPickError.NotMember || e == TeamPickError.NotFound) return NotFound();
        Msg = e switch { TeamPickError.None => "picked", TeamPickError.Closed => "closed", _ => "gone" };
        return Redirect($"/{Slug}/gifts/team/{Id}");
    }

    public string? MsgText => Msg switch
    {
        "picked" => "선물을 골랐어요! 선생님이 준비할 거예요.",
        "closed" => "지금은 고르거나 바꿀 수 없어요. 선생님이 이미 준비했거나 기한이 지났어요.",
        "gone" => "그 선물은 지금 고를 수 없어요.",
        _ => null,
    };
}
