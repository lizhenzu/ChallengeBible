using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>대기 카드(ADR 0082 · 2026-11-25 시안 「페이지 이동 대기」): 공통 레이아웃에서 불러온다. 링크 0.2초 뒤 이동 · 로딩 → 지연 → 오류 · 큰 동작(읽음 표시 · 사진 올리기)에서도 쓴다.</summary>
public class WaitCardTests(WebApplicationFactory<Program> app) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly string _db = InMemoryDb.NewName();

    private HttpClient Client() => app.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
        s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
        s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
    })).CreateClient();

    [Fact]
    public async Task Layout_loads_wait_script()
    {
        var html = await Client().GetStringAsync("/account/login");
        Assert.Matches(@"src=""/js/wait\.[^""]*js", html);   // 파일 이름에 지문(hash)이 붙는다
    }

    [Theory]
    [InlineData("읽음으로 표시하고 있어요")]
    [InlineData("읽음 표시를 취소하고 있어요")]
    public async Task Mark_read_button_has_card_message(string message)
    {
        var js = await Client().GetStringAsync("/js/mission.js");
        Assert.Contains(message, js);
    }

    [Fact]
    public async Task Reading_list_mark_button_uses_card()
    {
        var js = await Client().GetStringAsync("/js/reading.js");
        Assert.Contains("cbWait.during", js);
        Assert.Contains("읽음으로 표시하고 있어요", js);
    }

    [Theory]
    [InlineData("이동 중입니다")]
    [InlineData("기다려 주세요")]
    [InlineData("평소보다 조금 오래 걸리고 있어요")]
    [InlineData("이동하지 못했어요")]
    [InlineData("다시 시도")]
    [InlineData("홈으로 돌아가기")]
    [InlineData("저장하고 있어요")]
    public async Task Script_has_message(string message)
    {
        var js = await Client().GetStringAsync("/js/wait.js");
        Assert.Contains(message, js);
    }

    [Fact]
    public async Task 링크_이동은_0점2초만_늦추고_폼은_늦추지_않는다()
    {
        // 2026-11-25 사용자: 0.6초 지연을 0.2초로 — 카드는 그대로 띄운다(시안 「페이지 이동 대기」)
        var js = await Client().GetStringAsync("/js/wait.js");
        Assert.Contains("LINK_HOLD_MS = 200", js);
        Assert.Contains("addEventListener('click'", js);
        Assert.Contains("addEventListener('submit'", js);
        Assert.Contains("setTimeout(() => location.assign(url), LINK_HOLD_MS)", js);   // 폼에는 이 늦춤이 없다(submit 처리기는 start 만)
    }

    [Fact]
    public async Task 사진을_올리며_저장할_때도_대기_카드에_진행률을_보인다()
    {
        var c = Client();
        var photo = await c.GetStringAsync("/js/photo.js");
        Assert.Contains("사진을 올리고 있어요", photo);   // 2026-11-14 사용자: 사진 등록 때 진행 상태 · 대기 카드
        Assert.Contains("upload.onprogress", photo);
        Assert.Contains("show: (message) => start(message, { keep: true })", await c.GetStringAsync("/js/wait.js"));
    }
}
