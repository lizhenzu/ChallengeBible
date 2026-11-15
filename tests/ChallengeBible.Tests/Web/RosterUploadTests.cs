using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 명단 올리기(ADR 0097, R-301 ~ R-304): 파일 이름 = 부서, 학생 · 교사 탭, 그 부서 명단 · 반 배정을 비우고 넣기(계정 있는 사람 · 관리자는 그대로), 틀린 줄만 빼기. 플랫폼 관리자만.
/// </summary>
public partial class RosterUploadTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;
    private readonly int _c1, _c2, _oldPid, _adminPid;
    private readonly string _adminUser;

    public RosterUploadTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        _adminUser = InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin).UserName;

        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        var term = new ClassTerm { TenantId = _kids.Id, Year = 2026, StartDate = new DateOnly(2026, 1, 1), CreatedUtc = DateTime.UtcNow };
        raw.ClassTerms.Add(term);
        raw.SaveChanges();
        var c1 = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1반", CreatedUtc = DateTime.UtcNow };
        var c2 = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "2반", CreatedUtc = DateTime.UtcNow };
        raw.Classes.AddRange(c1, c2, new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = ClassService.NewcomerName, IsNewcomer = true, CreatedUtc = DateTime.UtcNow });
        var pAdmin = raw.People.FirstOrDefault(p => p.UserId == adm.Id) ?? new Person { TenantId = _kids.Id, Name = "윤동호", UserId = adm.Id, RoleCode = "T1002", CreatedUtc = DateTime.UtcNow };
        if (pAdmin.Id == 0) raw.People.Add(pAdmin);
        var old = new Person { TenantId = _kids.Id, Name = "옛날아이", RoleCode = "T1001", NewcomerFirstVisit = new DateOnly(2026, 10, 4), CreatedUtc = DateTime.UtcNow };
        raw.People.Add(old);
        var elemKid = new Person { TenantId = _elem.Id, Name = "다른부서아이", RoleCode = "T1001", CreatedUtc = DateTime.UtcNow };
        raw.People.Add(elemKid);
        raw.SaveChanges();
        raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c2.Id, PersonId = old.Id, CreatedUtc = DateTime.UtcNow });
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = old.Id, Sunday = new DateOnly(2026, 11, 8), CreatedUtc = DateTime.UtcNow });
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _elem.Id, PersonId = elemKid.Id, Sunday = new DateOnly(2026, 11, 8), CreatedUtc = DateTime.UtcNow });
        raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Newcomer, State = GiftStates.Open, Year = 2026, PersonId = old.Id, SourceKey = "N1", MaxAmount = 10000, CreatedUtc = DateTime.UtcNow });
        raw.SaveChanges();
        (_c1, _c2, _oldPid, _adminPid) = (c1.Id, c2.Id, old.Id, pAdmin.Id);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();
    [GeneratedRegex("name=\"plan\" value=\"([^\"]+)\"")]
    private static partial Regex PlanRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private static byte[] Book(string[][] students, string[][] teachers)
    {
        using var wb = new XLWorkbook();
        void Fill(IXLWorksheet ws, string[] head, string[][] rows)
        {
            ws.Cell(1, 1).Value = "안내 글";
            for (var i = 0; i < head.Length; i++) ws.Cell(2, i + 1).Value = head[i];
            for (var r = 0; r < rows.Length; r++) for (var i = 0; i < rows[r].Length; i++) ws.Cell(3 + r, i + 1).Value = rows[r][i];
        }
        Fill(wb.AddWorksheet("학생"), ["반", "이름", "생일\n(월/일)", "전화번호", "보호자 전화번호\n(선택)"], students);
        Fill(wb.AddWorksheet("교사"), ["이름", "전화번호", "생일\n(월/일, 선택)", "맡은 반\n(담임, 선택)", "임원 역할"], teachers);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static async Task<string> Preview(HttpClient c, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent { { new StringContent(TokenRx().Match(await c.GetStringAsync("/platform/roster-upload")).Groups[1].Value), "__RequestVerificationToken" } };
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return await (await c.PostAsync("/platform/roster-upload?handler=Preview", form)).Content.ReadAsStringAsync();
    }

    private static async Task<string> Apply(HttpClient c, string page, bool agree)
    {
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["plan"] = WebUtility.HtmlDecode(PlanRx().Match(page).Groups[1].Value) };
        if (agree) form["agree"] = "true";
        return await (await c.PostAsync("/platform/roster-upload?handler=Apply", new FormUrlEncodedContent(form))).Content.ReadAsStringAsync();
    }

    private static readonly string[][] Kids =
    [
        ["예) 1반", "홍길동", "3/15", "010-1234-5678", ""],
        ["1반", "김철수", "3/15", "01012345678", "010-9876-5432"],
        ["2반", "이영희", "12월 1일", "", ""],
        ["새친구반", "박새롬", "", "", ""],
        ["", "반없음", "", "", ""],
        ["9반", "틀린반", "", "", ""],
        ["1반", "틀린생일", "13/40", "", ""],
    ];
    private static readonly string[][] Staff =
    [
        ["김선생", "010-2345-6789", "5/20", "1반", ""],
        ["이부장", "010-3456-7890", "", "2반", "부장"],
        ["윤동호", "010-1111-2222", "", "2반", ""],
        ["", "", "", "", ""],
    ];

    [Fact]
    public async Task R301_파일_이름으로_부서를_정하고_틀린_줄만_빼고_미리_보여_준다()
    {
        var op = await Login("operator@example.com");
        var page = await Preview(op, "유년부.xlsx", Book(Kids, Staff));
        Assert.Contains("유년부.xlsx → 잠실새내교회 유년부", page);
        Assert.Contains("고칠 곳 2줄", page);
        Assert.Contains("학생 8번째 줄 · 반 &quot;9반&quot;이 유년부에 없어요", page);
        Assert.Contains("학생 9번째 줄 · 생일 &quot;13/40&quot;을 읽을 수 없어요", page);
        Assert.Contains("지금 명단 <b>1명</b>, 반 배정 <b>1건</b>, 그 사람들의 출석 <b>1건</b>", page);
        Assert.Contains("계정 있는 1명(윤동호)", page);
        Assert.Contains("넣기 (7명)", page);   // 학생 4 + 교사 3(예 줄 · 빈 줄 · 틀린 줄 빼고)
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(3, raw.People.Count());   // 미리 보기는 넣지 않는다

        Assert.Contains("부서를 정할 수 없어요", await Preview(op, "명단.xlsx", Book(Kids, Staff)));
        Assert.Contains("엑셀 파일(.xlsx)만", await Preview(op, "유년부.csv", [1, 2, 3]));
    }

    [Fact]
    public async Task R302_넣으면_그_부서_명단과_반_배정을_비우고_채운다_관리자와_다른_부서는_그대로()
    {
        var op = await Login("operator@example.com");
        var page = await Preview(op, "유년부 (1).xlsx", Book(Kids, Staff));
        Assert.Contains("지우는 것을 알아요&quot;에 체크해야", await Apply(op, page, agree: false));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(3, raw.People.Count());

        var done = await Apply(op, page, agree: true);
        Assert.Contains("유년부에 넣었어요 · 학생 4명 · 교사 2명 · 계정 있는 사람 1명 고침", done);
        using var db = InMemoryDb.Raw(_db);
        var kids = db.People.Where(p => p.TenantId == _kids.Id).ToList();
        Assert.DoesNotContain(kids, p => p.Id == _oldPid);                                       // 계정 없는 옛 명단은 지움
        Assert.Empty(db.AttendanceChecks.Where(a => a.TenantId == _kids.Id));                     // 그 사람의 출석 · 새친구 선물도
        Assert.Empty(db.Gifts);
        Assert.Single(db.AttendanceChecks.Where(a => a.TenantId == _elem.Id));                    // 다른 부서는 그대로
        Assert.Contains(db.People, p => p.TenantId == _elem.Id && p.Name == "다른부서아이");

        var admin = kids.Single(p => p.Id == _adminPid);                                           // 관리자(계정 있는 줄)는 남고 전화번호만 고침
        Assert.Equal("010-1111-2222", admin.Phone1);
        Assert.Equal("T1002", admin.RoleCode);
        var cs = kids.Single(p => p.Name == "김철수");
        Assert.Equal(("T1001", (byte?)3, (byte?)15, "010-1234-5678", "010-9876-5432"), (cs.RoleCode, cs.BirthMonth, cs.BirthDay, cs.Phone1, cs.Phone2));
        Assert.Equal(((byte?)12, (byte?)1), (kids.Single(p => p.Name == "이영희").BirthMonth, kids.Single(p => p.Name == "이영희").BirthDay));
        var nc = kids.Single(p => p.Name == "박새롬");
        Assert.Equal(ClassService.NewcomerCode, nc.RoleCode);                                      // 새친구반 → 새친구 역할, 반 배정 없음(미배정반)
        Assert.Equal("T1005", kids.Single(p => p.Name == "이부장").RoleCode);                     // 임원 역할 → 부서 역할 코드
        Assert.Equal("T1002", kids.Single(p => p.Name == "김선생").RoleCode);

        var members = db.ClassMembers.Where(m => m.TenantId == _kids.Id).ToList();
        int Pid(string n) => kids.Single(p => p.Name == n).Id;
        Assert.Equal(5, members.Count);
        Assert.Contains(members, m => m.PersonId == Pid("김철수") && m.ClassId == _c1 && !m.IsTeacher);
        Assert.Contains(members, m => m.PersonId == Pid("이영희") && m.ClassId == _c2 && !m.IsTeacher);
        Assert.Contains(members, m => m.PersonId == Pid("김선생") && m.ClassId == _c1 && m.IsTeacher);
        Assert.Contains(members, m => m.PersonId == Pid("이부장") && m.ClassId == _c2 && m.IsTeacher);   // 임원도 담임(R-232)
        Assert.Contains(members, m => m.PersonId == _adminPid && m.ClassId == _c2 && m.IsTeacher);
        Assert.DoesNotContain(members, m => m.PersonId == nc.Id);
        var none = kids.Single(p => p.Name == "반없음");                                          // 반을 비우면 학생 · 미배정반
        Assert.Equal("T1001", none.RoleCode);
        Assert.DoesNotContain(members, m => m.PersonId == none.Id);
        Assert.Contains(db.Users, u => u.UserName == _adminUser);                                 // 계정은 그대로
        Assert.StartsWith("file=유년부 (1).xlsx;students=4;teachers=2;updated=1;dropped=1;attendance=1;skipped=2",
            db.AuditLogs.Single(a => a.Action == RosterUploadService.Action).Detail);
    }

    [Fact]
    public async Task R303_템플릿은_부서_반_목록과_임원_역할을_담고_파일_이름이_부서다()
    {
        var op = await Login("operator@example.com");
        var list = await op.GetStringAsync("/platform/roster-upload");
        Assert.Contains("템플릿 받기 (유년부.xlsx)", list);
        Assert.Contains("반 2개 · 명부 2명", list);
        var res = await op.GetAsync($"/platform/roster-upload?handler=Template&t={_kids.Id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("유년부.xlsx", res.Content.Headers.ContentDisposition!.FileNameStar ?? res.Content.Headers.ContentDisposition.FileName);
        using var wb = new XLWorkbook(await res.Content.ReadAsStreamAsync());
        Assert.Equal(new[] { "학생", "교사", "목록" }, wb.Worksheets.Select(w => w.Name).ToArray());
        var l = wb.Worksheet("목록");
        Assert.Equal(new[] { "새친구반", "1반", "2반" }, Enumerable.Range(2, 3).Select(r => l.Cell(r, 1).GetString()).ToArray());
        Assert.Contains("부장", Enumerable.Range(2, 20).Select(r => l.Cell(r, 2).GetString()));
        Assert.DoesNotContain("교사", Enumerable.Range(2, 20).Select(r => l.Cell(r, 2).GetString()));
        Assert.Equal("이름", wb.Worksheet("교사").Cell(2, 1).GetString());

        // 받은 템플릿을 그대로 올리면(예 줄만) 넣을 사람이 없다
        var page = await Preview(op, "유년부.xlsx", await (await op.GetAsync($"/platform/roster-upload?handler=Template&t={_kids.Id}")).Content.ReadAsByteArrayAsync());
        Assert.Contains("넣기 (0명)", page);
        Assert.Contains("넣을 사람이 없어요", await Apply(op, page, agree: true));
    }

    [Fact]
    public async Task R304_플랫폼_관리자만()
    {
        var admin = await Login("admin@example.com");
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync("/platform/roster-upload")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync($"/platform/roster-upload?handler=Template&t={_kids.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("3/15", 3, 15)]
    [InlineData("03.05", 3, 5)]
    [InlineData("12월 1일", 12, 1)]
    [InlineData("2/29", 2, 29)]
    [InlineData("2015-07-02", 7, 2)]
    public void 생일은_월_일만_읽는다(string text, int m, int d)
    {
        using var wb = new XLWorkbook();
        var cell = wb.AddWorksheet("a").Cell(1, 1);
        cell.Value = text;
        Assert.Equal(((byte?)m, (byte?)d, true), RosterUploadService.ParseBirthday(cell));
    }

    [Fact]
    public void 전화번호는_하이픈_꼴로_바꾸고_파일_이름은_부서가_하나일_때만()
    {
        Assert.Equal(("010-1234-5678", true), RosterUploadService.ParsePhone("01012345678"));
        Assert.Equal(("010-1234-5678", true), RosterUploadService.ParsePhone("1012345678"));   // 엑셀이 앞 0 을 지운 경우
        Assert.Equal(("02-123-4567", true), RosterUploadService.ParsePhone("021234567"));
        Assert.Equal((null, false), RosterUploadService.ParsePhone("1234"));
        Assert.Equal(((string?)null, true), RosterUploadService.ParsePhone(" "));
        var ts = new[] { (1, "잠실새내교회 유년부"), (2, "잠실새내교회 초등부"), (3, "잠실새내교회 유아부") };
        Assert.Equal(1, RosterUploadService.MatchTenant("유년부.xlsx", ts));
        Assert.Equal(2, RosterUploadService.MatchTenant("초등부 명단 (2).xlsx", ts));
        Assert.Null(RosterUploadService.MatchTenant("잠실새내교회.xlsx", ts));   // 셋 다 맞음
        Assert.Null(RosterUploadService.MatchTenant("중등부.xlsx", ts));
    }
}
