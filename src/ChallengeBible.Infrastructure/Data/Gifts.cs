using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

// 선물 시스템(ADR 0094, docs/design/선물-데이터-설계.md). 금액 · 크레딧 기본값은 규칙(RuleSet.Gifts)에 있다.

public static class GiftItemStatus
{
    public const string Active = "Active";
    /// <summary>삭제 대상(R-279): 쿠팡 링크 없음 · 쿠팡 아님 · 유년부 숨김. 아이에게 안 보인다.</summary>
    public const string Review = "Review";
    /// <summary>목록에서 내림(고른 기록이 있어 지우지 않음, R-279).</summary>
    public const string Retired = "Retired";
}

/// <summary>상품(R-276 ~ R-279). TenantId NULL = 공통, 값 = 그 부서 상품. 번호는 유년부 ProductID 그대로, 새 번호는 이어서.</summary>
public sealed class GiftItem : ISharedContent
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    /// <summary>공통에 올린 부서(R-277): 그 부서와 플랫폼 관리자만 고친다. 유년부 상품은 NULL.</summary>
    public int? SharedByTenantId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int Price { get; set; }
    public string? CoupangUrl { get; set; }
    public string Status { get; set; } = GiftItemStatus.Active;
    public string? ReviewReason { get; set; }
    /// <summary>/images/AwardItem/{Id}.jpg 가 있는가(없으면 기본 그림).</summary>
    public bool HasImage { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

/// <summary>부서가 숨긴 공통 상품(R-276).</summary>
public sealed class GiftItemHidden : ITenantOwned
{
    public int TenantId { get; set; }
    public int GiftItemId { get; set; }
    public DateTime HiddenUtc { get; set; }
    public string? HiddenByUserName { get; set; }
}

/// <summary>부서 선물 설정 한 줄. 빈 칸 = 규칙 기본값(R-280 · R-286 · R-288).</summary>
public sealed class GiftSetting : ITenantOwned
{
    public int TenantId { get; set; }
    public int? PersonalCap { get; set; }
    public int? NewcomerMin { get; set; }
    public int? NewcomerMax { get; set; }
    public int? InviteMin { get; set; }
    public int? InviteMax { get; set; }
    public int? BirthdayMin { get; set; }
    public int? BirthdayMax { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

/// <summary>부서 크레딧 → 금액 표(R-283). 줄이 없는 크레딧은 1크레딧 = 1원.</summary>
public sealed class GiftCreditRate : ITenantOwned
{
    public int TenantId { get; set; }
    public int Credits { get; set; }
    public int Amount { get; set; }
}

/// <summary>부서 완독 회차별 금액(R-289). 표에 없는 회차는 마지막 줄, 비면 규칙 값.</summary>
public sealed class GiftReadingAmount : ITenantOwned
{
    public int TenantId { get; set; }
    public int RoundNo { get; set; }
    public int Amount { get; set; }
}

public static class GiftKinds
{
    public const string Personal = "Personal";
    public const string Newcomer = "Newcomer";
    public const string Invite = "Invite";
    public const string Birthday = "Birthday";
    public const string Reading = "Reading";
    public const string Family = "Family";
    public const string Friend = "Friend";
    public static readonly string[] All = [Personal, Newcomer, Invite, Birthday, Reading, Family, Friend];
}

/// <summary>선물 상태(R-298). YearClosed = 새해 강제 초기화로 닫힘(R-296).</summary>
public static class GiftStates
{
    public const string Open = "Open";
    public const string Picked = "Picked";
    public const string Purchased = "Purchased";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";
    public const string Skipped = "Skipped";
    public const string Expired = "Expired";
    public const string YearClosed = "YearClosed";
    public static readonly string[] All = [Open, Picked, Purchased, Delivered, Cancelled, Skipped, Expired, YearClosed];
    /// <summary>끝나지 않은 상태 — 새해에 닫힌다.</summary>
    public static readonly string[] Unfinished = [Open, Picked, Purchased];
}

/// <summary>선물 한 건(모든 종류가 같은 장부, ADR 0094 정한 것 4). 고른 때의 상품 모습을 함께 남긴다(R-295).</summary>
public sealed class Gift : ITenantOwned
{
    public int TenantId { get; set; }
    public long Id { get; set; }
    public required string Kind { get; set; }
    public short Year { get; set; }
    public string? UserName { get; set; }
    public int? PersonId { get; set; }
    public int? FamilyTeamId { get; set; }
    /// <summary>한 번만 생기게 하는 열쇠: 생일 B2026 · 완독 R2 · 팀 단계 T1000 · 새친구 · 초대 N{사람} · 개인 P{차례}.</summary>
    public required string SourceKey { get; set; }
    public string State { get; set; } = GiftStates.Open;
    public DateOnly? OpenFrom { get; set; }
    public DateOnly? OpenUntil { get; set; }
    public int MinAmount { get; set; }
    public int MaxAmount { get; set; }
    public int? GiftItemId { get; set; }
    public string? ItemName { get; set; }
    public int? ItemPrice { get; set; }
    public bool? ItemHasImage { get; set; }
    public int? CreditsUsed { get; set; }
    public int? PointsUsed { get; set; }
    public int? PointsBurned { get; set; }
    /// <summary>로그인 없는 링크(R-287) 32자. 새로 만들면 바뀐다.</summary>
    public string? LinkToken { get; set; }
    public DateTime? PickedUtc { get; set; }
    public DateTime? PurchasedUtc { get; set; }
    public DateTime? DeliveredUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public static class GiftActions
{
    public const string Open = "Open";
    public const string Pick = "Pick";
    public const string Change = "Change";
    public const string Cancel = "Cancel";
    public const string Purchase = "Purchase";
    public const string Deliver = "Deliver";
    public const string Undo = "Undo";
    public const string Reopen = "Reopen";
    public const string Skip = "Skip";
    public const string Expire = "Expire";
    public const string Link = "Link";
    public const string YearClose = "YearClose";
}

/// <summary>선물 한 건의 동작 하나(R-295). 링크로 고르면 Actor = NULL(부모님 링크).</summary>
public sealed class GiftLog : ITenantOwned
{
    public int TenantId { get; set; }
    public long Id { get; set; }
    public long GiftId { get; set; }
    public required string Action { get; set; }
    public string? ActorUserName { get; set; }
    public DateTime AtUtc { get; set; }
    public string? ItemName { get; set; }
    public int? ItemPrice { get; set; }
    public string? Detail { get; set; }
}

/// <summary>개인 선물에 쓴 점수(R-281). 미션 기록은 그대로 두고 여기에만 적는다.</summary>
public sealed class GiftPointUse : ITenantOwned
{
    public int TenantId { get; set; }
    public long Id { get; set; }
    public required string UserName { get; set; }
    public short Year { get; set; }
    public long GiftId { get; set; }
    public int PointsUsed { get; set; }
    public int PointsBurned { get; set; }
    /// <summary>그해 처음 5,000 크레딧을 이번에 썼는가.</summary>
    public bool FirstBonus { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>새해 강제 초기화 때 옮겨 둔 지난해 시상 기록(R-296). DM2_TeamAward 와 같은 칸 + Year.</summary>
public sealed class TeamAwardArchive : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public short Year { get; set; }
    public int AwardId { get; set; }
    public required string Kind { get; set; }
    public required string AwardKey { get; set; }
    public int Threshold { get; set; }
    public DateTime AwardedAt { get; set; }
}

/// <summary>새해 강제 초기화 기록(플랫폼 표, R-296). 해마다 한 줄 — 먼저 넣은 쪽만 초기화한다.</summary>
public sealed class GiftYearClose
{
    public short Year { get; set; }
    public DateTime ClosedUtc { get; set; }
    public required string ClosedByUserName { get; set; }
}

internal sealed class GiftItemConfig : IEntityTypeConfiguration<GiftItem>
{
    public void Configure(EntityTypeBuilder<GiftItem> e)
    {
        e.ToTable("GiftItem", t =>
        {
            t.HasCheckConstraint("CK_GiftItem_Price", "[Price] >= 0");
            t.HasCheckConstraint("CK_GiftItem_Status", "[Status] IN ('Active','Review','Retired')");
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).UseIdentityColumn(1000, 1);
        e.Property(x => x.Name).HasMaxLength(100);
        e.Property(x => x.Description).HasMaxLength(500);
        e.Property(x => x.CoupangUrl).HasMaxLength(1000);
        e.Property(x => x.Status).HasMaxLength(10).IsUnicode(false);
        e.Property(x => x.ReviewReason).HasMaxLength(100);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedByUserName).HasMaxLength(256);
        e.HasIndex(x => new { x.TenantId, x.Status });
        e.HasIndex(x => new { x.Status, x.Price });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GiftItemHiddenConfig : IEntityTypeConfiguration<GiftItemHidden>
{
    public void Configure(EntityTypeBuilder<GiftItemHidden> e)
    {
        e.ToTable("GiftItemHidden");
        e.HasKey(x => new { x.TenantId, x.GiftItemId });
        e.Property(x => x.HiddenUtc).HasPrecision(3);
        e.Property(x => x.HiddenByUserName).HasMaxLength(256);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<GiftItem>().WithMany().HasForeignKey(x => x.GiftItemId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GiftSettingConfig : IEntityTypeConfiguration<GiftSetting>
{
    public void Configure(EntityTypeBuilder<GiftSetting> e)
    {
        e.ToTable("GiftSetting");
        e.HasKey(x => x.TenantId);
        e.Property(x => x.TenantId).ValueGeneratedNever();
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedByUserName).HasMaxLength(256);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GiftCreditRateConfig : IEntityTypeConfiguration<GiftCreditRate>
{
    public void Configure(EntityTypeBuilder<GiftCreditRate> e)
    {
        e.ToTable("GiftCreditRate", t => t.HasCheckConstraint("CK_GiftCreditRate_Values", "[Credits] > 0 AND [Credits] % 1000 = 0 AND [Amount] >= 0"));
        e.HasKey(x => new { x.TenantId, x.Credits });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GiftReadingAmountConfig : IEntityTypeConfiguration<GiftReadingAmount>
{
    public void Configure(EntityTypeBuilder<GiftReadingAmount> e)
    {
        e.ToTable("GiftReadingAmount", t => t.HasCheckConstraint("CK_GiftReadingAmount_Values", "[RoundNo] >= 1 AND [Amount] >= 0"));
        e.HasKey(x => new { x.TenantId, x.RoundNo });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GiftConfig : IEntityTypeConfiguration<Gift>
{
    public void Configure(EntityTypeBuilder<Gift> e)
    {
        e.ToTable("Gift", t =>
        {
            t.HasCheckConstraint("CK_Gift_Kind", "[Kind] IN ('Personal','Newcomer','Invite','Birthday','Reading','Family','Friend')");
            t.HasCheckConstraint("CK_Gift_State", "[State] IN ('Open','Picked','Purchased','Delivered','Cancelled','Skipped','Expired','YearClosed')");
            t.HasCheckConstraint("CK_Gift_Amount", "[MinAmount] >= 0 AND [MaxAmount] >= [MinAmount]");
            t.HasCheckConstraint("CK_Gift_Who", "[UserName] IS NOT NULL OR [PersonId] IS NOT NULL OR [FamilyTeamId] IS NOT NULL");
        });
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Kind).HasMaxLength(10).IsUnicode(false);
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.SourceKey).HasMaxLength(40).IsUnicode(false);
        e.Property(x => x.State).HasMaxLength(10).IsUnicode(false);
        e.Property(x => x.ItemName).HasMaxLength(100);
        e.Property(x => x.LinkToken).HasMaxLength(32).IsUnicode(false).IsFixedLength();
        e.Property(x => x.PickedUtc).HasPrecision(3);
        e.Property(x => x.PurchasedUtc).HasPrecision(3);
        e.Property(x => x.DeliveredUtc).HasPrecision(3);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.HasIndex(x => new { x.TenantId, x.Kind, x.UserName, x.PersonId, x.FamilyTeamId, x.SourceKey }).IsUnique().HasDatabaseName("UX_Gift_Source");
        e.HasIndex(x => new { x.TenantId, x.Year, x.State });
        e.HasIndex(x => new { x.TenantId, x.UserName });
        e.HasIndex(x => x.LinkToken).IsUnique().HasFilter("[LinkToken] IS NOT NULL").HasDatabaseName("UX_Gift_LinkToken");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<GiftItem>().WithMany().HasForeignKey(x => x.GiftItemId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GiftLogConfig : IEntityTypeConfiguration<GiftLog>
{
    public void Configure(EntityTypeBuilder<GiftLog> e)
    {
        e.ToTable("GiftLog");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Action).HasMaxLength(12).IsUnicode(false);
        e.Property(x => x.ActorUserName).HasMaxLength(256);
        e.Property(x => x.AtUtc).HasPrecision(3);
        e.Property(x => x.ItemName).HasMaxLength(100);
        e.Property(x => x.Detail).HasMaxLength(200);
        e.HasIndex(x => new { x.TenantId, x.GiftId, x.AtUtc });
        e.HasOne<Gift>().WithMany().HasForeignKey(x => new { x.TenantId, x.GiftId }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GiftPointUseConfig : IEntityTypeConfiguration<GiftPointUse>
{
    public void Configure(EntityTypeBuilder<GiftPointUse> e)
    {
        e.ToTable("GiftPointUse", t => t.HasCheckConstraint("CK_GiftPointUse_Points", "[PointsUsed] >= 0 AND [PointsBurned] >= 0"));
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.HasIndex(x => new { x.TenantId, x.UserName, x.Year });
        e.HasOne<Gift>().WithMany().HasForeignKey(x => new { x.TenantId, x.GiftId }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TeamAwardArchiveConfig : IEntityTypeConfiguration<TeamAwardArchive>
{
    public void Configure(EntityTypeBuilder<TeamAwardArchive> e)
    {
        e.ToTable("TeamAwardArchive");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Kind).HasMaxLength(10);
        e.Property(x => x.AwardKey).HasMaxLength(200);
        e.Property(x => x.AwardedAt).HasColumnType("datetime");
        e.HasIndex(x => new { x.TenantId, x.Year });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GiftYearCloseConfig : IEntityTypeConfiguration<GiftYearClose>
{
    public void Configure(EntityTypeBuilder<GiftYearClose> e)
    {
        e.ToTable("GiftYearClose");
        e.HasKey(x => x.Year);
        e.Property(x => x.Year).ValueGeneratedNever();
        e.Property(x => x.ClosedUtc).HasPrecision(3);
        e.Property(x => x.ClosedByUserName).HasMaxLength(256);
    }
}
