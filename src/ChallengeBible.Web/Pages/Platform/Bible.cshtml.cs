using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/bible` 성경 본문 적재 도구(0036 · 확정 시안: 플랫폼-성경데이터의 번역 카드와 검사 결과).</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class BibleModel(BibleImportService bible) : PageModel, ITitled
{
    public string Title => "성경 데이터";
    public IReadOnlyList<BibleSourceStatus> Sources { get; private set; } = [];
    public BibleCheck? Check { get; private set; }
    public string? Message { get; private set; }
    public bool MessageOk { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task OnPostImportAsync(string code, CancellationToken ct)
    {
        var r = await bible.ImportAsync(code, ct);
        Message = r.Message; MessageOk = r.Ok;
        await LoadAsync(ct);
    }

    public async Task OnPostCheckAsync(CancellationToken ct) => await LoadAsync(ct, check: true);

    public string BookName(int id) => BibleBooks.All().FirstOrDefault(b => b.Id == id)?.Name ?? id.ToString();

    private async Task LoadAsync(CancellationToken ct, bool check = false)
    {
        Sources = await bible.StatusAsync(ct);
        if (check || Sources.Any(s => s.LoadedVerses > 0)) Check = await bible.CheckAsync(ct);
    }
}
