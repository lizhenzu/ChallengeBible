using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/read` 성경 읽기(R-110 · R-111 · R-115, 확정 시안: 말씀-성경읽기).
/// `from=word|prayer|recite` + `date` 로 열면 그날 미션의 구절을, 아니면 `book · sc · sv · ec · ev` 로 지정한 구절을 보여 준다.
/// 읽음 · 책갈피(R-112) · 듣기 · 책 개요(R-116)는 해당 표가 생기는 다음 단계에서 붙인다.
/// </summary>
public class ReadModel(MissionService missions, TenantContext tenant, BibleTextService bible) : MissionPageBase(missions, tenant)
{
    public override string Title => "성경 읽기";

    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public byte Book { get; set; }
    [BindProperty(SupportsGet = true)] public short Sc { get; set; }
    [BindProperty(SupportsGet = true)] public short Sv { get; set; }
    [BindProperty(SupportsGet = true)] public short Ec { get; set; }
    [BindProperty(SupportsGet = true)] public short Ev { get; set; }
    /// <summary>읽기 단위: 1 · 3 · 5 · 0(전체)</summary>
    [BindProperty(SupportsGet = true)] public int Unit { get; set; } = 3;
    /// <summary>번역: KRV · KLB · NIV · ALL</summary>
    [BindProperty(SupportsGet = true, Name = "tr")] public string Tr { get; set; } = BibleTranslation.Krv;
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;

    public BibleRef? Ref { get; private set; }
    public string? Kicker { get; private set; }
    public string? DayTitle { get; private set; }
    public string? DaySummary { get; private set; }
    public string BackText => From switch { "prayer" => "기도 미션", "recite" => "암송 미션", _ => "말씀 미션" };
    public string BackPage => From switch { "prayer" => "prayer", "recite" => "recite", _ => "word" };
    public ReadPage Slice { get; private set; } = new([], 0, 1, 1, 0, 0);
    public IReadOnlyList<string> Shown { get; private set; } = [BibleTranslation.Krv];

    protected override async Task LoadMoreAsync(CancellationToken ct)
    {
        var d = Data!;
        Ref = From switch
        {
            "word" => d.Word?.Ref,
            "prayer" => d.Prayer?.Ref,
            "recite" => d.Recite?.Ref,
            _ => new BibleRef(Book, Sc, Sv, Ec == 0 ? Sc : Ec, Ev == 0 ? Sv : Ev),
        };
        Kicker = From switch { "word" => "오늘의 말씀", "prayer" => "오늘의 성품기도", "recite" => "이번 주 암송 구절", _ => "성경 읽기" };
        if (From == "word" && d.Word is not null) { DayTitle = d.Word.Title; DaySummary = d.Word.Summary; }

        if (!BibleTextService.Codes.Contains(Tr)) Tr = Tr == "ALL" ? "ALL" : BibleTranslation.Krv;
        Shown = Tr == "ALL" ? BibleTextService.Codes : [Tr];
        if (Unit is not (0 or 1 or 3 or 5)) Unit = 3;
        if (Ref is { IsValid: true } r) Slice = BibleTextService.Slice(await bible.RangeAsync(r, Shown, ct), Unit, PageNo);
    }

    /// <summary>같은 구절을 다른 설정으로 여는 주소(미션에서 온 `from` · `date` 는 그대로 둔다).</summary>
    public string Url(int? unit = null, string? tr = null, int? page = null)
    {
        var q = new List<string>();
        if (From is not null) q.Add($"from={From}");
        else if (Ref is not null) q.Add($"book={Ref.BookId}&sc={Ref.StartChapter}&sv={Ref.StartVerse}&ec={Ref.EndChapter}&ev={Ref.EndVerse}");
        if (Data is not null && Day != Data.Today) q.Add($"date={Day:yyyy-MM-dd}");
        q.Add($"unit={unit ?? Unit}");
        q.Add($"tr={tr ?? Tr}");
        if ((page ?? 1) > 1) q.Add($"p={page}");
        return $"/{Slug}/read?{string.Join("&", q)}";
    }
}
