using System.Text.Json;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Copy;

/// <summary>보기 도우미: 진도 % 를 소수 한 자리로.</summary>
public static class CopyPageBaseHelpers
{
    public static string P(double p) => p.ToString("0.0");
}

/// <summary>성경필사 화면 공통(ADR 0099): 필사를 켠 부서만(임시 부서 제외), 아니면 404.</summary>
public abstract class CopyPageBase(CopyService copy, TenantContext tenant) : PageModel, ITitled
{
    public abstract string Title { get; }
    public TenantContext Tenant => tenant;
    public CopyService Copy => copy;
    public string Slug => tenant.Current.Slug;
    protected bool Off => !tenant.Current.CopyEnabled || tenant.IsLobby;
    public string WriteUrl(byte book, short chapter, short? verse = null) => $"/{Slug}/copy/write/{book}/{chapter}" + (verse is > 0 ? $"?v={verse}" : "");
    public static string Pct(double p) => p.ToString("0.0");
}

/// <summary>`/{slug}/copy` 필사 홈(확정 시안 성경필사 ①): 내 진도 · 이어 쓰기 · 완필 선물 · 필사 진도 · 최근 쓴 곳.</summary>
public class IndexModel(CopyService copy, TenantContext tenant, CopyGiftService gifts, BirthdayGiftService linkGifts) : CopyPageBase(copy, tenant)
{
    public override string Title => "나의 필사";
    public CopySummary Data { get; private set; } = null!;
    /// <summary>고를 수 있는 내 완필 선물(완독 선물 카드와 같은 모양).</summary>
    public IReadOnlyList<BirthdayGiftView> MyGifts { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Off) return NotFound();
        await gifts.EnsureAsync(ct);   // 신약 · 구약을 다 썼으면 완필 선물(R-307)
        Data = await Copy.SummaryAsync(ct);
        MyGifts = (await linkGifts.MineAsync(ct)).Where(g => g.Kind == GiftKinds.Copy).ToList();
        return Page();
    }
}

/// <summary>`/{slug}/copy/books?b=` 장 고르기(확정 시안 ③): 구약 · 신약 탭, 책마다 진도, 연 책의 장 칸.</summary>
public class BooksModel(CopyService copy, TenantContext tenant) : CopyPageBase(copy, tenant)
{
    public override string Title => "장 고르기";
    [BindProperty(SupportsGet = true)] public byte? B { get; set; }
    [BindProperty(SupportsGet = true)] public string? T { get; set; }
    public IReadOnlyList<CopyBook> Books { get; private set; } = [];
    public IReadOnlyDictionary<(byte, short), int> ChapterDone { get; private set; } = new Dictionary<(byte, short), int>();
    public IReadOnlyDictionary<(byte Book, short Chapter), short> Counts { get; private set; } = new Dictionary<(byte, short), short>();
    public bool NewTab => T is null ? B is not byte b || b >= 40 : T == "nt";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Off) return NotFound();
        (Books, ChapterDone) = await Copy.BooksAsync(ct);
        Counts = await Copy.CountsAsync(ct);
        return Page();
    }
}

/// <summary>
/// `/{slug}/copy/write/{book}/{chapter}?v=` 따라 치기(확정 시안 ②): 세 번역 본문을 화면에 넘기고, 글자 비교 · 색칠은 copy.js 가 한다.
/// 다 맞게 치면 [Save] 로 보내고, 서버가 글자만 다시 맞춰 보고 저장한다(R-306).
/// </summary>
public class WriteModel(CopyService copy, TenantContext tenant, BibleTextService bible, CopyGiftService gifts) : CopyPageBase(copy, tenant)
{
    public override string Title => Data is null ? "따라 쓰기" : $"{Data.Name} {Data.Chapter}{(Data.BookId == 19 ? "편" : "장")}";
    [BindProperty(SupportsGet = true)] public byte Book { get; set; }
    [BindProperty(SupportsGet = true)] public short Chapter { get; set; }
    [BindProperty(SupportsGet = true)] public short? V { get; set; }
    public CopyChapter? Data { get; private set; }
    public string Json { get; private set; } = "{}";
    public (byte Book, short Chapter)? Prev { get; private set; }
    public (byte Book, short Chapter)? Next { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Off) return NotFound();
        Data = await Copy.ChapterAsync(bible, Book, Chapter, V, ct);
        if (Data is null) return NotFound();
        var order = (await Copy.CountsAsync(ct)).Keys.OrderBy(k => k.Book).ThenBy(k => k.Chapter).ToList();
        var i = order.IndexOf((Book, Chapter));
        Prev = i > 0 ? order[i - 1] : null;
        Next = i >= 0 && i < order.Count - 1 ? order[i + 1] : null;
        Json = JsonSerializer.Serialize(new
        {
            book = Book, chapter = Chapter, start = Data.Start, done = Data.Done.OrderBy(x => x).ToArray(),
            verses = Data.Lines.Select(l => new { v = l.Verse, t = l.Texts }).ToArray(),
            codes = BibleTextService.Codes, names = BibleTextService.Codes.Select(c => c switch { BibleTranslation.Krv => "개역개정", BibleTranslation.Klb => "현대인의성경", _ => "NIV" }).ToArray(),
            next = Next is { } n ? WriteUrl(n.Book, n.Chapter) : null, nextName = Next is { } n2 ? $"{BibleBooks.NameOf(n2.Book)} {n2.Chapter}장" : null,
        });
        return Page();
    }

    /// <summary>절 하나 저장(fetch, JSON). 글자가 다르면 저장하지 않는다. 신약 · 구약을 막 다 썼으면 완필 선물을 만든다.</summary>
    public async Task<IActionResult> OnPostSaveAsync(byte book, short chapter, short verse, string? translation, string? typed, CancellationToken ct)
    {
        if (Off) return NotFound();
        var r = await Copy.SaveAsync(bible, book, chapter, verse, translation ?? "", typed, ct);
        if (r.NewlyDone) await gifts.EnsureAsync(ct);
        return new JsonResult(new
        {
            ok = r.Error == CopySaveError.None, chapterDone = r.ChapterDone, chapterVerses = r.ChapterVerses, newlyDone = r.NewlyDone,
            error = r.Error switch { CopySaveError.Mismatch => "본문과 글자가 달라요. 빨간 글자를 고쳐 주세요.", CopySaveError.NoVerse => "없는 절이에요.", CopySaveError.BadTranslation => "번역을 다시 골라 주세요.", _ => null },
        });
    }
}

/// <summary>`/{slug}/copy/participants` 참여자 현황: 이 부서 소속원의 필사 진도(통독 참여자 현황처럼, 점수 없음).</summary>
public class ParticipantsModel(CopyService copy, TenantContext tenant) : CopyPageBase(copy, tenant)
{
    public override string Title => "필사 참여자 현황";
    public IReadOnlyList<CopyBoardRow> Rows { get; private set; } = [];
    public CopySummary Totals { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Off) return NotFound();
        Rows = await Copy.BoardAsync(ct);
        Totals = await Copy.SummaryAsync(ct);
        return Page();
    }
}
