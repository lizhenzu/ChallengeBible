using Xunit;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 브라우저 없이는 못 보는 스타일 버그가 다시 생기지 않게 하는 글자 검사(2026-11-24 사용자 신고 두 건).
/// ① 「이번 주」 막대가 하나도 안 그려짐 — 막대 높이를 격자 칸 안의 퍼센트(height: N%)로 줘서 0px 로 계산됐다.
/// ② 로그인하면 오른쪽 위 이름 칩이 빈 테두리만 보임 — 380px 이하에서 이름을 숨기는 규칙이 사진 동그라미 유무와 상관없이 걸렸다.
/// </summary>
public class ScreenStyleGuardTests
{
    private static string Css(string name)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "ChallengeBible.Web"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, "src", "ChallengeBible.Web", "wwwroot", "css", name));
    }

    [Fact]
    public void 이번_주_막대_높이는_변수로_계산한다()
    {
        var css = Css("landing.css");
        Assert.Contains(".vb-ch-week i { display: block; flex: none; width: 100%; height: calc((100% - 18px) * var(--h, 0) / 100);", css);
        Assert.DoesNotContain(".vb-ch-week div { display: grid", css);   // 격자 칸 안의 퍼센트 높이는 0 이 된다
    }

    [Fact]
    public void 좁은_화면에서_이름_숨김은_사진_동그라미가_있을_때만_걸린다()
    {
        var home = Css("home.css");
        Assert.Contains(".hmx-me:has(.av) b { display: none; }", home);
        Assert.DoesNotContain(".hmx-me b { display: none; }", home.Replace(".hmx-me:has(.av) b { display: none; }", ""));
        var landing = Css("landing.css");
        Assert.Contains(".vb .hmx-me:has(.av) b { display: none; }", landing);
    }

    [Fact]   // 2026-11-24 사용자: 첫 화면 맨 아래 「© 잠실새내 · 주일학교」 위 가는 줄 삭제
    public void 첫_화면_맨_아래에는_가는_줄이_없다()
    {
        var line = Css("landing.css").Split('\n').First(l => l.StartsWith(".vb-footrow {"));
        Assert.DoesNotContain("border-top", line);
    }

    [Fact]   // 2026-11-24 사용자: 「나의 도전」 이번 주 빈 공백 · 작은 회색 글자 대비
    public void 나의_도전은_기록이_없으면_막대_영역을_낮추고_작은_글자를_진하게_쓴다()
    {
        var css = Css("landing.css");
        Assert.Contains(".vb-ch-week.empty { height: 38px; }", css);
        Assert.Contains(".vb.vb-ch { --ink-3: #6e6e75; }", css);
    }
}
