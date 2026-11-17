using Microsoft.AspNetCore.Mvc;
﻿using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform` 플랫폼 홈(확정 시안: 플랫폼-홈): 숫자 4칸 → 챙길 일 → 부서별 오늘 | 최근 관리 기록.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class IndexModel(PlatformHomeService home, PlatformTodoService todo, MessageService msgs) : PageModel, ITitled
{
    /// <summary>로그인 후 한 번 새 쪽지 띠 · 팝업(ADR 0112) — 개선 · 불편 쪽지는 superadmin 이 받는다.</summary>
    public int NewMessages { get; private set; }
    public IReadOnlyList<MsgRow> NewList { get; private set; } = [];
    public string Title => "플랫폼 홈";
    public PlatformHome Data { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Data = await home.GetAsync(User.UserName()!, ct);
        // 오늘 할 일(ADR 0100, R-309): 로그인 직후 내 할 일이 있고 안 보기 기간이 아니면 할 일 화면으로
        // 리디렉트 응답은 TempData 를 저절로 남기므로 직접 지운다 — 로그인 직후 한 번만
        if (TempData.Remove(ChallengeBible.Web.Pages.Account.LoginModel.TodoCheckKey) && Data.Todos.Count > 0 && !await todo.SnoozedAsync(User.UserName()!, ct))
            return Redirect("/platform/todo");
        if (TempData.Remove(ChallengeBible.Web.Pages.TenantHomeModel.MsgBandKey))
        {
            NewMessages = await msgs.UnreadAsync(User.UserName()!, ct);
            if (NewMessages > 0) NewList = (await msgs.InboxAsync(User.UserName()!, ct)).Where(r => r.Unread).Take(3).ToList();
        }
        return Page();
    }

    /// <summary>기록 시각: 오늘이면 시:분, 어제면 "어제", 그 전은 월/일(한국 시간).</summary>
    public string When(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(PlatformHomeService.PlatformTimeZone));
        var days = DateOnly.FromDateTime(Data.NowLocal).DayNumber - DateOnly.FromDateTime(local).DayNumber;
        return days switch { 0 => local.ToString("HH:mm"), 1 => "어제", _ => local.ToString("M'/'d") };
    }
}
