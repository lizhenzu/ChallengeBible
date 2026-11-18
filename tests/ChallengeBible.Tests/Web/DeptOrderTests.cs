using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>부서 나이순(2026-11-15 사용자): 시험 도구 상황별 로그인의 부서 버튼 순서.</summary>
public class DeptOrderTests
{
    [Fact]
    public void 영아부_유아부_유치부_유년부_초등부_중등부_고등부_순이고_모르는_부서는_맨_뒤()
    {
        var made = new[] { "잠실새내교회 유년부", "잠실새내교회 초등부", "잠실새내교회 중등부", "잠실새내교회 고등부", "잠실새내교회 영아부", "잠실새내교회 유아부", "잠실새내교회 유치부", "어른 통독 모임" };
        Assert.Equal(new[] { "잠실새내교회 영아부", "잠실새내교회 유아부", "잠실새내교회 유치부", "잠실새내교회 유년부", "잠실새내교회 초등부", "잠실새내교회 중등부", "잠실새내교회 고등부", "어른 통독 모임" },
            made.OrderBy(DeptOrder.Key).ToArray());
    }

    [Fact]
    public void 플랫폼_부서_카드는_연령별이고_고등부가_맨_나중이다()
    {
        var made = new[] { "잠실새내교회 고등부", "잠실새내교회 초등부", "어른 통독 모임", "잠실새내교회 유년부", "잠실새내교회 중등부", "잠실새내교회 영아부" };
        Assert.Equal(new[] { "어른 통독 모임", "잠실새내교회 영아부", "잠실새내교회 유년부", "잠실새내교회 초등부", "잠실새내교회 중등부", "잠실새내교회 고등부" },
            made.OrderBy(DeptOrder.AgeLastKey).ToArray());
    }

    [Fact]
    public void 청년부_장년부는_고등부_다음이다()   // 2026-11-18 사용자 "새로 입력한 부서 나이별로 정렬 안 됨"
    {
        var made = new[] { "잠실새내교회 장년부", "잠실새내교회 청년부", "처음 오신 분", "잠실새내교회 고등부", "잠실새내교회 영아부" };
        Assert.Equal(new[] { "처음 오신 분", "잠실새내교회 영아부", "잠실새내교회 고등부", "잠실새내교회 청년부", "잠실새내교회 장년부" },
            made.OrderBy(DeptOrder.AgeLastKey).ToArray());
        Assert.Equal(new[] { "잠실새내교회 영아부", "잠실새내교회 고등부", "잠실새내교회 청년부", "잠실새내교회 장년부", "처음 오신 분" },
            made.OrderBy(DeptOrder.Key).ToArray());
    }
}
