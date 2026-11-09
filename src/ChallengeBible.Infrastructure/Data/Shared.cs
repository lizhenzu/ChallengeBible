using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

// 유년부와 공유하는 표(ADR 0090, 유년부 부록 08). 칸 이름 · 형식은 08 과 같고 B · C · D 묶음에만 TenantId 를 더한다.
// 칸을 혼자 바꾸지 않는다 — 유년부가 원본이다(지시서 07 의 10장).

/// <summary>사람(아이디)에 붙는 표: 앱 전역 필터(UserName = 이 요청의 사람) + RLS. 부서 필터 대상이 아니다.</summary>
public interface IPersonOwned
{
    string UserName { get; }
}

// A. 회원 · 로그인 (Identity 스키마, TenantId 없음) ---------------------------

public sealed class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? UserName { get; set; }
    public string? NormalizedUserName { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public bool EmailConfirmed { get; set; }
    public string? PasswordHash { get; set; }
    public string? SecurityStamp { get; set; }
    public string? ConcurrencyStamp { get; set; }
    public string? PhoneNumber { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool LockoutEnabled { get; set; }
    public int AccessFailedCount { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public byte[]? ProfilePicture { get; set; }
    public int UsernameChangeLimit { get; set; }
    public DateTime InsertDT { get; set; }

    /// <summary>표시 이름 = 성 + 이름(유년부와 같다). SQL 로도 번역된다.</summary>
    public static string FullName(string? lastName, string? firstName) => ((lastName ?? "") + (firstName ?? "")).Trim();
    public string DisplayName => FullName(LastName, FirstName);
    /// <summary>플랫폼 관리자가 사용 중지한 계정.</summary>
    public bool IsDisabled(DateTimeOffset now) => LockoutEnd is { } end && end > now;
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

public sealed class AppRole
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Name { get; set; }
    public string? NormalizedName { get; set; }
    public string? ConcurrencyStamp { get; set; }
}

public sealed class AppUserRole
{
    public required string UserId { get; set; }
    public required string RoleId { get; set; }
}

public sealed class AppUserClaim
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public string? ClaimType { get; set; }
    public string? ClaimValue { get; set; }
}

public sealed class AppRoleClaim
{
    public int Id { get; set; }
    public required string RoleId { get; set; }
    public string? ClaimType { get; set; }
    public string? ClaimValue { get; set; }
}

public sealed class AppUserLogin
{
    public required string LoginProvider { get; set; }
    public required string ProviderKey { get; set; }
    public string? ProviderDisplayName { get; set; }
    public required string UserId { get; set; }
}

public sealed class AppUserToken
{
    public required string UserId { get; set; }
    public required string LoginProvider { get; set; }
    public required string Name { get; set; }
    public string? Value { get; set; }
}

/// <summary>Identity.Role 의 이름. SuperAdmin = 플랫폼 관리자, Admin = 소속 부서의 관리자(ADR 0090).</summary>
public static class IdentityRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Moderator = "Moderator";
    public const string Basic = "Basic";
    public static readonly string[] All = [SuperAdmin, Admin, Moderator, Basic];
}

/// <summary>Identity 칸을 바꿀 수 없어 따로 둔 계정 값(0090). 유년부 전송 대상이 아니다.</summary>
public sealed class UserSetting
{
    public required string UserName { get; set; }
    public bool MustChangePassword { get; set; }
    public int? LastTenantId { get; set; }
    /// <summary>통독 화면 색 테마(0073).</summary>
    public string? ReadingTheme { get; set; }
    /// <summary>성경말씀 미션 화면 색 테마(0074).</summary>
    public string? MissionTheme { get; set; }
}

// B. 가입 신청 · 역할 -----------------------------------------------------------

public static class SignupStatus
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
}

public static class SignupReqType
{
    public const string Join = "JOIN";
    public const string Change = "CHANGE";
}

/// <summary>신청한 역할: S 학생 · T 교사 · P 학부모 · E 기타(유년부 SignupApplication.RoleKind).</summary>
public static class SignupRoleKind
{
    public const string Student = "S";
    public const string Teacher = "T";
    public const string Parent = "P";
    public const string Etc = "E";
    public static readonly string[] All = [Student, Teacher, Parent, Etc];
}

