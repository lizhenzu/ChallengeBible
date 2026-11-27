using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>선물함(2026-11-27 민트 시안): [신청한 선물 / 내가 받은 선물] · 요약 3칸 · 점수는 개인 선물만, 상태 글자(신청 완료 · 준비 중 · 받음).</summary>
public partial class GiftBoxTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly string _pid;

    public GiftBoxTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());   // 2027-01-05
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var kid = InMemoryDb.SeedAccount(_db, "kid@example.com", "김하늘");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        _pid = m.UserName;
        var teacher = InMemoryDb.SeedAccount(_db, "teacher@example.com", "박선생");
        InMemoryDb.SeedMembership(_db, _kids.Id, teacher.UserName!, MembershipStatus.Approved, Roles.Teacher);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        var now = DateTime.UtcNow;
        raw.Gifts.AddRange(
            new Gift { TenantId = _kids.Id, Kind = GiftKinds.Personal, Year = 2027, UserName = _pid, SourceKey = "P1", State = GiftStates.Picked, ItemName = "색연필 세트", PointsUsed = 300, PickedUtc = now, CreatedUtc = now },
            new Gift { TenantId = _kids.Id, Kind = GiftKinds.Personal, Year = 2027, UserName = _pid, SourceKey = "P2", State = GiftStates.Purchased, ItemName = "스티커북", PointsUsed = 200, PickedUtc = now, PurchasedUtc = now, CreatedUtc = now },
            new Gift { TenantId = _kids.Id, Kind = GiftKinds.Personal, Year = 2027, UserName = _pid, SourceKey = "P0", State = GiftStates.Delivered, ItemName = "연필", PointsUsed = 100, PickedUtc = now, DeliveredUtc = now, CreatedUtc = now },
            new Gift { TenantId = _kids.Id, Kind = GiftKinds.Birthday, Year = 2027, UserName = _pid, SourceKey = "B2027", State = GiftStates.Picked, ItemName = "생일 인형", PickedUtc = now, CreatedUtc = now },
            new Gift { TenantId = _kids.Id, Kind = GiftKinds.Personal, Year = 2027, UserName = _pid, SourceKey = "P9", State = GiftStates.Cancelled, ItemName = "취소한 선물", PointsUsed = 50, PickedUtc = now, CreatedUtc = now });
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string login)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = login + "@example.com", ["Password"] = "1234" }));
        return c;
    }

    [Fact]
    public async Task 선물함은_신청한_선물과_받은_선물을_나눠_보이고_점수는_개인_선물만_보인다()
    {
        var c = await Login("kid");
        var req = await c.GetStringAsync("/kids/gift-box");
        Assert.Contains("<span>신청한 선물</span><b class=\"num\">3<small>개</small></b>", req);   // 신청 완료 · 준비 중 · 생일 = 3
        Assert.Contains("<span>받은 선물</span><b class=\"num\">1<small>개</small></b>", req);
        Assert.Contains("<span>사용한 점수</span><b class=\"num\">600<small>점</small></b>", req);   // 개인 선물 300 + 200 + 100
        Assert.Contains("색연필 세트", req);
        Assert.Contains("<i class=\"req\">신청 완료</i>", req);
        Assert.Contains("스티커북", req);
        Assert.Contains("<i class=\"prep\">준비 중</i>", req);
        Assert.Contains("<b class=\"num\">300점</b>", req);                  // 개인 선물은 점수
        Assert.Contains("<b>생일 선물</b>", req);                              // 생일 선물은 종류만(금액 숨김)
        Assert.DoesNotContain("연필", req.Replace("색연필 세트", ""));         // 받은 선물은 다른 탭
        Assert.DoesNotContain("취소한 선물", req);

        var got = await c.GetStringAsync("/kids/gift-box?tab=got");
        Assert.Contains("연필", got);
        Assert.Contains("<i class=\"got\">받음</i>", got);
        Assert.DoesNotContain("색연필 세트", got);
    }

    [Fact]
    public async Task 남의_선물은_선물함에_없다()
    {
        var c = await Login("teacher");   // 선물은 내 것만 — 교사의 선물함은 비어 있다
        var html = await c.GetStringAsync("/kids/gift-box");
        Assert.Contains("<span>신청한 선물</span><b class=\"num\">0<small>개</small></b>", html);
        Assert.DoesNotContain("색연필 세트", html);
    }
}
