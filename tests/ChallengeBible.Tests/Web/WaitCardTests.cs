using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>대기 카드(ADR 0082): 공통 레이아웃에서 불러온다. 2026-11-25 부터 페이지 이동에는 쓰지 않고, 큰 동작(읽음 표시 · 사진 올리기)에서만 쓴다.</summary>
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
    [InlineData("잠시만 기다려 주세요")]
    public async Task Script_has_message(string message)
    {
        var js = await Client().GetStringAsync("/js/wait.js");
        Assert.Contains(message, js);
    }

    [Fact]
    public async Task 페이지_이동과_폼_제출에는_대기_카드를_띄우지_않고_바로_이동한다()
    {
        // 2026-11-25 사용자: 페이지 이동마다 느리게 하는 효과 삭제 — 링크 · 폼 처리기가 없어야 한다
        var js = await Client().GetStringAsync("/js/wait.js");
        Assert.DoesNotContain("addEventListener('click'", js);
        Assert.DoesNotContain("addEventListener('submit'", js);
        Assert.DoesNotContain("location.assign", js);
    }

    [Fact]
    public async Task 사진을_올리며_저장할_때도_대기_카드에_진행률을_보인다()
    {
        var c = Client();
        var photo = await c.GetStringAsync("/js/photo.js");
        Assert.Contains("사진을 올리고 있어요", photo);   // 2026-11-14 사용자: 사진 등록 때 진행 상태 · 대기 카드
        Assert.Contains("upload.onprogress", photo);
        Assert.Contains("show: (message) => start(message, true)", await c.GetStringAsync("/js/wait.js"));
    }
}
