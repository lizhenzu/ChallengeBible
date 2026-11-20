using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Album;

/// <summary>사진첩 화면 공용: 보는 사람(로그인 아이디 · 플랫폼 관리자).</summary>
public abstract class AlbumPageBase : PageModel
{
    protected AlbumViewer Viewer => new(User.UserId() is null ? null : User.UserName(), User.IsPlatformAdmin());
}
