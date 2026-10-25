using System.Globalization;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Reading;

// 도전!!!성경통독 화면(확정 시안: 통독-대시보드 · 통독-성경목록, 본문은 말씀-성경읽기-v2 모양 + 장 번호 줄 — ADR 0057)

/// <summary>통독 화면 공통: 부서가 통독을 껐으면 404(R-150). 기록은 로그인 계정 것(R-158).</summary>
public abstract class ReadingPageBase(ReadingService reading, TenantContext tenant) : PageModel, ITitled
{
    public abstract string Title { get; }
    public TenantContext Tenant => tenant;
    public ReadingService Reading => reading;
    public string Slug => tenant.Current.Slug;
    public static readonly CultureInfo Ko = new("ko-KR");

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.ReadingEnabled) return NotFound();
        return await LoadAsync(ct);
    }

    protected abstract Task<IActionResult> LoadAsync(CancellationToken ct);

    public string ReadUrl(byte book, short chapter, short? verse = null) =>
        $"/{Slug}/reading/read/{book}/{chapter}" + (verse is > 1 ? $"?v={verse}" : "");
    public string ListUrl(string testament, byte? book = null, bool mark = false) =>
        $"/{Slug}/reading/list?t={testament}" + (book is null ? "" : $"&b={book}") + (mark ? "&mode=mark" : "") + (book is null ? "" : $"#b{book}");
    public static string BookName(byte id) => BibleBooks.NameOf(id);
    public static string TestamentOf(byte id) => id <= 39 ? "ot" : "nt";
}

/// <summary>`/{slug}/reading` 대시보드(R-155 · R-156 · R-161).</summary>
public class IndexModel(ReadingService reading, TenantContext tenant, BibleTextService bible) : ReadingPageBase(reading, tenant)
{
    public override string Title => "성경통독";
    public ReadingSummary Data { get; private set; } = null!;
    public string? ResumeText { get; private set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "ot";

    protected override async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        Data = await Reading.SummaryAsync(ct);
        var r = Data.Resume;
        var lines = await bible.RangeAsync(new BibleRef(r.BookId, r.Chapter, r.Verse, r.Chapter, r.Verse), [BibleTranslation.Krv], ct);
        ResumeText = lines.FirstOrDefault()?.Texts.GetValueOrDefault(BibleTranslation.Krv);
        return Page();
    }
}

