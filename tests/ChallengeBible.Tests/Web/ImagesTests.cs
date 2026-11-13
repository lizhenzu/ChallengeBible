using System.Net;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// R-274: 게시 뒤 서버에 넣은 그림(선물 이미지 /images/AwardItem/{상품번호}.jpg)도 열린다. 얼굴 사진 폴더(/profile)는 계속 막힌다.
/// 시험에서는 그림 폴더를 임시 폴더(설정 Images:Dir)로 바꿔 저장소를 더럽히지 않는다.
/// </summary>
public class ImagesTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-images-" + Guid.NewGuid().ToString("N"));

    public ImagesTests(WebApplicationFactory<Program> app)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "AwardItem"));
        File.WriteAllBytes(Path.Combine(_dir, "AwardItem", "1342.jpg"), [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);
        _app = app.WithWebHostBuilder(b =>
        {
            b.UseSetting("Images:Dir", _dir);
            b.ConfigureServices(s =>
            {
                s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
                s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            });
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task R274_나중에_넣은_선물_이미지가_열리고_없는_번호는_404_얼굴_사진_폴더는_막힌다()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await c.GetAsync("/images/AwardItem/1342.jpg");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/jpeg", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(7, (await r.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/images/AwardItem/9999.jpg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/profile/anyone.jpg")).StatusCode);
    }
}
