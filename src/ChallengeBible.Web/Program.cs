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
builder.Services.AddScoped<PeopleService>();
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<ClassMoveService>();
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
builder.Services.AddScoped<MilestoneService>();
builder.Services.AddScoped<PhotoService>();
builder.Services.AddScoped<ContentCalendarService>();
builder.Services.AddScoped<PlatformHomeService>();
builder.Services.AddScoped<PlatformAccountService>();
builder.Services.AddScoped<RuleService>();
builder.Services.AddScoped<PlatformRuleService>();
builder.Services.AddScoped<MemberDetailService>();
builder.Services.AddScoped<TenantContentService>();
builder.Services.AddScoped<RankingService>();
builder.Services.AddScoped<RosterService>();
builder.Services.AddScoped<HomeService>();
builder.Services.AddScoped<LandingService>();
builder.Services.AddScoped<LobbyService>();
builder.Services.AddScoped<BookOverviewService>();
builder.Services.AddScoped<ChapterMediaService>();
builder.Services.AddScoped<FamilyTeamService>();
builder.Services.AddScoped<FamilyStageService>();
builder.Services.AddScoped<FamilyAwardService>();
builder.Services.AddScoped<FamilyJoinService>();
builder.Services.AddScoped<ImpersonationService>();
builder.Services.AddScoped<UserSwitchService>();   // 사람 바꾸기(ADR 0096)
builder.Services.AddScoped<MyAccountService>();
builder.Services.AddSingleton<ErrorLog>();
builder.Services.AddSingleton<AppInfo>();
builder.Services.AddScoped<OpsService>();
builder.Services.AddScoped<GiftCatalogService>();
builder.Services.AddScoped<DeptGiftService>();   // 부서 선물 설정(ADR 0094)   // 선물 상품 관리(ADR 0094 A)
builder.Services.AddScoped<PersonalGiftService>();   // 개인 선물 고르기(ADR 0094 B)
builder.Services.AddScoped<TeamGiftService>();   // 가족팀 · 친구팀 선물(ADR 0094 F)
builder.Services.AddScoped<GiftOrderService>();
builder.Services.AddScoped<YearResetService>();   // 새해 초기화 버튼(ADR 0094 정한 것 12)   // 선물 구매 · 전달 처리 · 부서 카드(ADR 0094 G)
builder.Services.AddScoped<TestToolsService>();   // 임시 시험 도구(ADR 0095), 설정 TestTools:Enabled
// 해설 표(dbo.eBibleSummary)는 읽기만 한다(ADR 0056): 읽기 전용 계정(BibleNote)이 있으면 그것으로 접속한다.
var bibleNoteConnection = builder.Configuration.GetConnectionString("BibleNote") ?? connectionString;
builder.Services.AddSingleton(sp => new BibleNoteService(bibleNoteConnection, sp.GetRequiredService<ILogger<BibleNoteService>>()));
builder.Services.AddScoped<GuardianService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<PromotionApprover>();
builder.Services.AddScoped<TenantResolver>();
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ChallengeBible.Web.Auth.UserNameGate>();
builder.Services.AddSingleton<LookupLimiter>();
builder.Services.AddSingleton<AdminLoginLimiter>();

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
    o.Conventions.AllowAnonymousToPage("/Account/AdminLogin");
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
// 얼굴 사진 폴더(wwwroot/profile, 0078)는 주소로 바로 열지 않는다 — 권한을 보는 /{slug}/photo/{계정} 으로만(R-201)
app.Use((ctx, next) =>
{
    if (!ctx.Request.Path.StartsWithSegments("/profile")) return next();
    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
});
// 선물 이미지 등 나중에 서버에 넣는 그림(wwwroot/images, 설정 `Images:Dir`): MapStaticAssets 는 게시 때 함께 올라간 파일만
// 내보내므로 FTP · 관리 화면으로 넣은 파일(/images/AwardItem/{상품번호}.jpg)은 이것으로 내보낸다(R-274). /images 아래만 연다.
{
    var imagesDir = app.Configuration["Images:Dir"] is { Length: > 0 } d ? d
        : Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "images");
    Directory.CreateDirectory(imagesDir);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(imagesDir),
        RequestPath = "/images",
    });
}
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
app.MapContentApi();

app.Run();

public partial class Program;