/// <summary>
/// `/{slug}/reading/search?q=&tr=&s=` 성경 검색(확정 시안: 통독-성경검색, R-163 · R-164 · R-165).
/// 검색어가 없으면 추천 단어 · 부서 인기 검색어, 있으면 결과. 구절 주소면 그 본문으로 바로 간다.
/// </summary>
public class SearchModel(ReadingService reading, TenantContext tenant, BibleSearchService search, TenantClock clock) : ReadingPageBase(reading, tenant)
{
    public override string Title => "성경 검색";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "tr")] public string Tr { get; set; } = BibleTranslation.Krv;
    /// <summary>범위: all · ot · nt · 책 번호</summary>
    [BindProperty(SupportsGet = true, Name = "s")] public string S { get; set; } = "all";

    public string Term { get; private set; } = "";
    public SearchResult? Result { get; private set; }
    public IReadOnlyList<(string Term, int Count)> Popular { get; private set; } = [];
    public IReadOnlyList<string> Shown { get; private set; } = [BibleTranslation.Krv];
    public DateTimeOffset NowLocal { get; private set; }

    protected override async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        Term = BibleSearchService.Normalize(Q);
        if (BibleSearchService.ParseReference(Term) is { } at) return Redirect(ReadUrl(at.Book, at.Chapter, at.Verse));
        if (!BibleTextService.Codes.Contains(Tr) && Tr != "ALL") Tr = BibleTranslation.Krv;
        Shown = Tr == "ALL" ? BibleTextService.Codes : [Tr];
        NowLocal = TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(Tenant.Current.TimeZoneId));
        if (Term.Length == 0)
        {
            Popular = await search.PopularAsync(ct);
            return Page();
        }
        Result = await search.SearchAsync(Term, Shown, S, ct);
        // 번역 · 범위만 바꿔 다시 보는 것은 한 번 찾은 것으로 친다(인기 검색어를 부풀리지 않게)
        if (Request.Query["s"].Count == 0 && Request.Query["tr"].Count == 0) await search.LogAsync(Term, ct);
        return Page();
    }

    public string Url(string? tr = null, string? s = null) =>
        $"/{Slug}/reading/search?q={Uri.EscapeDataString(Term)}&tr={tr ?? Tr}&s={s ?? S}";

    /// <summary>본문을 HTML 로 바꾸고 검색어를 &lt;mark&gt; 로 감싼다(대소문자 무시).</summary>
    public Microsoft.AspNetCore.Html.IHtmlContent Highlight(string text)
    {
        var enc = System.Net.WebUtility.HtmlEncode(text);
        var t = System.Net.WebUtility.HtmlEncode(Term);
        return new Microsoft.AspNetCore.Html.HtmlString(t.Length == 0 ? enc
            : System.Text.RegularExpressions.Regex.Replace(enc, System.Text.RegularExpressions.Regex.Escape(t), m => $"<mark>{m.Value}</mark>", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }
}

/// <summary>`/{slug}/reading/participants?sort=progress|recent&q=` 참여자 현황(확정 시안: 통독-참여자현황, R-159). 점수 없음(R-157).</summary>
public class ParticipantsModel(ReadingService reading, TenantContext tenant, ReadingBoardService board) : ReadingPageBase(reading, tenant)
{
    public override string Title => "참여자 현황";
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "progress";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public ReadingBoard Board { get; private set; } = null!;
    public IReadOnlyList<BoardMember> Rows { get; private set; } = [];

    protected override async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();   // 임시 부서: 모르는 사람끼리 이름 · 진도가 보이지 않게(0064)
        Board = await board.GetAsync(ct);
        Rows = ReadingBoardService.Sort(Board.Rows, Sort, Q).ToList();
        return Page();
    }

    public string LastText(BoardMember m) => m.LastBook is byte b ? $"{BookName(b)} {m.LastChapter}{(b == 19 ? "편" : "장")} · {ReadingBoardService.Ago(m.DaysAgo!.Value)}" : "아직 시작 전";
}

/// <summary>
/// `/{slug}/reading/rounds` 회차 기록(확정 시안: 통독-회차기록, R-153 · R-154 · R-155). [새 회차 시작]은 확인(`?ask=1`) 뒤에만,
/// 지금 회차에 읽은 장이 하나도 없으면 하지 않는다(빈 회차를 쌓지 않게).
/// </summary>
public class RoundsModel(ReadingService reading, TenantContext tenant, TenantClock clock) : ReadingPageBase(reading, tenant)
{
    public override string Title => "회차 기록";
    [BindProperty(SupportsGet = true)] public bool Ask { get; set; }
    [BindProperty(SupportsGet = true)] public int? Started { get; set; }
    public IReadOnlyList<RoundRow> Rows { get; private set; } = [];
    public RoundRow Now => Rows.First(r => r.State == RoundState.Reading);
    public IEnumerable<RoundRow> Past => Rows.Where(r => r.State != RoundState.Reading);
    public DateOnly Today { get; private set; }

    protected override async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        Rows = await Reading.RoundsAsync(ct);
        Today = clock.Today(Tenant.Current.TimeZoneId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!Tenant.Current.ReadingEnabled) return NotFound();
        Rows = await Reading.RoundsAsync(ct);
        if (Now.Read == 0) return RedirectToPage(new { slug = Slug });
        var no = await Reading.StartNewRoundAsync(ct);
        return RedirectToPage(new { slug = Slug, Started = no });
    }

    public static string Ymd(DateOnly d) => d.ToString("yyyy.MM.dd");
}

