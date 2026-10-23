using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

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
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LookupLimiter>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "cb.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/account/login";
        o.LogoutPath = "/account/logout";
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
// 한글을 &#x...; 로 바꾸지 않고 그대로 쓴다(화면 크기 · 검색).  < > & " 등은 그대로 이스케이프된다.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddRazorPages(o =>
{
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

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseRequestScope();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
