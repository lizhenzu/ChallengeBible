using ChallengeBible.Domain.Rules;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/rules` 기본 규칙(확정 시안: 플랫폼-기본규칙, ADR 0060). "바뀐 점 보기"로 서버가 바뀐 값 · 오류를 보여 주고,
/// 확인한 뒤 "새 버전으로 저장". 저장은 늘 새 버전(내일 또는 다음 주 시작일부터).
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class RulesModel(RuleService rules, PlatformRuleService platformRules) : PageModel, ITitled
{
    public string Title => "기본 규칙";
    private int Me => User.AccountId()!.Value;

    [BindProperty] public RuleForm Form { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public IReadOnlyList<RuleVersion> Versions { get; private set; } = [];
    public RuleVersion Latest => Versions[^1];
    public RuleSet Applied { get; private set; } = RuleSet.Default;
    public int AppliedNo { get; private set; }
    public DateOnly Today { get; private set; }
    public (DateOnly Tomorrow, DateOnly NextWeek) Starts { get; private set; }
    public int MissionTenants { get; private set; }
    public Dictionary<int, string> Names { get; private set; } = [];
    public List<string> Changes { get; private set; } = [];
    public List<string> Errors { get; private set; } = [];
    public bool Previewed { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        Form = RuleForm.Of(Latest.Rules);
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        Inspect();
        Previewed = Errors.Count == 0 && Changes.Count > 0;
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        var (next, parseErrors) = Form.ToRuleSet(Latest.Rules);
        if (parseErrors.Count > 0) { Errors = parseErrors; return Page(); }
        var (saved, errors) = await platformRules.SaveAsync(Me, next!, Form.From == "week" ? Starts.NextWeek : Starts.Tomorrow, ct);
        if (saved is null) { Errors = errors; Inspect(); return Page(); }
        return RedirectToPage(new { Ok = $"v{saved.VersionNo}" });
    }

    private void Inspect()
    {
        var (next, parseErrors) = Form.ToRuleSet(Latest.Rules);
        Errors = parseErrors;
        if (next is null) return;
        Errors.AddRange(PlatformRuleService.Check(Latest.Rules, next));
        Changes = PlatformRuleService.Diff(Latest.Rules, next);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Versions = await rules.VersionsAsync(ct);
        Today = platformRules.TodayDate;
        Applied = RuleService.Pick(Versions, Today);
        AppliedNo = Versions.Where(v => v.EffectiveFrom <= Today).Max(v => v.VersionNo);
        Starts = platformRules.StartChoices(Latest.Rules);
        MissionTenants = await platformRules.MissionTenantsAsync(Me, ct);
        Names = await platformRules.NamesAsync(Me, Versions.Where(v => v.ChangedByAccountId is not null).Select(v => v.ChangedByAccountId!.Value), ct);
    }

    public static string MissionLabel(MissionItem item) => item.Mission() switch
    {
        Domain.Rules.Mission.Word => "말씀", Domain.Rules.Mission.Prayer => "기도", Domain.Rules.Mission.Quiz => "퀴즈", _ => "암송",
    };
}

/// <summary>화면 입력. 주 시작 요일 · 항목 목록 · 가족 단계 줄 수는 지금 규칙 그대로 둔다(1차 확정).</summary>
public sealed class RuleForm
{
    public int GraceDays { get; set; }
    public int QuizMaxAttempts { get; set; }
    public List<ItemForm> Items { get; set; } = [];
    /// <summary>축하 단계 "3000, 5000, 8000".</summary>
    public string Milestones { get; set; } = "";
    public List<FamilyForm> Family { get; set; } = [];
    public bool Ranking { get; set; }
    public bool FamilyTeam { get; set; }
    public bool TranslationCompare { get; set; }
    /// <summary>tomorrow | week</summary>
    public string From { get; set; } = "tomorrow";

    public sealed class ItemForm
    {
        public string Code { get; set; } = "";
        public bool Enabled { get; set; }
        public string Name { get; set; } = "";
        public int Points { get; set; }
    }

    public sealed class FamilyForm
    {
        public int Points { get; set; }
        public FamilyMilestoneKind Kind { get; set; }
        public string Message { get; set; } = "";
        public string Emoji { get; set; } = "";
        public bool Enabled { get; set; }
    }

    public static RuleForm Of(RuleSet r) => new()
    {
        GraceDays = r.GraceDays,
        QuizMaxAttempts = r.QuizMaxAttempts,
        Items = r.Items.Select(i => new ItemForm { Code = i.Item.Code(), Enabled = i.Enabled, Name = i.DisplayName, Points = i.Points }).ToList(),
        Milestones = string.Join(", ", r.Milestones),
        Family = r.FamilyMilestones.Select(f => new FamilyForm { Points = f.Points, Kind = f.Kind, Message = f.Message, Emoji = f.Emoji, Enabled = f.Enabled }).ToList(),
        Ranking = r.Features.Ranking,
        FamilyTeam = r.Features.FamilyTeam,
        TranslationCompare = r.Features.TranslationCompare,
    };

    public (RuleSet? Rules, List<string> Errors) ToRuleSet(RuleSet current)
    {
        var errors = new List<string>();
        var ms = new List<int>();
        foreach (var part in Milestones.Split([',', ' ', '·'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Replace("점", ""), System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var v)) ms.Add(v);
            else errors.Add($"축하 단계 \"{part}\"는 숫자가 아니에요.");
        }
        var items = new List<MissionItemRule>();
        foreach (var i in Items)
        {
            if (!MissionItemCodes.TryParse(i.Code, out var item)) { errors.Add("알 수 없는 미션 항목이에요."); continue; }
            items.Add(new MissionItemRule(item, i.Enabled, i.Name?.Trim() ?? "", i.Points));
        }
        if (errors.Count > 0) return (null, errors);
        return (current with
        {
            GraceDays = GraceDays,
            QuizMaxAttempts = QuizMaxAttempts,
            Items = items,
            Milestones = ms.Order().ToList(),
            FamilyMilestones = Family.Select(f => new FamilyMilestoneRule(f.Points, f.Kind, f.Message?.Trim() ?? "", f.Emoji?.Trim() ?? "", f.Enabled)).ToList(),
            Features = new FeatureSwitches(Ranking, FamilyTeam, TranslationCompare),
        }, errors);
    }
}