public sealed class SignupApplication : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string UserName { get; set; }
    public required string FullName { get; set; }
    public required string RoleKind { get; set; }
    public int PersonId { get; set; }
    public int ClassYear { get; set; }
    public int ClassBan { get; set; }
    public int BMonth { get; set; }
    public int BDay { get; set; }
    public string? Sex { get; set; }
    public string? School { get; set; }
    public string? Phone { get; set; }
    public string? Kids { get; set; }
    public string ReqType { get; set; } = SignupReqType.Join;
    public string? PrevRole { get; set; }
    public string Status { get; set; } = SignupStatus.Pending;
    public string? RejectReason { get; set; }
    public DateTime InsertDT { get; set; }
    public DateTime? DecidedDT { get; set; }
    public string? DecidedBy { get; set; }
}

/// <summary>사람의 부서 소속과 역할. TenantId 가 곧 소속이다(사람당 한 줄, 앱이 지킨다). RoleCode 가 없으면 학부모.</summary>
public sealed class DeptRoleUserAssoc : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string? RoleCode { get; set; }
    public string? UserId { get; set; }
    public DateTime InsertDT { get; set; }
    public DateTime UpdateDT { get; set; }
}

public sealed class CodeData : ITenantOwned
{
    public const string RoleMajor = "T001";

    public int TenantId { get; set; }
    public int Id { get; set; }
    public string? MajorCD { get; set; }
    public string? MinorCD { get; set; }
    public string? MinorNM { get; set; }
    public DateTime InsertDT { get; set; }
    public DateTime UpdateDT { get; set; }
    public int? DisplayOrder { get; set; }
}

// C. 가족팀 · 친구팀 · 시상 -------------------------------------------------------

public static class TeamTypes
{
    public const string Family = "FAMILY";
    public const string Friend = "FRIEND";
}

public sealed class FamilyTeam : ITenantOwned
{
    public int TenantId { get; set; }
    public int FamilyTeamId { get; set; }
    public required string TeamName { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Memo { get; set; }
    public DateTime InsertDT { get; set; }
    public DateTime UpdateDT { get; set; }
    public string? AwardKey { get; set; }
    public string? CreatorUserName { get; set; }
    public string TeamType { get; set; } = TeamTypes.Family;
}

public sealed class FamilyMember : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int FamilyTeamId { get; set; }
    public required string UserName { get; set; }
    public byte RoleCode { get; set; }
    public int SortNo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime InsertDT { get; set; }
    public DateTime UpdateDT { get; set; }
    public byte RelationCode { get; set; } = 9;
    public string? RelationLabel { get; set; }
}

public sealed class FamilyRequest : ITenantOwned
{
    public const string Invite = "I";
    public const string Join = "R";

