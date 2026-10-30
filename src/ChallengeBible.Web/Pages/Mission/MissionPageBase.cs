using System.Globalization;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// 미션 화면 공통: `?date=` (없으면 오늘), 내 참여자 Id, 그날 데이터.
/// 참여자 역할이 없으면 보기만(보호자 · 교사), 성경말씀을 끈 부서면 404(R-150).
/// </summary>
public abstract class MissionPageBase(MissionService missions, TenantContext tenant) : PageModel, ITitled
{
    public abstract string Title { get; }
    public TenantContext Tenant => tenant;
    public MissionService Missions => missions;

    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }

    public DateOnly Day { get; private set; }
    public int? ParticipantId { get; private set; }
    public MissionDay? Data { get; private set; }

    public bool IsToday => Data is not null && Day == Data.Today;
    public bool CanAct => ParticipantId is not null && Data?.Window == Domain.Missions.WindowResult.Open;
    /// <summary>완료 취소(R-24): 참여자가 말씀 · 기도는 오늘, 암송은 이번 주 날짜를 볼 때.</summary>
    public bool CanUndo(Domain.Rules.MissionItem item) => ParticipantId is not null && Data is not null && missions.CanUndo(item, Day);
    public string Slug => tenant.Current.Slug;

    public static readonly CultureInfo Ko = new("ko-KR");
    public string DayLabel(DateOnly d) => d.ToString("M월 d일 dddd", Ko);
    public string Link(string page, DateOnly? d = null) => $"/{Slug}/{page}" + ((d ?? Day) == Data?.Today ? "" : $"?date={(d ?? Day):yyyy-MM-dd}");

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        Day = Date ?? missions.Today;
        ParticipantId = await missions.ParticipantIdAsync(ct);
        // 참여자가 아니어도 화면은 보여 준다(콘텐츠 보기). 기록은 없으니 빈 참여자 0 으로 읽는다.
        Data = await missions.GetDayAsync(ParticipantId ?? 0, Day, ct);
        await LoadMoreAsync(ct);
        return Page();
    }

    protected virtual Task LoadMoreAsync(CancellationToken ct) => Task.CompletedTask;

    public string BlockReason => ParticipantId is null ? "보호자 · 선생님은 보기만 해요. 미션은 참여자만 할 수 있어요."
        : MissionService.WindowMessage(Data!.Window, Data.Rules.GraceDays);
}
