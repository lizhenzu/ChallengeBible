using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Pages.Reading;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/reading` 통독 현황(확정 시안: 관리-통독현황, 0040 규칙 7). 영역 관리자만. 점수 없음(R-157).</summary>
[TenantRoles(Roles.TenantAdmin)]
public class ReadingModel(ReadingBoardService board, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "통독 현황";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    /// <summary>"" 모두 · on 읽는 중(7일 안) · old 7일 넘게 안 읽음 · none 아직 시작 안 함</summary>
    [BindProperty(SupportsGet = true)] public string? St { get; set; }
    /// <summary>pct 진도 높은 순 · recent 최근 읽은 순 · name 이름 순</summary>
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "pct";
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public ReadingBoard Board { get; private set; } = null!;
    public List<BoardMember> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.ReadingEnabled) return NotFound();
        Board = await board.GetAsync(ct);
        var f = Board.Rows.Where(r => St switch
        {
            "on" => r.DaysAgo is < 7,
            "old" => r.DaysAgo is >= 7,
            "none" => r.DaysAgo is null,
            _ => true,
        });
        Rows = Sort == "name"
            ? f.Where(r => string.IsNullOrWhiteSpace(Q) || r.Name.Contains(Q.Trim())).OrderBy(r => r.Name, StringComparer.Create(ReadingPageBase.Ko, false)).ToList()
            : ReadingBoardService.Sort(f, Sort == "recent" ? "recent" : "progress", Q).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostPublicAsync(bool on, CancellationToken ct)
    {
        await board.SetPublicAsync(on, ct);
        return RedirectToPage(new { slug = tenant.Current.Slug, Ok = on ? "on" : "off" });
    }

    public string LastText(BoardMember m) => m.LastBook is byte b ? $"{ReadingPageBase.BookName(b)} {m.LastChapter}{(b == 19 ? "편" : "장")}" : "";
}
