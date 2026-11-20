using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>
/// 가입 뒤 명부 연결(R-341, 확정 시안 가입연결-탈퇴 ② ③): 같은 이름 후보를 가린 힌트로 보이고, 고르면 명부에 있는 정보로 본인 확인.
/// 맞으면 그 명부 줄에 이어 그 부서 · 명부의 역할로 바로 들어가고, 그 부서 관리자와 플랫폼 관리자에게 쪽지.
/// </summary>
public class LinkModel(RosterLinkService roster, MessageService msgs) : PageModel, ITitled
{
    public string Title => Id is null ? "명부에 같은 이름" : "본인 확인";

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public string? Last4 { get; set; }
    [BindProperty] public int BMonth { get; set; }
    [BindProperty] public int BDay { get; set; }

    public IReadOnlyList<RosterCandidate> Candidates { get; private set; } = [];
    public string Dept { get; private set; } = "";
    public bool AskPhone { get; private set; }
    public bool AskBirth { get; private set; }
    public string? Error { get; private set; }
    public bool Locked { get; private set; }
    public string? DoneSlug { get; private set; }

    private string Me => User.UserName()!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is int id) return await Load(id, ct) ? Page() : Redirect("/account/link");
        Candidates = await roster.CandidatesAsync(Me, ct);
        if (Candidates.Count == 0) return Redirect("/");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Id is not int id || !await Load(id, ct)) return Redirect("/account/link");
        var (err, slug, dept) = await roster.VerifyAsync(Me, id, Last4, BMonth, BDay, ct);
        if (err == LinkError.None)
        {
            DoneSlug = slug;
            await msgs.NotifyDeptAsync(Me, (await roster.TenantOfSlugAsync(slug!, ct)), "부서 참여",
                $"{User.DisplayName()}님이 {dept}에 들어왔어요 · 명부 본인 확인(전화 · 생일)으로 그 줄과 이어졌어요.", ct);
            return Page();
        }
        Locked = err == LinkError.Locked || roster.IsLocked(Me);
        Error = RosterLinkService.Message(err, roster.TriesLeft(Me));
        return Page();
    }

    private async Task<bool> Load(int id, CancellationToken ct)
    {
        var (p, dept) = await roster.FindAsync(Me, id, ct);
        if (p is null) return false;
        Dept = dept;
        AskPhone = RosterLinkService.PhoneHint(p) is not null;
        AskBirth = RosterLinkService.BirthHint(p) is not null;
        Locked = roster.IsLocked(Me);
        return AskPhone || AskBirth;
    }
}
