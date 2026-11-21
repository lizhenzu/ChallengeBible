using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.People;

/// <summary>
/// `/{slug}/people` 인명 검색(R-358, 2026-11-21 사용자 · 확정 시안 인명검색): 유년부 /AddressBook 을 그대로 —
/// 이름 · 아이디 실시간 검색 · 학생 / 선생님·기타 거르기 · 전화 · 문자 · 쪽지 · 번호 복사 · 최근 검색. 교사 · 관리자만(개인정보).
/// </summary>
public class IndexModel(PeopleSearchService search, MessageService messages, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "인명 검색";
    public TenantContext Tenant => tenant;
    public string Slug => tenant.Current.Slug;
    /// <summary>쪽지 창 글자 수(유년부와 같다).</summary>
    public const int NoteMax = 500;

    /// <summary>교사 · 관리자만(생일자 보기의 상세와 같은 기준): 부서 관리자 · 교사 꼬리표 · 역할이 교사 · 임원.</summary>
    public static bool CanUse(TenantContext t) => !t.IsLobby && t.IsApproved && (t.IsStaff || t.IsAdmin || UserRole.KindOf(t.RoleCode) is UserRoleKind.Teacher or UserRoleKind.Officer);

    public IActionResult OnGet() => CanUse(tenant) ? Page() : NotFound();

    /// <summary>`?handler=Search&amp;q=&amp;take=` — 유년부 /AddressBook/Search 와 같은 JSON { ok, items }.</summary>
    public async Task<IActionResult> OnGetSearchAsync(string? q, int take = PeopleSearchService.DefaultTake, CancellationToken ct = default)
    {
        if (!CanUse(tenant)) return NotFound();
        q = (q ?? "").Trim();
        if (q.Length < PeopleSearchService.MinLength) return new JsonResult(new { ok = true, items = Array.Empty<object>() });
        return new JsonResult(new { ok = true, items = await search.SearchAsync(q, take, Slug, ct) });
    }

    /// <summary>쪽지 보내기(한 사람, 유년부 /PopMessage/SendAjax 와 같은 { ok, message }). 이 부서 명부의 계정에게만.</summary>
    public async Task<IActionResult> OnPostSendAsync([FromForm] string? toUserName, [FromForm] string? messageText, CancellationToken ct)
    {
        if (!CanUse(tenant)) return NotFound();
        var to = (toUserName ?? "").Trim();
        var text = (messageText ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return new JsonResult(new { ok = false, message = "내용을 입력해 주세요." });
        if (text.Length > NoteMax) return new JsonResult(new { ok = false, message = $"{NoteMax}자까지 쓸 수 있어요." });
        if (to.Length == 0 || !await search.InDeptAsync(to, ct)) return new JsonResult(new { ok = false, message = "받는 사람을 찾을 수 없어요." });
        var e = await messages.SendAsync(User.UserName()!, to, text, null, ct);
        return new JsonResult(new { ok = e == MsgError.None, message = e == MsgError.None ? "보냈어요" : MessageService.Text(e) });
    }
}
