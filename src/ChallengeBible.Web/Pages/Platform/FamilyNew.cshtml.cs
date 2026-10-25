using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/families/new?request=&amp;pick=&amp;q=` 새로 묶기(확정 시안: 플랫폼-온가족 "신청에서 묶기", R-175 · R-176).
/// JS 없이: 찾기 → [넣기] 는 주소의 pick 에 더하고, [온가족 만들기] 때 관계와 이름을 함께 보낸다. 신청에서 왔으면 그 신청을 승인으로 닫는다.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class FamilyNewModel(FamilyAdminService families) : PageModel, ITitled
{
    public string Title => "온가족 묶기";
    private int Me => User.AccountId()!.Value;

    [BindProperty(SupportsGet = true, Name = "request")] public int? RequestId { get; set; }
    [BindProperty(SupportsGet = true)] public List<int> Pick { get; set; } = [];
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public FamilyRequestRow? From { get; private set; }
    public List<FamilyHit> Hits { get; private set; } = [];
    public List<FamilyHit> Picked { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(string? name, CancellationToken ct)
    {
        var members = Pick.Distinct().Select(id => (id, Request.Form[$"rel_{id}"].ToString())).ToList();
        var (e, id) = await families.CreateAsync(Me, name, RequestId, members, ct);
        if (e == OnFamilyError.None) return Redirect($"/platform/families/{id}?ok=created");
        Error = e == OnFamilyError.AlreadyInFamily ? "이미 다른 온가족에 든 사람이 있어요." : "넣을 사람을 골라 주세요.";
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        From = RequestId is int r ? await families.RequestAsync(Me, r, ct) : null;
        Pick = Pick.Distinct().ToList();
        Picked = await families.AccountsAsync(Me, Pick, ct);
        Hits = await families.SearchAsync(Me, Q, ct);
    }

    /// <summary>지금 상태에 pick 을 바꾼 주소(넣기 · 빼기 링크).</summary>
    public string Url(IEnumerable<int> pick, string? q = null) =>
        "/platform/families/new?" + string.Join("&", new[] { RequestId is int r ? $"request={r}" : null, q is { Length: > 0 } ? $"q={Uri.EscapeDataString(q)}" : null }
            .Concat(pick.Select(p => $"pick={p}")).OfType<string>());

    /// <summary>처음 관계 짐작: 보호자면 기타(관리자가 엄마 · 아빠로 고름), 아니면 학생.</summary>
    public static string Guess(FamilyHit h) => h.Depts.Contains("보호자") ? FamilyRelation.Other : FamilyRelation.Student;
}
