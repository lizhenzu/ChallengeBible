using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Tests.Web;

/// <summary>성경 검색(R-163) · 부서 인기 검색어(R-164) · 번역 나란히(R-165).</summary>
public class BibleSearchServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;

    public BibleSearchServiceTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        using var raw = InMemoryDb.Raw(_db);
        void Add(byte book, short ch, short v, string krv, string niv)
        {
            raw.BibleVerses.Add(new BibleVerse { TranslationId = 1, BookId = book, Chapter = ch, Verse = v, Text = krv });
            raw.BibleVerses.Add(new BibleVerse { TranslationId = 2, BookId = book, Chapter = ch, Verse = v, Text = "현대 " + krv });
            raw.BibleVerses.Add(new BibleVerse { TranslationId = 3, BookId = book, Chapter = ch, Verse = v, Text = niv });
        }
        Add(1, 1, 1, "태초에 하나님이 천지를 창조하시니라", "In the beginning God created");
        Add(43, 3, 16, "하나님이 세상을 이처럼 사랑하사", "For God so loved the world");
        Add(46, 13, 13, "믿음 소망 사랑 이 세 가지는 항상 있을 것인데 그 중의 제일은 사랑이라", "faith, hope and love");
        Add(62, 4, 8, "하나님은 사랑이심이라", "God is love");
        raw.SaveChanges();
    }

    private BibleSearchService In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var tc = new TenantContext();
        tc.Set(t, null);
        return new BibleSearchService(new InMemoryDb(_db, scope), tc, _time);
    }

    [Fact]
    public async Task R163_단어로_찾고_구약_신약_책별로_센다()
    {
        var r = await In(_kids).SearchAsync("사랑", [BibleTranslation.Krv], "all");
        Assert.Equal(3, r.Total);
        Assert.Equal(0, r.Ot);
        Assert.Equal(3, r.Nt);
        Assert.Equal([(byte)43, (byte)46, (byte)62], r.Books.Select(b => b.BookId).ToArray());
        var one = await In(_kids).SearchAsync("사랑", [BibleTranslation.Krv], "46");
        Assert.Equal(1, one.Shown);
        Assert.Equal(3, one.Total);                        // 범위를 좁혀도 전체 개수는 그대로 보인다
        Assert.Empty((await In(_kids).SearchAsync("노아", [BibleTranslation.Krv], "all")).Hits);
    }

    [Fact]
    public async Task R163_영어로_찾으면_NIV_에서_찾는다()
    {
        var r = await In(_kids).SearchAsync("love", [BibleTranslation.Niv], "all");
        Assert.Equal(3, r.Total);                           // "loved" · "love" 두 번 — 대소문자는 DB 정렬 규칙을 따른다
        Assert.Equal(0, r.Ot);
    }

    [Fact]
    public async Task R165_번역을_모두_고르면_절마다_세_번역을_나란히()
    {
        var r = await In(_kids).SearchAsync("천지", BibleTextService.Codes, "all");
        var hit = Assert.Single(r.Hits);
        Assert.Equal(["KRV", "KLB", "NIV"], hit.Texts.Keys.OrderBy(k => Array.IndexOf(BibleTextService.Codes, k)).ToArray());
    }

    [Theory]
    [InlineData("요한복음 3:16", 43, 3, 16)]
    [InlineData("요 3:16", 43, 3, 16)]
    [InlineData("창세기 24", 1, 24, 1)]
    [InlineData("창세기 24장 3절", 1, 24, 3)]
    [InlineData("시편 23편", 19, 23, 1)]
    public void R163_구절_주소면_그_본문으로_간다(string q, int book, int ch, int v)
    {
        Assert.Equal(((byte)book, (short)ch, (short)v), BibleSearchService.ParseReference(q));
    }

    [Theory]
    [InlineData("사랑")]
    [InlineData("창세기 99")]
    [InlineData("없는책 1:1")]
    public void 구절_주소가_아니면_단어로_찾는다(string q) => Assert.Null(BibleSearchService.ParseReference(q));

    [Fact]
    public void 검색어는_공백을_정리하고_50자로_자른다()
    {
        Assert.Equal("믿음 소망", BibleSearchService.Normalize("  믿음    소망 "));
        Assert.Equal(50, BibleSearchService.Normalize(new string('가', 80)).Length);
    }

    [Fact]
    public async Task R164_인기_검색어는_부서별로_최근_30일만_센다()
    {
        foreach (var w in new[] { "사랑", "사랑", "믿음", "사랑", "노아" }) await In(_kids).LogAsync(w);
        await In(_elem).LogAsync("다윗");
        _time.Now = _time.Now.AddDays(-40);
        await In(_kids).LogAsync("옛날말");
        _time.Now = _time.Now.AddDays(40);

        var top = await In(_kids).PopularAsync();
        Assert.Equal([("사랑", 3), ("노아", 1), ("믿음", 1)], top.ToArray());   // 같은 수면 가나다순
        Assert.DoesNotContain(top, x => x.Term == "다윗");                     // 다른 부서 것은 안 보인다
        Assert.DoesNotContain(top, x => x.Term == "옛날말");                   // 30일 지난 것도
        Assert.Equal(["다윗"], (await In(_elem).PopularAsync()).Select(x => x.Term).ToArray());
    }
}