    public int TenantId { get; set; }
    public int Id { get; set; }
    public int FamilyTeamId { get; set; }
    public required string Kind { get; set; }
    public required string TargetUserName { get; set; }
    public required string FromUserName { get; set; }
    public required string RelationKey { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class FamilyNotice : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public required string UserName { get; set; }
    public int FamilyTeamId { get; set; }
    public required string TeamName { get; set; }
    public string Kind { get; set; } = "JOINED";
    public DateTime CreatedAt { get; set; }
}

public static class MilestoneTypes
{
    public const string Award = "AWARD";
    public const string Applause = "APPLAUSE";
}

public sealed class FamilyMilestoneConfig : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int Year { get; set; }
    public int PointThreshold { get; set; }
    public required string MilestoneType { get; set; }
    public required string DisplayTitle { get; set; }
    public string? DisplayEmoji { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class FamilyMilestoneHidden : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int MilestoneConfigId { get; set; }
    public int FamilyTeamId { get; set; }
    public DateTime HiddenAt { get; set; }
}

public sealed class TeamAward : ITenantOwned
{
    public int TenantId { get; set; }
    public int AwardId { get; set; }
    public required string Kind { get; set; }
    public required string AwardKey { get; set; }
    public int Threshold { get; set; }
    public DateTime AwardedAt { get; set; }
}

// D. 미션 기록 ------------------------------------------------------------------

public static class MissionItems
{
    public const string WordMe = "Word.Me";
    public const string WordParent = "Word.Parent";
    public const string PrayerMe = "Prayer.Me";
    public const string PrayerOthers = "Prayer.Others";
    public const string QuizCorrect = "Quiz.Correct";
    public const string Recite = "Recite";
}

/// <summary>미션 한 항목 = 한 줄, 그때의 점수 포함. 합계는 이 표를 더해 계산한다(콘텐츠 번호는 남기지 않는다).</summary>
public sealed class MissionActivity : ITenantOwned
{
    public int TenantId { get; set; }
    public long ActivityId { get; set; }
    public required string UserName { get; set; }
    public DateOnly MissionDate { get; set; }
    public required string MissionItem { get; set; }
    public int Points { get; set; }
    public DateOnly? ReciteWeekStart { get; set; }
    public DateTime PerformedAt { get; set; }
}

public sealed class QuizAttempt : ITenantOwned
{
    public int TenantId { get; set; }
    public required string UserName { get; set; }
    public DateOnly MissionDate { get; set; }
    public int QuestionId { get; set; }
    public byte AttemptNo { get; set; }
    public string? AnswerText { get; set; }
    public string? CorrectAnswerText { get; set; }
    public bool IsCorrect { get; set; }
    public DateTime AttemptedAt { get; set; }
}

// E. 통독 기록 (TenantId 없음, 사람에 붙는다) ---------------------------------------

public sealed class ReadingRound : IPersonOwned
{
    public required string UserName { get; set; }
    public int RoundNo { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public bool IsOpen => CompletedAt is null && ClosedAt is null;
}

public sealed class ReadingVerse : IPersonOwned
{
    public required string UserName { get; set; }
    public int RoundNo { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public DateTime ReadAt { get; set; }
}

public sealed class ReadingBookmark : IPersonOwned
{
    public required string UserName { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ReadingChapterRead : IPersonOwned
{
    public required string UserName { get; set; }
    public int RoundNo { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public DateTime ReadAt { get; set; }
}

public sealed class ReadingChapterProgress : IPersonOwned
{
    public required string UserName { get; set; }
    public int RoundNo { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public short LastViewedVerse { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// 표 설정 ------------------------------------------------------------------------

internal static class SharedConfig
{
    public const string Identity = "Identity";
    public const string Dbo = "dbo";

    public static PropertyBuilder<string> Ascii(this PropertyBuilder<string> p, int len) => p.HasMaxLength(len).IsUnicode(false);
}

internal sealed class AppUserConfig : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> e)
    {
        e.ToTable("User", SharedConfig.Identity);
        e.HasKey(x => x.Id).HasName("PK_User");
        e.Property(x => x.Id).HasMaxLength(450);
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.NormalizedUserName).HasMaxLength(256);
        e.Property(x => x.Email).HasMaxLength(256);
        e.Property(x => x.NormalizedEmail).HasMaxLength(256);
        e.Property(x => x.UsernameChangeLimit).HasDefaultValue(0);
        e.Property(x => x.InsertDT).HasColumnName("insertDT").HasColumnType("datetime");
        e.Ignore(x => x.DisplayName);
        e.HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex");
        e.HasIndex(x => x.UserName).HasDatabaseName("IX_IdentityUser_UserName");
        e.HasIndex(x => x.NormalizedUserName).IsUnique().HasFilter("[NormalizedUserName] IS NOT NULL").HasDatabaseName("UserNameIndex");
    }
}

internal sealed class AppRoleConfig : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> e)
    {
        e.ToTable("Role", SharedConfig.Identity);
        e.HasKey(x => x.Id).HasName("PK_Role");
        e.Property(x => x.Id).HasMaxLength(450);
        e.Property(x => x.Name).HasMaxLength(256);
        e.Property(x => x.NormalizedName).HasMaxLength(256);
        e.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("[NormalizedName] IS NOT NULL").HasDatabaseName("RoleNameIndex");
    }
}

internal sealed class AppUserRoleConfig : IEntityTypeConfiguration<AppUserRole>
{
    public void Configure(EntityTypeBuilder<AppUserRole> e)
    {
        e.ToTable("UserRoles", SharedConfig.Identity);
        e.HasKey(x => new { x.UserId, x.RoleId }).HasName("PK_UserRoles");
        e.Property(x => x.UserId).HasMaxLength(450);
        e.Property(x => x.RoleId).HasMaxLength(450);
        e.HasIndex(x => x.RoleId).HasDatabaseName("IX_UserRoles_RoleId");
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).HasConstraintName("FK_UserRoles_User_UserId");
        e.HasOne<AppRole>().WithMany().HasForeignKey(x => x.RoleId).HasConstraintName("FK_UserRoles_Role_RoleId");
    }
}

internal sealed class AppUserClaimConfig : IEntityTypeConfiguration<AppUserClaim>
{
    public void Configure(EntityTypeBuilder<AppUserClaim> e)
    {
        e.ToTable("UserClaims", SharedConfig.Identity);
        e.HasKey(x => x.Id).HasName("PK_UserClaims");
        e.Property(x => x.UserId).HasMaxLength(450);
        e.HasIndex(x => x.UserId).HasDatabaseName("IX_UserClaims_UserId");
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).HasConstraintName("FK_UserClaims_User_UserId");
    }
}

internal sealed class AppRoleClaimConfig : IEntityTypeConfiguration<AppRoleClaim>
{
    public void Configure(EntityTypeBuilder<AppRoleClaim> e)
    {
        e.ToTable("RoleClaims", SharedConfig.Identity);
        e.HasKey(x => x.Id).HasName("PK_RoleClaims");
        e.Property(x => x.RoleId).HasMaxLength(450);
        e.HasIndex(x => x.RoleId).HasDatabaseName("IX_RoleClaims_RoleId");
        e.HasOne<AppRole>().WithMany().HasForeignKey(x => x.RoleId).HasConstraintName("FK_RoleClaims_Role_RoleId");
    }
}

internal sealed class AppUserLoginConfig : IEntityTypeConfiguration<AppUserLogin>
{
    public void Configure(EntityTypeBuilder<AppUserLogin> e)
    {
        e.ToTable("UserLogins", SharedConfig.Identity);
        e.HasKey(x => new { x.LoginProvider, x.ProviderKey }).HasName("PK_UserLogins");
        e.Property(x => x.LoginProvider).HasMaxLength(450);
        e.Property(x => x.ProviderKey).HasMaxLength(450);
        e.Property(x => x.UserId).HasMaxLength(450);
        e.HasIndex(x => x.UserId).HasDatabaseName("IX_UserLogins_UserId");
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).HasConstraintName("FK_UserLogins_User_UserId");
    }
}

internal sealed class AppUserTokenConfig : IEntityTypeConfiguration<AppUserToken>
{
    public void Configure(EntityTypeBuilder<AppUserToken> e)
    {
        e.ToTable("UserTokens", SharedConfig.Identity);
        e.HasKey(x => new { x.UserId, x.LoginProvider, x.Name }).HasName("PK_UserTokens");
        e.Property(x => x.UserId).HasMaxLength(450);
        e.Property(x => x.LoginProvider).HasMaxLength(450);
        e.Property(x => x.Name).HasMaxLength(450);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).HasConstraintName("FK_UserTokens_User_UserId");
    }
}

