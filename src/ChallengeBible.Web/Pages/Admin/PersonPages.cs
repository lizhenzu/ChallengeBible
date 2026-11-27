using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>사람 관리 한 사람 화면들이 같이 쓰는 것(2026-11-21 확정 시안 사람관리-쪼개기).</summary>
public static class PersonUi
{
    public static string Ok(string? ok) => ok switch
    {
        "psave" => "저장했어요",
        "padd" => "명부에 올렸어요",
        "plink" => "계정과 연결했어요 · 출석 · 생일 · 반 · 선물이 이 계정과 이어져요",
        "punlink" => "연결을 풀었어요 · 이 계정은 새 명부 줄을 받았어요",
        "link" => "보호자를 연결했어요 · 이제 자녀 기록을 볼 수 있어요",
        "unlink" => "보호자 연결을 풀었어요",
        _ => "",
    };

    /// <summary>생일 고르기: 월 · 일(연도 없음). 비우면 "생일 없음".</summary>
    public static Microsoft.AspNetCore.Html.IHtmlContent Days(byte? m, byte? d)
    {
        var sb = new System.Text.StringBuilder("<span class=\"two\"><select class=\"sel\" name=\"month\" aria-label=\"월\"><option value=\"\">월</option>");
        for (var i = 1; i <= 12; i++) sb.Append($"<option value=\"{i}\"{(m == i ? " selected" : "")}>{i}월</option>");
        sb.Append("</select><select class=\"sel\" name=\"day\" aria-label=\"일\"><option value=\"\">일</option>");
        for (var i = 1; i <= 31; i++) sb.Append($"<option value=\"{i}\"{(d == i ? " selected" : "")}>{i}일</option>");
        sb.Append("</select></span>");
        return new Microsoft.AspNetCore.Html.HtmlString(sb.ToString());
    }

    /// <summary>역할 고르기 목록: 부서 역할 코드(없으면 기본 19개) — 어드민은 빼고(R-315, 부서 관리자 계정 전용).</summary>
    public static List<(string Code, string Name)> Roles(List<(string Code, string Name)> fromDept) =>
        (fromDept.Count > 0 ? fromDept : [.. UserRole.Codes]).Select(c => (c.Code.Trim(), c.Name)).Where(c => c.Item1 != UserRole.AdminCode).ToList();

    /// <summary>동그라미 글자: 성을 뺀 이름 두 글자까지.</summary>
    public static string Short(string n) { var b = n.EndsWith(" 선생님") ? n[..^4] : n; b = b.Length > 1 ? b[1..] : b; return b.Length > 2 ? b[..2] : b; }
}

/// <summary>`/{slug}/admin/members/p/{pid}` 사람 상세(읽기). 부서 관리자 · 교사(고치기만).</summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class PersonDetailModel(PeopleService people, MemberAdminService members, TenantContext tenant) : PageModel, ITitled
{
    public string Title => Row?.Name ?? "사람";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public int Pid { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    public RosterRow? Row { get; private set; }
    public string? Error { get; private set; }
    public bool IsAdmin => tenant.IsAdmin;
    public bool CanStaff => tenant.IsAdmin || (tenant.IsApproved && UserRole.KindOf(tenant.RoleCode) == UserRoleKind.Teacher);
    public List<(string UserName, string Name)> Guardians { get; private set; } = [];
    public List<string> MyGuardians { get; private set; } = [];
    public string Me => User.UserName()!;

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Row = (await people.RosterAsync(null, null, ct)).FirstOrDefault(r => r.PersonId == Pid);
        if (Row is null) return false;
        if (IsAdmin && Row is { Linked: true, RoleCode: UserRole.StudentCode } r)
        {
            Guardians = await members.GuardiansAsync(ct);
            MyGuardians = (await members.LinksAsync(ct)).GetValueOrDefault(r.UserName!) ?? [];
        }
        return true;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!CanStaff) return Forbid();
        return await LoadAsync(ct) ? Page() : NotFound();
    }

    private IActionResult Back(string ok) => RedirectToPage(new { slug = tenant.Current.Slug, pid = Pid, Ok = ok });

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
        var e = await people.DeleteAsync(Pid, ct);
        if (e == PeopleError.None) return RedirectToPage("Members", new { slug = tenant.Current.Slug, Ok = "pdel" });
        Error = PeopleService.Message(e);
        return await LoadAsync(ct) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostUnlinkAsync(CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
        var e = await people.UnlinkAsync(Me, Pid, ct);
        if (e == PeopleError.None) return Back("punlink");
        Error = PeopleService.Message(e);
        return await LoadAsync(ct) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostGuardianAsync(string guardian, string student, bool unlink, CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
        var e = unlink ? await members.UnlinkGuardianAsync(guardian, student, ct) : await members.LinkGuardianAsync(Me, guardian, student, ct);
        if (e == MemberError.None) return Back(unlink ? "unlink" : "link");
        Error = MemberAdminService.Message(e);
        return await LoadAsync(ct) ? Page() : NotFound();
    }
}

