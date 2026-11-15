using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/quiz-ai-prompt` 퀴즈 출제 AI 프롬프트 고치기(유년부 QuizAiPrompt 그대로, 확정 시안 퀴즈출제-말씀입력). 플랫폼 관리자만.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class QuizAiPromptModel(QuizAiPromptService prompts) : PageModel, ITitled
{
    public string Title => "AI 프롬프트";
    [BindProperty] public string? Text { get; set; }
    public QuizAiPromptInfo? Info { get; private set; }
    public IReadOnlyList<string> Missing { get; private set; } = [];
    [TempData] public string? Ok { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Info = await prompts.GetAsync(User.UserName()!, ct);
        Text = Info.Text;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Missing = await prompts.SaveAsync(User.UserName()!, Text, ct);
        if (Missing.Count > 0) { Info = await prompts.GetAsync(User.UserName()!, ct); return Page(); }
        Ok = string.IsNullOrWhiteSpace(Text) ? "reset" : "saved";
        return Redirect("/platform/quiz-ai-prompt");
    }

    public async Task<IActionResult> OnPostResetAsync(CancellationToken ct)
    {
        await prompts.SaveAsync(User.UserName()!, "", ct);
        Ok = "reset";
        return Redirect("/platform/quiz-ai-prompt");
    }
}
