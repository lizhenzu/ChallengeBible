using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/families/{id}?q=` 온가족 상세(확정 시안: 플랫폼-온가족 "온가족 상세", 0065, R-175 · R-179).
/// 구성원(모든 부서 소속 · 누적 점수) · 관계 · 순서 · 빼기 · 더 넣기 · 이름, 보호자는 시상 부서 정하기, 해체(한 번 더 묻기). JS 없이 폼마다.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class FamilyModel(FamilyAdminService families) : PageModel, ITitled
{
    public string Title => "온가족";
    private int Me => User.AccountId()!.Value;

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public FamilyAdminDetail Detail { get; private set; } = null!;
    public List<FamilyHit> Hits { get; private set; } = [];
    public string? Error { get; private set; }

    public string OkMessage => Ok switch
    {
        "created" => "온가족을 만들었어요",
        "saved" => "저장했어요",
        "added" => "구성원을 넣었어요 · 관계를 확인해 주세요",
        "removed" => "구성원을 뺐어요 · 부서 기록 · 점수는 그대로예요",
        "award" => "시상 부서를 정했어요",
        _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    /// <summary>이름 · 관계 저장. 순서는 지금 화면 순서 그대로(관계는 rel_{계정 Id}).</summary>
    public async Task<IActionResult> OnPostSaveAsync(string? name, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        var order = Detail.Members.Select(m => (m.Person.AccountId, Rel(m.Person.AccountId) ?? m.Person.Relation)).ToList();
        await families.SaveAsync(Me, Id, name, order, ct);
        return Done("saved");
    }

    /// <summary>순서 한 칸 옮기기(관계 · 이름은 그대로).</summary>
    public async Task<IActionResult> OnPostMoveAsync(int account, bool up, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        var list = Detail.Members.Select(m => (m.Person.AccountId, m.Person.Relation)).ToList();
        var i = list.FindIndex(x => x.AccountId == account);
        var j = up ? i - 1 : i + 1;
        if (i >= 0 && j >= 0 && j < list.Count) (list[i], list[j]) = (list[j], list[i]);
        await families.SaveAsync(Me, Id, Detail.RawName, list, ct);
        return Done(null);
    }

    public async Task<IActionResult> OnPostRemoveAsync(int account, CancellationToken ct)
    {
        await families.RemoveAsync(Me, Id, account, ct);
        // 마지막 사람을 빼면 온가족도 없어진다
        return await families.DetailAsync(Me, Id, ct) is null ? Redirect("/platform/families?ok=dissolved") : Done("removed");
    }

    public async Task<IActionResult> OnPostAddAsync(int account, CancellationToken ct)
    {
        var hit = (await families.AccountsAsync(Me, [account], ct)).FirstOrDefault();
        var e = hit is null ? OnFamilyError.NotFound : await families.AddAsync(Me, Id, account, FamilyNewModel.Guess(hit), ct);
        if (e == OnFamilyError.None) return Done("added");
        Error = e == OnFamilyError.AlreadyInFamily ? "이미 다른 온가족에 든 사람이에요." : "넣을 수 없어요. 화면을 새로 고쳐 주세요.";
        return await LoadAsync(ct) ? Page() : NotFound();
    }

    /// <summary>보호자의 시상 부서 정하기 · 바꾸기(R-179) — 그 사람이 승인된 부서 중 하나.</summary>
    public async Task<IActionResult> OnPostAwardAsync(int account, int tenant, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        var m = Detail.Members.FirstOrDefault(x => x.Person.AccountId == account);
        if (m is null || !m.IsGuardian || m.AwardOptions.All(o => o.Id != tenant) || !await families.SetAwardTenantAsync(Me, account, tenant, ct))
        {
            Error = "고를 수 있는 부서가 아니에요.";
            return Page();
        }
        return Done("award");
    }

    public async Task<IActionResult> OnPostDissolveAsync(CancellationToken ct) =>
        await families.DissolveAsync(Me, Id, ct) ? Redirect("/platform/families?ok=dissolved") : NotFound();

    private string? Rel(int accountId) =>
        Request.Form[$"rel_{accountId}"].ToString() is { Length: > 0 } r && FamilyRelation.All.Contains(r) ? r : null;

    private IActionResult Done(string? ok) => Redirect($"/platform/families/{Id}" + (ok is null ? "" : $"?ok={ok}"));

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        if (await families.DetailAsync(Me, Id, ct) is not { } d) return false;
        Detail = d;
        if (!string.IsNullOrWhiteSpace(Q)) Hits = await families.SearchAsync(Me, Q, ct);
        return true;
    }
}
