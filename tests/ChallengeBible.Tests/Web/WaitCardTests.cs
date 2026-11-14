using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>페이지 이동 대기 카드(ADR 0082): 공통 레이아웃에서 불러오고, 페이지별 문구를 담는다.</summary>
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
    [InlineData("말씀을 펼치고 있어요")]
    [InlineData("퀴즈를 준비하고 있어요")]
    [InlineData("기도 시간을 준비하고 있어요")]
    [InlineData("암송 구절을 꺼내고 있어요")]
    [InlineData("오늘의 미션을 불러오고 있어요")]
    [InlineData("달력을 넘기고 있어요")]
    [InlineData("점수를 모으고 있어요")]
    [InlineData("성경을 펼치고 있어요")]
    [InlineData("통독 기록을 찾고 있어요")]
    [InlineData("내 정보를 확인하고 있어요")]
    [InlineData("저장하고 있어요")]
    [InlineData("이동하고 있어요")]
    public async Task Script_has_message(string message)
    {
        var js = await Client().GetStringAsync("/js/wait.js");
        Assert.Contains(message, js);
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