/// <summary>`/{slug}/reading/bookmarks?sort=recent|bible` 북마크 모음(확정 시안: 통독-북마크, 출처 표시는 뺌).</summary>
public class BookmarksModel(ReadingService reading, TenantContext tenant, BibleMarkService marks, TenantClock clock) : ReadingPageBase(reading, tenant)
{
    public override string Title => "북마크";
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "recent";
    public IReadOnlyList<BookmarkRow> Rows { get; private set; } = [];
    public DateOnly Today { get; private set; }

    protected override async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        Rows = await marks.BookmarksAsync(Sort == "bible", ct);
        Today = clock.Today(Tenant.Current.TimeZoneId);
        return Page();
    }

    /// <summary>담은 때: 오늘이면 "오늘 07:40", 아니면 "1월 2일".</summary>
    public string When(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(Tenant.Current.TimeZoneId));
        return DateOnly.FromDateTime(local) == Today ? "오늘 " + local.ToString("HH:mm") : local.ToString("M월 d일", Ko);
    }
}

/// <summary>`/{slug}/reading/list?t=ot|nt&b=&mode=mark` 성경 목록: 책 → 장 번호 칸, 눌러 읽으러 가기 또는 바로 완료(R-151).</summary>
public class ListModel(ReadingService reading, TenantContext tenant) : ReadingPageBase(reading, tenant)
{
    public override string Title => "성경 목록";
    [BindProperty(SupportsGet = true, Name = "t")] public string T { get; set; } = "ot";
    [BindProperty(SupportsGet = true, Name = "b")] public byte? B { get; set; }
    [BindProperty(SupportsGet = true)] public string? Mode { get; set; }

    public ReadingSummary Data { get; private set; } = null!;
    public IReadOnlyDictionary<byte, IReadOnlySet<short>> Done { get; private set; } = new Dictionary<byte, IReadOnlySet<short>>();
    public bool MarkMode => Mode == "mark";

    /// <summary>분류(구약 5 · 신약 5). 책 번호 범위.</summary>
    public static readonly (string Testament, string Name, byte From, byte To)[] Groups =
    [
        ("ot", "율법서", 1, 5), ("ot", "역사서", 6, 17), ("ot", "시가서", 18, 22), ("ot", "대선지서", 23, 27), ("ot", "소선지서", 28, 39),
        ("nt", "복음서", 40, 43), ("nt", "역사서", 44, 44), ("nt", "바울서신", 45, 57), ("nt", "일반서신", 58, 65), ("nt", "예언서", 66, 66),
    ];

    protected override async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        if (T is not ("ot" or "nt")) T = "ot";
        if (B is byte b && TestamentOf(b) != T) T = TestamentOf(b);
        Data = await Reading.SummaryAsync(ct);
        Done = await Reading.DoneChaptersAsync(ct);
        return Page();
    }

    public IReadOnlySet<short> DoneOf(byte book) => Done.TryGetValue(book, out var s) ? s : new HashSet<short>();
}

/// <summary>
/// `/{slug}/reading/read/{book}/{chapter}?unit=&tr=&p=&v=` 장 본문 읽기(R-160 · R-161 · R-165).
/// 화면을 열면 보던 위치(장 · 보이는 첫 절)를 남긴다 — 읽음과 무관(R-161).
/// </summary>
public class ReadModel(ReadingService reading, TenantContext tenant, BibleTextService bible, BibleMarkService marks, BibleNoteService notes, BookOverviewService overview) : ReadingPageBase(reading, tenant)
{
    public override string Title => $"{BookName(Book)} {Chapter}장";
    [BindProperty(SupportsGet = true)] public byte Book { get; set; }
    [BindProperty(SupportsGet = true)] public short Chapter { get; set; }
    [BindProperty(SupportsGet = true)] public int Unit { get; set; }
    [BindProperty(SupportsGet = true, Name = "tr")] public string Tr { get; set; } = BibleTranslation.Krv;
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    /// <summary>이어 읽기로 들어올 때 그 절이 보이는 쪽을 연다.</summary>
    [BindProperty(SupportsGet = true, Name = "v")] public short? V { get; set; }

