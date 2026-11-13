namespace ChallengeBible.Tests.Web;

/// <summary>R-265: 휴대폰 다크 모드와 상관없이 모든 화면은 항상 밝게 보인다(사용자 2026-11-13).</summary>
public class ThemeTests
{
    private static string WebDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "src", "ChallengeBible.Web");
            if (Directory.Exists(p)) return p;
        }
        throw new DirectoryNotFoundException("src/ChallengeBible.Web");
    }

    [Fact]
    public void R265_다크_모드_규칙이_없고_화면은_항상_밝게_고정한다()
    {
        var web = WebDir();
        var files = Directory.GetFiles(Path.Combine(web, "wwwroot"), "*.css", SearchOption.AllDirectories)
            .Append(Path.Combine(web, "wwwroot", "offline.html"));
        foreach (var f in files)
            Assert.False(File.ReadAllText(f).Contains("prefers-color-scheme"), Path.GetFileName(f) + " 에 다크 모드 규칙이 남아 있어요");
        Assert.Contains("<meta name=\"color-scheme\" content=\"light\" />", File.ReadAllText(Path.Combine(web, "Pages", "Shared", "_Layout.cshtml")));
    }
}
