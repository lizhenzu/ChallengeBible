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
    /// <summary>같은 팀의 다른 선물(여러 단계를 넘었을 때): 아직 안 고른 것 · 고른 것.</summary>
    public IReadOnlyList<TeamGiftView> Others { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        await gifts.EnsureAsync(ct);
        var mine = await gifts.MineAsync(ct);
        Gift = mine.FirstOrDefault(g => g.Id == Id);
        if (Gift is null) return NotFound();
        Others = mine.Where(g => g.Id != Id && g.State is "Open" or "Picked" or "Purchased").OrderBy(g => g.Stage).ToList();
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
        if (e == TeamPickError.None)   // 여러 단계를 넘었으면 다음 안 고른 선물로 바로
        {
            var next = (await gifts.MineAsync(ct)).Where(g => g.State == "Open").OrderBy(g => g.Stage).FirstOrDefault();
            if (next is not null) { Msg = "next"; return Redirect($"/{Slug}/gifts/team/{next.Id}"); }
        }
        return Redirect($"/{Slug}/gifts/team/{Id}");
    }

    public string? MsgText => Msg switch
    {
        "picked" => "선물을 골랐어요! 선생님이 준비할 거예요.",
        "next" => "골랐어요! 다음 선물도 골라 주세요.",
        "closed" => "지금은 고르거나 바꿀 수 없어요. 선생님이 이미 준비했거나 기한이 지났어요.",
        "gone" => "그 선물은 지금 고를 수 없어요.",
        _ => null,
    };
}
