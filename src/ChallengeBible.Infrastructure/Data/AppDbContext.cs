using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 일반 요청용 DbContext. 영역 범위 표에는 전역 필터(TenantId = 이 요청의 영역)를,
/// 공용 콘텐츠 표에는 (공용 또는 이 요청의 영역)을 건다.
/// DB 의 RLS 와 같은 조건을 앱에서 한 번 더 거는 것이다(0020 이중 격리).
/// 영역을 넘는 일은 <see cref="SystemDbContextFactory"/> 로만 한다(물리 설계 3-3).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, int? tenantId) : DbContext(options)
{
    /// <summary>필터 기준 영역. 시스템 연결에서는 null 이고 필터를 끈다.</summary>
    public int? CurrentTenantId { get; } = tenantId;
    public bool FilterTenants { get; init; } = true;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<TenantSeason> TenantSeasons => Set<TenantSeason>();

    public DbSet<BibleBook> BibleBooks => Set<BibleBook>();
    public DbSet<DailyWord> DailyWords => Set<DailyWord>();
    public DbSet<DailyPrayer> DailyPrayers => Set<DailyPrayer>();
    public DbSet<DailyQuiz> DailyQuizzes => Set<DailyQuiz>();
    public DbSet<DailyQuizChoice> DailyQuizChoices => Set<DailyQuizChoice>();
    public DbSet<DailyQuizAcceptedAnswer> DailyQuizAcceptedAnswers => Set<DailyQuizAcceptedAnswer>();
    public DbSet<WeeklyVerse> WeeklyVerses => Set<WeeklyVerse>();

    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<PointLedger> PointLedgers => Set<PointLedger>();
    public DbSet<MilestoneReached> MilestonesReached => Set<MilestoneReached>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("app");
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var type in b.Model.GetEntityTypes().Select(t => t.ClrType).ToList())
        {
            var name = typeof(ITenantOwned).IsAssignableFrom(type) ? nameof(TenantFilter)
                     : typeof(ISharedContent).IsAssignableFrom(type) ? nameof(SharedFilter)
                     : null;
            if (name is null) continue;
            var method = typeof(AppDbContext).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(type);
            b.Entity(type).HasQueryFilter((LambdaExpression)method.Invoke(this, null)!);
        }
    }

    private Expression<Func<T, bool>> TenantFilter<T>() where T : ITenantOwned =>
        e => !FilterTenants || e.TenantId == CurrentTenantId;

    private Expression<Func<T, bool>> SharedFilter<T>() where T : ISharedContent =>
        e => !FilterTenants || e.TenantId == null || e.TenantId == CurrentTenantId;
}
