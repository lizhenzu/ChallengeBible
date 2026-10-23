using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/content/{kind}?month=&amp;date=` 공용 콘텐츠(확정 시안: 플랫폼-공용콘텐츠).</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class ContentModel(ContentAdminService content, TimeProvider time) : PageModel, ITitled
{
    public string Title => "공용 콘텐츠 · 플랫폼";

    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "word";
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    [BindProperty] public ContentInput Input { get; set; } = new();

    public ContentKind K { get; private set; }
    public DateOnly First { get; private set; }
    public List<ContentDay> Days { get; private set; } = [];
    public string Status { get; private set; } = "None";
    public FieldErrors Errors { get; private set; } = new();
    public string? Notice { get; private set; }

    public static readonly (string Key, string Label, ContentKind K)[] Kinds =
        [("word", "말씀", ContentKind.Word), ("quiz", "퀴즈", ContentKind.Quiz), ("prayer", "기도", ContentKind.Prayer), ("recite", "암송", ContentKind.Recite)];

    public static string StatusLabel(string s) => s switch
    {
        ContentStatus.Published => "공개", ContentStatus.Draft => "초안", _ => "없음",
    };

    public static string StatusClass(string s) => s switch
    {
        ContentStatus.Published => "s-pub", ContentStatus.Draft => "s-draft", _ => "s-none",
    };

    private int Me => User.AccountId()!.Value;

    public string OkMessage => Ok switch
    {
        "draft" => "초안으로 저장했어요 · 아이들에게는 아직 안 보여요",
        "published" => "공개했어요 · 덮어쓰지 않은 모든 부서에 보여요",
        _ when Ok?.StartsWith("bulk") == true => $"{Ok[4..]}개를 공개했어요",
        _ => "",
    };

    public string Link(DateOnly? date = null, string? kind = null, DateOnly? month = null) =>
        $"/platform/content/{kind ?? Kind}?month={(month ?? First):yyyy-MM}" + (date is { } d ? $"&date={d:yyyy-MM-dd}" : "");

    private bool Init()
    {
        var k = Kinds.FirstOrDefault(x => x.Key == Kind);
        if (k.Key is null) return false;
        K = k.K;
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        First = DateOnly.TryParseExact((Month ?? "") + "-01", "yyyy-MM-dd", out var f) ? f : new DateOnly(today.Year, today.Month, 1);
        return true;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!Init()) return NotFound();
        await LoadAsync(ct);
        if (Date is { } d) (Input, Status, _) = await content.LoadAsync(Me, K, d, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(bool publish, CancellationToken ct)
    {
        if (!Init()) return NotFound();
        Input.Date = Date ?? Input.Date;
        Errors = await content.SaveAsync(Me, K, Input, publish, ct);
        if (Errors.Ok) return Redirect(Link(Input.Date) + $"&ok={(publish ? "published" : "draft")}");
        await LoadAsync(ct);
        Status = (await content.LoadAsync(Me, K, Input.Date, ct)).Status;
        return Page();
    }

    public async Task<IActionResult> OnPostBulkAsync(List<DateOnly> dates, CancellationToken ct)
    {
        if (!Init()) return NotFound();
        var (ok, skipped) = await content.PublishManyAsync(Me, K, dates, ct);
        if (skipped > 0)
        {
            Notice = $"{ok}개를 공개했어요. 칸이 덜 채워진 초안 {skipped}개는 건너뛰었어요.";
            await LoadAsync(ct);
            return Page();
        }
        return Redirect(Link(Date) + $"&ok=bulk{ok}");
    }

    private async Task LoadAsync(CancellationToken ct) => Days = await content.MonthAsync(Me, K, First.Year, First.Month, ct);
}
