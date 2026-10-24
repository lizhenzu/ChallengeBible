using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Api;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 접속 문자열은 Git 에 두지 않는다(X-21): 개발은 user-secrets, 운영은 호스팅 환경 설정.
var connectionString = builder.Configuration.GetConnectionString("App") ?? "";

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TenantClock>();
builder.Services.AddScoped<RequestScope>();
builder.Services.AddScoped<IAppDb>(sp => new RequestDbContextFactory(connectionString, sp.GetRequiredService<RequestScope>()));
builder.Services.AddSingleton<ISystemDb>(new SystemDbContextFactory(connectionString));
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<JoinService>();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<TenantPageFilter>();
builder.Services.AddScoped<MemberAdminService>();
builder.Services.AddScoped<PlatformTenantService>();
builder.Services.AddScoped<MissionService>();
builder.Services.AddScoped<ContentAdminService>();
builder.Services.AddScoped<BibleImportService>();
builder.Services.AddScoped<BibleTextService>();
builder.Services.AddScoped<AdminHomeService>();
builder.Services.AddScoped<BibleMarkService>();
builder.Services.AddScoped<ReadingService>();
builder.Services.AddScoped<BibleSearchService>();
builder.Services.AddScoped<ReadingBoardService>();
builder.Services.AddScoped<BrandingService>();
builder.Services.AddScoped<SeasonService>();
builder.Services.AddScoped<ContentCalendarService>();
builder.Services.AddScoped<PlatformHomeService>();
builder.Services.AddScoped<PlatformAccountService>();
builder.Services.AddScoped<RuleService>();
builder.Services.AddScoped<PlatformRuleService>();
builder.Services.AddScoped<MemberDetailService>();
builder.Services.AddScoped<TenantContentService>();
builder.Services.AddScoped<RankingService>();
builder.Services.AddScoped<HomeService>();
builder.Services.AddScoped<LandingService>();
builder.Services.AddScoped<FamilyTeamService>();
builder.Services.AddScoped<FamilyTenantService>();
builder.Services.AddScoped<MyAccountService>();
builder.Services.AddSingleton<ErrorLog>();
builder.Services.AddSingleton<AppInfo>();
builder.Services.AddScoped<OpsService>();
// 해설 표(dbo.eBibleSummary)는 읽기만 한다(ADR 0056): 읽기 전용 계정(BibleNote)이 있으면 그것으로 접속한다.
var bibleNoteConnection = builder.Configuration.GetConnectionString("BibleNote") ?? connectionString;
builder.Services.AddSingleton(sp => new BibleNoteService(bibleNoteConnection, sp.GetRequiredService<ILogger<BibleNoteService>>()));
builder.Services.AddScoped<GuardianService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<PromotionApprover>();
builder.Services.AddScoped<TenantResolver>();
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LookupLimiter>();

// 쿠키를 푸는 키를 DB 에 둔다(ADR 0055): 게시 · 앱 재시작 뒤에도 로그인이 유지된다.
// 개발(테스트 포함)은 기본값(사용자 프로필 폴더)을 쓴다.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("ChallengeBible");
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddDbContext<KeyStoreDbContext>(o => o.UseSqlServer(connectionString));
    dataProtection.PersistKeysToDbContext<KeyStoreDbContext>();
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "cb.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/account/login";
        o.LogoutPath = "/account/logout";
        o.AccessDeniedPath = "/account/denied";
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o =>
    o.AddPolicy(CurrentUser.PlatformAdminPolicy, p => p.RequireClaim(CurrentUser.PlatformAdminClaim, "1")));
// 한글을 &#x...; 로 바꾸지 않고 그대로 쓴다(화면 크기 · 검색).  < > & " 등은 그대로 이스케이프된다.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddRazorPages(o =>
{
    o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(TenantPageFilter)));
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToPage("/Index");
    o.Conventions.AllowAnonymousToPage("/Account/Login");
    o.Conventions.AllowAnonymousToPage("/Account/Signup");
    o.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseErrorLog();   // 운영 점검의 최근 오류(서버 메모리, 개인 내용 없음) — 오류 화면보다 안쪽에서 남기고 다시 던진다
app.Services.GetRequiredService<AppInfo>();   // 가동 시간은 앱이 켜진 때부터

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseRequestScope();
app.UsePasswordChangeGate();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapMissionApi();
app.MapBibleApi();
app.MapReadingApi();
app.MapBrandingApi();

app.Run();

public partial class Program;