    public ChapterView View { get; private set; } = null!;
    public ReadPage Slice { get; private set; } = new([], 0, 1, 1, 0, 0);
    public IReadOnlyList<string> Shown { get; private set; } = [BibleTranslation.Krv];
    public IReadOnlySet<(short, short)> Bookmarked { get; private set; } = new HashSet<(short, short)>();
    public IReadOnlySet<short> DoneChapters { get; private set; } = new HashSet<short>();
    /// <summary>절(장, 절) 위에 그릴 해설 카드. 앞 장에서 시작한 해설은 1절 위.</summary>
    public IReadOnlyDictionary<(short Chapter, short Verse), IReadOnlyList<BibleNote>> Notes { get; private set; } =
        new Dictionary<(short Chapter, short Verse), IReadOnlyList<BibleNote>>();
    public int ChapterCount => BibleBooks.All().First(b => b.Id == Book).ChapterCount;
    /// <summary>본문 위 책 개요(R-116, ADR 0062). 글이 없는 책이면 null.</summary>
    public BookOverviewCard? Overview { get; private set; }

    protected override async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        if (Book is < 1 or > 66 || Chapter < 1 || Chapter > ChapterCount) return NotFound();
        var view = await Reading.ChapterAsync(Book, Chapter, ct);
        if (view is null) return NotFound();
        View = view;
        Overview = await overview.CardAsync(Book, Chapter, ct);
        if (!BibleTextService.Codes.Contains(Tr)) Tr = Tr == "ALL" ? "ALL" : BibleTranslation.Krv;
        Shown = Tr == "ALL" ? BibleTextService.Codes : [Tr];
        if (Unit is not (0 or 1 or 3 or 5)) Unit = 0;
        var range = new BibleRef(Book, Chapter, 1, Chapter, (short)view.VerseCount);
        var lines = await bible.RangeAsync(range, Shown, ct);
        if (V is short v && Unit > 0) PageNo = (lines.TakeWhile(l => l.Verse < v).Count() / Unit) + 1;
        Slice = BibleTextService.Slice(lines, Unit, PageNo);
        Bookmarked = (await marks.MarksAsync(range, ct)).Bookmarked;
        // 해설 카드(R-118, ADR 0056 결정 7): 장 전체로 자리를 정하고, 지금 쪽에 시작 절이 있는 카드만 그린다(/read 와 같음)
        if (lines.Count > 0) Notes = BibleNoteService.Place(await notes.ForRangeAsync(range, ct), range, lines);
        DoneChapters = (await Reading.DoneChaptersAsync(ct)).GetValueOrDefault(Book) ?? new HashSet<short>();
        if (Slice.Lines.Count > 0) await Reading.SavePositionAsync(Book, Chapter, Slice.Lines[0].Verse, ct);
        return Page();
    }

    public int ReadPercent => View.VerseCount == 0 ? 0 : (int)Math.Round(View.VersesRead.Count * 100.0 / View.VerseCount);
    public bool PageAllRead => Slice.Lines.Count > 0 && Slice.Lines.All(l => View.VersesRead.Contains(l.Verse));

    public string Url(int? unit = null, string? tr = null, int? page = null)
    {
        var q = new List<string> { $"unit={unit ?? Unit}", $"tr={tr ?? Tr}" };
        if ((page ?? 1) > 1) q.Add($"p={page}");
        return $"/{Slug}/reading/read/{Book}/{Chapter}?{string.Join("&", q)}";
    }
}