internal sealed class UserSettingConfig : IEntityTypeConfiguration<UserSetting>
{
    public void Configure(EntityTypeBuilder<UserSetting> e)
    {
        e.ToTable("UserSetting");
        e.HasKey(x => x.UserName).HasName("PK_UserSetting");
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.ReadingTheme).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.MissionTheme).HasMaxLength(20).IsUnicode(false);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.LastTenantId).HasConstraintName("FK_UserSetting_LastTenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SignupApplicationConfig : IEntityTypeConfiguration<SignupApplication>
{
    public void Configure(EntityTypeBuilder<SignupApplication> e)
    {
        e.ToTable("SignupApplication", SharedConfig.Dbo);
        e.HasKey(x => new { x.TenantId, x.Id }).HasName("PK_SignupApplication");
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.UserId).HasMaxLength(450);
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.FullName).HasMaxLength(100);
        e.Property(x => x.RoleKind).HasColumnType("char(1)");
        e.Property(x => x.Sex).HasMaxLength(1);
        e.Property(x => x.School).HasMaxLength(200);
        e.Property(x => x.Phone).HasMaxLength(50);
        e.Property(x => x.Kids).HasMaxLength(1000);
        e.Property(x => x.ReqType).HasMaxLength(10);
        e.Property(x => x.PrevRole).HasMaxLength(100);
        e.Property(x => x.Status).HasMaxLength(10);
        e.Property(x => x.RejectReason).HasMaxLength(300);
        e.Property(x => x.InsertDT).HasColumnType("datetime");
        e.Property(x => x.DecidedDT).HasColumnType("datetime");
        e.Property(x => x.DecidedBy).HasMaxLength(256);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_SignupApplication_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class DeptRoleUserAssocConfig : IEntityTypeConfiguration<DeptRoleUserAssoc>
{
    public void Configure(EntityTypeBuilder<DeptRoleUserAssoc> e)
    {
        e.ToTable("DeptRoleUserAssoc", SharedConfig.Dbo);
        e.HasKey(x => new { x.TenantId, x.Id }).HasName("PK_DeptRoleUserAssoc");
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.RoleCode).HasMaxLength(50);
        e.Property(x => x.UserId).HasMaxLength(450);
        e.Property(x => x.InsertDT).HasColumnType("datetime");
        e.Property(x => x.UpdateDT).HasColumnName("updateDT").HasColumnType("datetime");
        e.HasIndex(x => x.UserId).HasDatabaseName("IX_DeptRoleUserAssoc_UserId");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_DeptRoleUserAssoc_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class CodeDataConfig : IEntityTypeConfiguration<CodeData>
{
    public void Configure(EntityTypeBuilder<CodeData> e)
    {
        e.ToTable("CodeData", SharedConfig.Dbo);
        e.HasKey(x => new { x.TenantId, x.Id }).HasName("PK_CodeData");
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.MajorCD).HasMaxLength(50);
        e.Property(x => x.MinorCD).HasMaxLength(50);
        e.Property(x => x.MinorNM).HasMaxLength(250);
        e.Property(x => x.InsertDT).HasColumnType("datetime");
        e.Property(x => x.UpdateDT).HasColumnName("updateDT").HasColumnType("datetime");
        e.Property(x => x.DisplayOrder).HasColumnName("displayOrder");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_CodeData_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyTeamConfig : IEntityTypeConfiguration<FamilyTeam>
{
    public void Configure(EntityTypeBuilder<FamilyTeam> e)
    {
        e.ToTable("DM2_FamilyTeam", SharedConfig.Dbo, t => t.HasCheckConstraint("CK_DM2_FamilyTeam_TeamType", "[TeamType]='FRIEND' OR [TeamType]='FAMILY'"));
        e.HasKey(x => new { x.TenantId, x.FamilyTeamId }).HasName("PK_DM2_FamilyTeam");
        e.Property(x => x.FamilyTeamId).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.TeamName).HasMaxLength(50);
        e.Property(x => x.IsActive).HasDefaultValue(true);
        e.Property(x => x.Memo).HasMaxLength(200);
        e.Property(x => x.InsertDT).HasColumnType("datetime");
        e.Property(x => x.UpdateDT).HasColumnType("datetime");
        e.Property(x => x.AwardKey).HasMaxLength(100);
        e.Property(x => x.CreatorUserName).HasMaxLength(200);
        e.Property(x => x.TeamType).Ascii(10).HasDefaultValue(TeamTypes.Family);
        e.HasIndex(x => new { x.TenantId, x.IsActive }).HasDatabaseName("IX_DM2_FamilyTeam_IsActive");
        e.HasIndex(x => new { x.TenantId, x.AwardKey }).IsUnique().HasFilter("[AwardKey] IS NOT NULL").HasDatabaseName("UX_DM2_FamilyTeam_AwardKey");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_DM2_FamilyTeam_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyMemberConfig : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> e)
    {
        e.ToTable("DM2_FamilyMember", SharedConfig.Dbo);
        e.HasKey(x => new { x.TenantId, x.Id }).HasName("PK_DM2_FamilyMember");
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.UserName).HasMaxLength(100);
        e.Property(x => x.SortNo).HasDefaultValue(0);
        e.Property(x => x.IsActive).HasDefaultValue(true);
        e.Property(x => x.InsertDT).HasColumnType("datetime");
        e.Property(x => x.UpdateDT).HasColumnType("datetime");
        e.Property(x => x.RelationCode).HasDefaultValue((byte)9);
        e.Property(x => x.RelationLabel).HasMaxLength(20);
        e.HasIndex(x => new { x.TenantId, x.FamilyTeamId, x.IsActive }).HasDatabaseName("IX_DM2_FamilyMember_FamilyTeamId_IsActive");
        e.HasIndex(x => new { x.TenantId, x.FamilyTeamId, x.RoleCode, x.IsActive }).HasDatabaseName("IX_DM2_FamilyMember_Team");
        e.HasIndex(x => new { x.TenantId, x.UserName, x.IsActive }).HasDatabaseName("IX_DM2_FamilyMember_UserName_IsActive");
        e.HasIndex(x => new { x.TenantId, x.FamilyTeamId, x.RelationCode }).IsUnique()
            .HasFilter("[IsActive]=(1) AND [RoleCode]=(0) AND [RelationCode]=(0)").HasDatabaseName("UX_DM2_FamilyMember_Team_Self");
        e.HasIndex(x => new { x.TenantId, x.FamilyTeamId, x.UserName }).IsUnique().HasDatabaseName("UX_DM2_FamilyMember_Team_User");
        e.HasIndex(x => x.UserName).IsUnique().HasDatabaseName("UX_DM2_FamilyMember_UserName");
        e.HasOne<FamilyTeam>().WithMany().HasForeignKey(x => new { x.TenantId, x.FamilyTeamId }).HasConstraintName("FK_DM2_FamilyMember_Team").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyRequestConfig : IEntityTypeConfiguration<FamilyRequest>
{
    public void Configure(EntityTypeBuilder<FamilyRequest> e)
    {
        e.ToTable("DM2_FamilyRequest", SharedConfig.Dbo, t => t.HasCheckConstraint("CK_DM2_FamilyRequest_Kind", "[Kind]='I' OR [Kind]='R'"));
        e.HasKey(x => new { x.TenantId, x.Id }).HasName("PK_DM2_FamilyRequest");
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Kind).HasColumnType("char(1)");
        e.Property(x => x.TargetUserName).HasMaxLength(200);
        e.Property(x => x.FromUserName).HasMaxLength(200);
        e.Property(x => x.RelationKey).Ascii(10);
        e.Property(x => x.CreatedAt).HasColumnType("datetime");
        e.HasIndex(x => new { x.TenantId, x.FromUserName, x.Kind }).HasDatabaseName("IX_DM2_FamilyRequest_From");
        e.HasIndex(x => new { x.TenantId, x.TargetUserName, x.Kind }).HasDatabaseName("IX_DM2_FamilyRequest_Target");
        e.HasIndex(x => new { x.TenantId, x.FamilyTeamId, x.Kind, x.TargetUserName }).IsUnique().HasDatabaseName("UX_DM2_FamilyRequest_Team_Kind_Target");
        e.HasOne<FamilyTeam>().WithMany().HasForeignKey(x => new { x.TenantId, x.FamilyTeamId }).HasConstraintName("FK_DM2_FamilyRequest_Team").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyNoticeConfig : IEntityTypeConfiguration<FamilyNotice>
{
    public void Configure(EntityTypeBuilder<FamilyNotice> e)
    {
        e.ToTable("DM2_FamilyNotice", SharedConfig.Dbo);
        e.HasKey(x => new { x.TenantId, x.Id }).HasName("PK_DM2_FamilyNotice");
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.UserName).HasMaxLength(200);
        e.Property(x => x.TeamName).HasMaxLength(100);
        e.Property(x => x.Kind).Ascii(10).HasDefaultValue("JOINED");
        e.Property(x => x.CreatedAt).HasColumnType("datetime");
        e.HasIndex(x => new { x.TenantId, x.UserName }).HasDatabaseName("IX_DM2_FamilyNotice_User");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_DM2_FamilyNotice_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyMilestoneConfigConfig : IEntityTypeConfiguration<FamilyMilestoneConfig>
{
    public void Configure(EntityTypeBuilder<FamilyMilestoneConfig> e)
    {
        e.ToTable("DM2_FamilyMilestoneConfig", SharedConfig.Dbo, t => t.HasCheckConstraint("CK_DM2FMC_Type", "[MilestoneType]='APPLAUSE' OR [MilestoneType]='AWARD'"));
        e.HasKey(x => new { x.TenantId, x.Id }).HasName("PK_DM2_FamilyMilestoneConfig");
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.MilestoneType).HasMaxLength(20);
        e.Property(x => x.DisplayTitle).HasMaxLength(200);
        e.Property(x => x.DisplayEmoji).HasMaxLength(10);
        e.Property(x => x.IsActive).HasDefaultValue(true);
        e.Property(x => x.SortOrder).HasDefaultValue(0);
        e.Property(x => x.CreatedAt).HasColumnType("datetime2(7)");
        e.HasIndex(x => new { x.TenantId, x.Year, x.IsActive }).HasDatabaseName("IX_DM2_FamilyMilestoneConfig_Year_IsActive");
        e.HasIndex(x => new { x.TenantId, x.Year, x.PointThreshold }).IsUnique().HasDatabaseName("UQ_DM2FMC_YearPoint");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_DM2_FamilyMilestoneConfig_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyMilestoneHiddenConfig : IEntityTypeConfiguration<FamilyMilestoneHidden>
{
    public void Configure(EntityTypeBuilder<FamilyMilestoneHidden> e)
    {
        e.ToTable("DM2_FamilyMilestoneHidden", SharedConfig.Dbo);
        e.HasKey(x => new { x.TenantId, x.Id }).HasName("PK_DM2_FamilyMilestoneHidden");
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.HiddenAt).HasColumnType("datetime2(7)");
        e.HasIndex(x => new { x.TenantId, x.MilestoneConfigId }).HasDatabaseName("IX_DM2FMH_ConfigId");
        e.HasIndex(x => new { x.TenantId, x.MilestoneConfigId, x.FamilyTeamId }).IsUnique().HasDatabaseName("UQ_DM2FMH_ConfigTeam");
        e.HasOne<FamilyMilestoneConfig>().WithMany().HasForeignKey(x => new { x.TenantId, x.MilestoneConfigId }).HasConstraintName("FK_DM2FMH_Config").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TeamAwardConfig : IEntityTypeConfiguration<TeamAward>
{
    public void Configure(EntityTypeBuilder<TeamAward> e)
    {
        e.ToTable("DM2_TeamAward", SharedConfig.Dbo, t => t.HasCheckConstraint("CK_DM2_TeamAward_Kind", "[Kind]=N'FRIEND' OR [Kind]=N'FAMILY'"));
        e.HasKey(x => new { x.TenantId, x.AwardId }).HasName("PK_DM2_TeamAward");
        e.Property(x => x.AwardId).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Kind).HasMaxLength(10);
        e.Property(x => x.AwardKey).HasMaxLength(200);
        e.Property(x => x.AwardedAt).HasColumnType("datetime");
        e.HasIndex(x => new { x.TenantId, x.AwardKey }).HasDatabaseName("IX_DM2_TeamAward_Key");
        e.HasIndex(x => new { x.TenantId, x.Kind, x.AwardKey, x.Threshold }).IsUnique().HasDatabaseName("UQ_DM2_TeamAward");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_DM2_TeamAward_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class MissionActivityConfig : IEntityTypeConfiguration<MissionActivity>
{
    public void Configure(EntityTypeBuilder<MissionActivity> e)
    {
        e.ToTable("DM2_MissionActivity", SharedConfig.Dbo, t =>
        {
            t.HasCheckConstraint("CK_DM2_MissionActivity_Item", "[MissionItem]='Recite' OR [MissionItem]='Quiz.Correct' OR [MissionItem]='Prayer.Others' OR [MissionItem]='Prayer.Me' OR [MissionItem]='Word.Parent' OR [MissionItem]='Word.Me'");
            t.HasCheckConstraint("CK_DM2_MissionActivity_Points", "[Points]>=(0)");
            t.HasCheckConstraint("CK_DM2_MissionActivity_Week", "[MissionItem]='Recite' AND [ReciteWeekStart] IS NOT NULL OR [MissionItem]<>'Recite' AND [ReciteWeekStart] IS NULL");
        });
        e.HasKey(x => new { x.TenantId, x.ActivityId }).HasName("PK_DM2_MissionActivity");
        e.Property(x => x.ActivityId).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.MissionItem).Ascii(20);
        e.Property(x => x.PerformedAt).HasColumnType("datetime2(3)");
        e.HasIndex(x => new { x.TenantId, x.MissionDate }).HasDatabaseName("IX_DM2_MissionActivity_Date");
        e.HasIndex(x => new { x.TenantId, x.UserName, x.MissionDate, x.MissionItem }).IsUnique().HasFilter("[MissionItem]<>'Recite'").HasDatabaseName("UX_DM2_MissionActivity_Daily");
        e.HasIndex(x => new { x.TenantId, x.UserName, x.ReciteWeekStart }).IsUnique().HasFilter("[MissionItem]='Recite'").HasDatabaseName("UX_DM2_MissionActivity_Recite");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_DM2_MissionActivity_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class QuizAttemptConfig : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> e)
    {
        e.ToTable("DM2_QuizAttempt", SharedConfig.Dbo, t => t.HasCheckConstraint("CK_DM2_QuizAttempt_No", "[AttemptNo]>=(1)"));
        e.HasKey(x => new { x.TenantId, x.UserName, x.MissionDate, x.QuestionId, x.AttemptNo }).HasName("PK_DM2_QuizAttempt");
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.AnswerText).HasMaxLength(400);
        e.Property(x => x.CorrectAnswerText).HasMaxLength(400);
        e.Property(x => x.AttemptedAt).HasColumnType("datetime2(3)");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_DM2_QuizAttempt_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ReadingRoundConfig : IEntityTypeConfiguration<ReadingRound>
{
    public void Configure(EntityTypeBuilder<ReadingRound> e)
    {
        e.ToTable("DM2_ReadingRound", SharedConfig.Dbo, t =>
        {
            t.HasCheckConstraint("CK_DM2_ReadingRound_End", "[CompletedAt] IS NULL OR [ClosedAt] IS NULL");
            t.HasCheckConstraint("CK_DM2_ReadingRound_No", "[RoundNo]>=(1)");
        });
        e.HasKey(x => new { x.UserName, x.RoundNo }).HasName("PK_DM2_ReadingRound");
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.StartedAt).HasColumnType("datetime2(3)");
        e.Property(x => x.CompletedAt).HasColumnType("datetime2(3)");
        e.Property(x => x.ClosedAt).HasColumnType("datetime2(3)");
        e.Ignore(x => x.IsOpen);
        e.HasIndex(x => x.UserName).IsUnique().HasFilter("[CompletedAt] IS NULL AND [ClosedAt] IS NULL").HasDatabaseName("UX_DM2_ReadingRound_Open");
    }
}

internal sealed class ReadingVerseConfig : IEntityTypeConfiguration<ReadingVerse>
{
    public void Configure(EntityTypeBuilder<ReadingVerse> e)
    {
        e.ToTable("DM2_ReadingVerse", SharedConfig.Dbo);
        e.HasKey(x => new { x.UserName, x.RoundNo, x.BookId, x.Chapter, x.Verse }).HasName("PK_DM2_ReadingVerse");
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.ReadAt).HasColumnType("datetime2(3)");
        e.HasIndex(x => x.ReadAt).HasDatabaseName("IX_DM2_ReadingVerse_ReadAt");
        e.HasOne<ReadingRound>().WithMany().HasForeignKey(x => new { x.UserName, x.RoundNo }).HasConstraintName("FK_DM2_ReadingVerse_Round").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ReadingBookmarkConfig : IEntityTypeConfiguration<ReadingBookmark>
{
    public void Configure(EntityTypeBuilder<ReadingBookmark> e)
    {
        e.ToTable("DM2_ReadingBookmark", SharedConfig.Dbo);
        e.HasKey(x => new { x.UserName, x.BookId, x.Chapter, x.Verse }).HasName("PK_DM2_ReadingBookmark");
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");
    }
}

internal sealed class ReadingChapterReadConfig : IEntityTypeConfiguration<ReadingChapterRead>
{
    public void Configure(EntityTypeBuilder<ReadingChapterRead> e)
    {
        e.ToTable("DM2_ReadingChapterRead", SharedConfig.Dbo);
        e.HasKey(x => new { x.UserName, x.RoundNo, x.BookId, x.Chapter }).HasName("PK_DM2_ReadingChapterRead");
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.ReadAt).HasColumnType("datetime2(3)");
        e.HasIndex(x => new { x.UserName, x.ReadAt }).HasDatabaseName("IX_DM2_ReadingChapterRead_ReadAt");
        e.HasOne<ReadingRound>().WithMany().HasForeignKey(x => new { x.UserName, x.RoundNo }).HasConstraintName("FK_DM2_ReadingChapterRead_Round").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ReadingChapterProgressConfig : IEntityTypeConfiguration<ReadingChapterProgress>
{
    public void Configure(EntityTypeBuilder<ReadingChapterProgress> e)
    {
        e.ToTable("DM2_ReadingChapterProgress", SharedConfig.Dbo, t => t.HasCheckConstraint("CK_DM2_ReadingChapterProgress_Verse", "[LastViewedVerse]>=(1)"));
        e.HasKey(x => new { x.UserName, x.RoundNo, x.BookId, x.Chapter }).HasName("PK_DM2_ReadingChapterProgress");
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)");
        e.HasIndex(x => new { x.UserName, x.UpdatedAt }).IsDescending(false, true).HasDatabaseName("IX_DM2_ReadingChapterProgress_Recent");
        e.HasOne<ReadingRound>().WithMany().HasForeignKey(x => new { x.UserName, x.RoundNo }).HasConstraintName("FK_DM2_ReadingChapterProgress_Round").OnDelete(DeleteBehavior.NoAction);
    }
}
