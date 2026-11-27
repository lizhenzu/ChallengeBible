using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/{slug}/feedback` 개선 · 불편 접수(2026-11-17 사용자): 부서 홈 맨 아래 상자를 누르면 오는 화면.
/// 보내기는 쪽지함의 Feedback 처리(superadmin 에게, 익명이면 부서만, R-326)를 그대로 쓰고 이 화면으로 돌아온다.
/// </summary>
[Authorize]
public class FeedbackModel(TenantContext tenant) : PageModel, ITitled
{
    public string Title => "개선 · 불편 접수";
    public TenantContext Tenant => tenant;
    public void OnGet() { }
}
