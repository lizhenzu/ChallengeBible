using ChallengeBible.Web.Auth;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/todo` 할 일 · 누가 보나(ADR 0100, 확정 시안 오늘할일 ④): "공통 규칙 따름 / 우리 부서만 따로" + 할 일 × 묶음 체크칸. 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class TodoRulesModel(TodoService todo, TodoRuleService rules, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "할 일 · 누가 보나";
    public TenantContext Tenant => tenant;
    public bool Custom { get; private set; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Rules { get; private set; } = new Dictionary<string, IReadOnlyList<string>>();
    [TempData] public string? Msg { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => (Custom, Rules) = await todo.RulesAsync(ct);

    public async Task<IActionResult> OnPostAsync(string mode, CancellationToken ct)
    {
        var map = TodoKinds.All.ToDictionary(k => k, k => (IReadOnlyList<string>)Request.Form[$"r_{k}"].OfType<string>().ToList());
        await rules.SaveDeptAsync(User.UserName()!, mode == "custom", map, ct);
        Msg = mode == "custom" ? "우리 부서 규칙을 저장했어요." : "공통 규칙을 따르게 했어요.";
        return Redirect($"/{tenant.Current.Slug}/admin/todo");
    }
}
