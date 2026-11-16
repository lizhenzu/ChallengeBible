using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/prayer365` 365 기도문 관리(ADR 0106, 확정 시안 365기도 ⑤): 366칸 달력판 · 샘플 채우기 · 진짜 기도문 넣기(JSON).</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class Prayer365Model(Prayer365Service prayers, TimeProvider time) : PageModel, ITitled
{
    public string Title => "365 기도문";
    public IReadOnlyDictionary<string, ChallengeBible.Infrastructure.Data.Prayer365> Map { get; private set; } = new Dictionary<string, ChallengeBible.Infrastructure.Data.Prayer365>();
    [TempData] public string? Msg { get; set; }
    /// <summary>달력판에서 오늘(플랫폼 시간대) 칸 표시.</summary>
    public string TodayMd => Prayer365Service.Md(ChallengeBible.Domain.Time.TenantClock.ToTenantDate(time.GetUtcNow(), PlatformHomeService.PlatformTimeZone));
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => Map = await prayers.AllAsync(User.UserName()!, ct);

    public async Task<IActionResult> OnPostSamplesAsync(CancellationToken ct)
    {
        var n = await prayers.FillSamplesAsync(User.UserName()!, ct);
        Msg = n == 0 ? "빈 날이 없어요." : $"빈 날 {n}일을 샘플로 채웠어요.";
        return Redirect("/platform/prayer365");
    }

    /// <summary>유년부 `FOR JSON PATH` 결과 파일(또는 붙여 넣은 글)을 받아 같은 날짜를 바꿔 넣는다.</summary>
    public async Task<IActionResult> OnPostImportAsync(IFormFile? file, string? json, CancellationToken ct)
    {
        var text = json;
        if (file is { Length: > 0 }) { using var r = new StreamReader(file.OpenReadStream()); text = await r.ReadToEndAsync(ct); }
        text = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(text)) { Error = "JSON 파일을 고르거나 붙여 넣어 주세요."; Map = await prayers.AllAsync(User.UserName()!, ct); return Page(); }
        try
        {
            var (saved, skipped) = await prayers.ImportAsync(User.UserName()!, text, ct);
            Msg = $"기도문 {saved}편을 넣었어요." + (skipped > 0 ? $" 날짜 · 제목 · 본문이 빠진 {skipped}줄은 건너뛰었어요." : "");
            return Redirect("/platform/prayer365");
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or FormatException)
        {
            Error = "JSON 을 읽지 못했어요. SSMS 에서 \"파일로 결과 표시\"로 저장한 파일인지 확인해 주세요.";
            Map = await prayers.AllAsync(User.UserName()!, ct);
            return Page();
        }
    }

    /// <summary>SSMS 결과 파일에는 앞에 열 이름 · 줄 표시가 붙을 수 있다 — 첫 '[' 부터 마지막 ']' 까지만.</summary>
    public static string? ExtractJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        int a = text.IndexOf('['), b = text.LastIndexOf(']');
        return a >= 0 && b > a ? string.Concat(text[a..(b + 1)].Split('\n').Select(l => l.TrimEnd('\r'))) : text;
    }
}