/// <summary>`/{slug}/admin/members/p/{pid}/edit` 고치기: 이름(계정 없을 때) · 생일 · 연락처 · 역할. 저장하면 상세로.</summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class PersonEditModel(PeopleService people, MemberAdminService members, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "고치기";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public int Pid { get; set; }
    public RosterRow? Row { get; private set; }
    public List<(string Code, string Name)> RoleCodes { get; private set; } = [];
    public string? Error { get; private set; }
    public bool IsAdmin => tenant.IsAdmin;
    public bool CanStaff => tenant.IsAdmin || (tenant.IsApproved && UserRole.KindOf(tenant.RoleCode) == UserRoleKind.Teacher);

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Row = (await people.RosterAsync(null, null, ct)).FirstOrDefault(r => r.PersonId == Pid);
        RoleCodes = PersonUi.Roles(await members.RoleCodesAsync(ct));
        return Row is not null;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!CanStaff) return Forbid();
        return await LoadAsync(ct) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(string? name, string? roleCode, int? month, int? day, string? phone1, string? phone2, CancellationToken ct)
    {
        if (!CanStaff) return Forbid();
        if (!await LoadAsync(ct)) return NotFound();
        if (Row!.RoleCode?.Trim() == UserRole.AdminCode || roleCode?.Trim() == UserRole.AdminCode) roleCode = Row.RoleCode;   // 어드민(부서 관리자 계정)은 바꾸지 않는다(R-315)
        var e = await people.UpdateAsync(Pid, new PersonInput(name, roleCode, month, day, phone1, phone2), ct);
        if (e == PeopleError.None && IsAdmin && Row!.Linked && Row.DeptRoleId is int did && (roleCode ?? "") != (Row.RoleCode ?? ""))
        {
            var me = await members.ChangeRoleAsync(did, roleCode, ct);   // 가입한 사람의 역할은 소속에서(관리자 규칙 다시 맞춤)
            if (me != MemberError.None) { Error = MemberAdminService.Message(me); return Page(); }
        }
        if (e == PeopleError.None) return RedirectToPage("PersonDetail", new { slug = tenant.Current.Slug, pid = Pid, Ok = "psave" });
        Error = PeopleService.Message(e);
        return Page();
    }
}

/// <summary>`/{slug}/admin/members/p/{pid}/link` 계정 연결(R-324): 같은 이름 계정 · 이름으로 찾기. 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class PersonLinkModel(PeopleService people, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "계정 연결";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public int Pid { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public RosterRow? Row { get; private set; }
    public List<AccountCandidate> Found { get; private set; } = [];
    public bool Searched { get; private set; }
    public string? Error { get; private set; }
    public string Me => User.UserName()!;

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Row = (await people.RosterAsync(null, null, ct)).FirstOrDefault(r => r.PersonId == Pid && !r.Linked);
        if (Row is null) return false;
        Searched = !string.IsNullOrWhiteSpace(Q);
        var all = await people.LinkableAccountsAsync(Searched ? Q : null, ct);
        Found = Searched ? all : all.Where(a => PeopleService.Key(a.Name) == PeopleService.Key(Row.Name)).ToList();   // 처음엔 같은 이름 계정만
        return true;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(string? uid, CancellationToken ct)
    {
        var e = string.IsNullOrEmpty(uid) ? PeopleError.NotFound : await people.LinkAccountAsync(Me, Pid, uid, ct);
        if (e == PeopleError.None) return RedirectToPage("PersonDetail", new { slug = tenant.Current.Slug, pid = Pid, Ok = "plink" });
        Error = PeopleService.Message(e);
        return await LoadAsync(ct) ? Page() : NotFound();
    }
}

/// <summary>`/{slug}/admin/members/new` 사람 추가(명부에 올리기). 부서 관리자 · 교사.</summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class PersonAddModel(PeopleService people, MemberAdminService members, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "사람 추가";
    public TenantContext Tenant => tenant;
    public List<(string Code, string Name)> RoleCodes { get; private set; } = [];
    public string? Error { get; private set; }
    public bool CanStaff => tenant.IsAdmin || (tenant.IsApproved && UserRole.KindOf(tenant.RoleCode) == UserRoleKind.Teacher);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!CanStaff) return Forbid();
        RoleCodes = PersonUi.Roles(await members.RoleCodesAsync(ct));
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? name, string? roleCode, int? month, int? day, string? phone1, string? phone2, CancellationToken ct)
    {
        if (!CanStaff) return Forbid();
        var (e, id) = await people.AddAsync(User.UserName()!, new PersonInput(name, roleCode, month, day, phone1, phone2), ct);
        if (e == PeopleError.None) return RedirectToPage("PersonDetail", new { slug = tenant.Current.Slug, pid = id, Ok = "padd" });
        Error = PeopleService.Message(e);
        RoleCodes = PersonUi.Roles(await members.RoleCodesAsync(ct));
        return Page();
    }
}
