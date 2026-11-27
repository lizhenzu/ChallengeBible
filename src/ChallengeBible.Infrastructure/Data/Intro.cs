using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 부서 소개 화면의 글(ADR 0140, 2026-11-23 사용자 "유년부 전용 · 처음 들어온 사람에게"): 제목 · 소개 글 · 대표 사진 한 장.
/// 부서마다 줄이 **하나**(TenantId 가 키). 줄이 있는 부서만 소개 화면이 있고, 줄은 운영 SQL 로 한 부서(유년부)에만 만든다 —
/// 그래서 다른 부서에는 소개 화면이 보이지 않고, 부서 관리자가 스스로 만들 수도 없다(없으면 고치는 화면도 404).
/// </summary>
public sealed class TenantIntro : ITenantOwned
{
    public int TenantId { get; set; }
    public required string Title { get; set; }
    public string? Body { get; set; }
    /// <summary>대표 사진 파일 이름(부서 폴더 안 GUID.확장자). wwwroot 밖이라 서비스로만 연다.</summary>
    public string? PhotoStoredName { get; set; }
    public string? PhotoContentType { get; set; }
    /// <summary>끄면 소개 화면이 나오지 않는다(지우지 않고).</summary>
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

/// <summary>
/// 소개 화면을 사람마다 어떻게 할지(계정 기준 · 2026-11-23 사용자): 「1주일 후 다시 보기」는 HideUntilUtc, 「다시 보지 않기」는 Never.
/// 줄이 없으면 처음 들어온 사람 — 소개 화면이 나온다. 로그인하지 않은 손님은 기억할 계정이 없어 들어올 때마다 나온다.
/// </summary>
public sealed class TenantIntroSeen : ITenantOwned
{
    public int TenantId { get; set; }
    public required string UserName { get; set; }
    public DateTime? HideUntilUtc { get; set; }
    public bool Never { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

internal sealed class TenantIntroConfig : IEntityTypeConfiguration<TenantIntro>
{
    public void Configure(EntityTypeBuilder<TenantIntro> e)
    {
        e.ToTable("TenantIntro");
        e.HasKey(x => x.TenantId);
        e.Property(x => x.TenantId).ValueGeneratedNever();
        e.Property(x => x.Title).HasMaxLength(60);
        e.Property(x => x.Body).HasMaxLength(1000);
        e.Property(x => x.PhotoStoredName).HasMaxLength(60).IsUnicode(false);
        e.Property(x => x.PhotoContentType).HasMaxLength(30).IsUnicode(false);
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedByUserName).HasMaxLength(256);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TenantIntroSeenConfig : IEntityTypeConfiguration<TenantIntroSeen>
{
    public void Configure(EntityTypeBuilder<TenantIntroSeen> e)
    {
        e.ToTable("TenantIntroSeen");
        e.HasKey(x => new { x.TenantId, x.UserName });
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.HideUntilUtc).HasPrecision(3);
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}
