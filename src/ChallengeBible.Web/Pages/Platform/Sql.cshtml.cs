using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/sql` SQL 실행(ADR 0107, R-320): 플랫폼 관리자만. 기본은 시험 실행(되돌림), 저장은 확인 글자 SAVE. 관리자가 바뀌면 되돌림.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class SqlModel(SqlConsoleService sql) : PageModel, ITitled
{
    public string Title => "SQL 실행";
    public string Sql { get; private set; } = SqlConsoleService.Presets[0].Sql;
    public bool Save { get; private set; }
    public SqlRunResult? Result { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<(DateTime AtUtc, string Actor, string Detail)> History { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => History = await sql.HistoryAsync(User.UserName()!, ct);

    public async Task<IActionResult> OnPostAsync(string? query, string? mode, string? confirm, CancellationToken ct)
    {
        Sql = query ?? "";
        Save = mode == "save";
        if (Save && !string.Equals(confirm?.Trim(), SqlConsoleService.ConfirmWord, StringComparison.Ordinal))
            Error = $"저장하려면 확인 칸에 {SqlConsoleService.ConfirmWord} 를 써 주세요. 실행하지 않았어요.";
        else
            Result = await sql.RunAsync(User.UserName()!, Sql, Save, ct);
        History = await sql.HistoryAsync(User.UserName()!, ct);
        return Page();
    }
}
