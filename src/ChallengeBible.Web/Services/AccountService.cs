using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record SignUpInput(string DisplayName, string Email, string Password, string PasswordConfirm);

/// <summary>칸 이름 → 오류 문구. 비어 있으면 성공.</summary>
public sealed class FieldErrors : Dictionary<string, string>
{
    public bool Ok => Count == 0;
}

public sealed record LoginResult(AppUser? Account, bool MustChangePassword, string? Error);

/// <summary>로그인한 뒤 갈 곳(0045): 승인된 소속 수로 정한다.</summary>
public sealed record AfterLogin(int ApprovedCount, string? OnlyTenantSlug);

/// <summary>
/// 가입 · 로그인. 받는 정보는 이름(또는 별명) · 이메일 · 비밀번호뿐이다(R-137, ADR 0050).
/// 계정은 플랫폼 표라 영역과 상관없이 다룬다.
/// </summary>
public sealed partial class AccountService(IAppDb db, ISystemDb systemDb, TimeProvider time)
{
    private static readonly PasswordHasher<AppUser> Hasher = new();

    // 형식만 검사한다(인증 메일은 1차에 없음, ADR 0050). 길이는 열 크기(254)와 맞춘다.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRule();

    public const int MinPassword = 4;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>아이디는 이메일의 @ 앞부분이다(유년부와 같다). 이미 있으면 가입할 수 없다.</summary>
    public static string UserNameFromEmail(string email) => (email.Split('@')[0]).Trim();

    public async Task<(AppUser? Account, FieldErrors Errors)> SignUpAsync(SignUpInput input, CancellationToken ct = default)
    {
        var errors = new FieldErrors();
        var name = input.DisplayName?.Trim() ?? "";
        var email = NormalizeEmail(input.Email ?? "");

        if (name.Length == 0) errors["DisplayName"] = "이름이나 별명을 써 주세요.";
        else if (name.Length > 20) errors["DisplayName"] = "이름은 20자까지 쓸 수 있어요.";
        if (email.Length > 254 || !EmailRule().IsMatch(email)) errors["Email"] = "이메일 주소를 확인해 주세요. 예: name@example.com";
        if ((input.Password ?? "").Length < MinPassword) errors["Password"] = $"비밀번호는 {MinPassword}자 이상으로 정해 주세요.";
        else if (input.Password != input.PasswordConfirm) errors["PasswordConfirm"] = "비밀번호가 서로 달라요.";
        if (!errors.Ok) return (null, errors);

        var userName = UserNameFromEmail(email);
        var normalized = AppUser.Normalize(userName);
        var normalizedEmail = AppUser.Normalize(email);
        await using var ctx = db.Create();
        if (await ctx.Users.AnyAsync(a => a.NormalizedEmail == normalizedEmail, ct))
        {
            errors["Email"] = "이미 가입된 이메일이에요. 로그인해 주세요.";
            return (null, errors);
        }
        if (await ctx.Users.AnyAsync(a => a.NormalizedUserName == normalized, ct))
        {
            errors["Email"] = "이 이메일의 앞부분이 이미 아이디로 쓰이고 있어요. 다른 이메일을 써 주세요.";
            return (null, errors);
        }

        var account = new AppUser
        {
            UserName = userName, NormalizedUserName = normalized, Email = email, NormalizedEmail = normalizedEmail,
            FirstName = name, LastName = "", SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true, InsertDT = time.GetUtcNow().UtcDateTime,
        };
        account.PasswordHash = Hasher.HashPassword(account, input.Password!);
        ctx.Users.Add(account);
        var basic = await ctx.IdentityRoleRows.AsNoTracking().Where(r => r.Name == IdentityRoles.Basic).Select(r => r.Id).FirstOrDefaultAsync(ct);
        if (basic is not null) ctx.UserRoles.Add(new AppUserRole { UserId = account.Id, RoleId = basic });
        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)   // 동시에 같은 아이디로 가입한 경우(UserNameIndex)
        {
            errors["Email"] = "이미 가입된 이메일이에요. 로그인해 주세요.";
            return (null, errors);
        }
        return (account, errors);
    }

    /// <summary>로그인 칸 하나에 아이디 또는 이메일(유년부와 같다). 이메일 모양이면 이메일로 아이디를 찾는다.</summary>
    public async Task<LoginResult> LoginAsync(string login, string password, CancellationToken ct = default)
    {
        const string wrong = "아이디(이메일) 또는 비밀번호가 맞지 않아요. 다시 확인해 주세요.";
        var id = (login ?? "").Trim();
        if (id.Length == 0) return new(null, false, "아이디나 이메일을 넣어 주세요.");
        if (string.IsNullOrEmpty(password)) return new(null, false, "비밀번호를 넣어 주세요.");

        var key = AppUser.Normalize(id);
        await using var ctx = db.Create();
        var account = EmailRule().IsMatch(id)
            ? await ctx.Users.FirstOrDefaultAsync(a => a.NormalizedEmail == key, ct)
            : await ctx.Users.SingleOrDefaultAsync(a => a.NormalizedUserName == key, ct);
        if (account is null || account.PasswordHash is null || account.IsDisabled(time.GetUtcNow())) return new(null, false, wrong);

        var verdict = Hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verdict == PasswordVerificationResult.Failed) return new(null, false, wrong);
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = Hasher.HashPassword(account, password);
            await ctx.SaveChangesAsync(ct);
        }
        var must = await ctx.UserSettings.AsNoTracking().Where(s => s.UserName == account.UserName).Select(s => s.MustChangePassword).FirstOrDefaultAsync(ct);
        return new(account, must, null);
    }

    /// <summary>쿠키에 넣을 값.</summary>
    public async Task<ChallengeBible.Web.Auth.SignedInUser> ToSignedInAsync(AppUser a, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var must = await ctx.UserSettings.AsNoTracking().Where(s => s.UserName == a.UserName).Select(s => s.MustChangePassword).FirstOrDefaultAsync(ct);
        return new(a.UserName!, a.Id, a.DisplayName, await IsPlatformAdminAsync(ctx, a.Id, ct), must);
    }

    /// <summary>플랫폼 관리자 = Identity 의 SuperAdmin 역할(0090).</summary>
    public static Task<bool> IsPlatformAdminAsync(AppDbContext ctx, string userId, CancellationToken ct) =>
        (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
         where ur.UserId == userId && r.Name == IdentityRoles.SuperAdmin select ur).AnyAsync(ct);

    private static async Task<UserSetting> SettingAsync(AppDbContext ctx, string userName, CancellationToken ct)
    {
        var s = await ctx.UserSettings.SingleOrDefaultAsync(x => x.UserName == userName, ct);
        if (s is null) ctx.UserSettings.Add(s = new UserSetting { UserName = userName });
        return s;
    }

    /// <summary>새 비밀번호로 바꾼다. 임시 비밀번호(0035) 뒤 첫 로그인에서 반드시 거친다.</summary>
    public async Task<FieldErrors> ChangePasswordAsync(string userName, string password, string confirm, CancellationToken ct = default)
    {
        var errors = new FieldErrors();
        if ((password ?? "").Length < MinPassword) errors["Password"] = $"비밀번호는 {MinPassword}자 이상으로 정해 주세요.";
        else if (password != confirm) errors["PasswordConfirm"] = "비밀번호가 서로 달라요.";
        if (!errors.Ok) return errors;

        await using var ctx = db.Create();
        var account = await ctx.Users.SingleAsync(a => a.UserName == userName, ct);
        account.PasswordHash = Hasher.HashPassword(account, password!);
        (await SettingAsync(ctx, userName, ct)).MustChangePassword = false;
        await ctx.SaveChangesAsync(ct);
        return errors;
    }

    /// <summary>
    /// 스스로 바꿀 때(내 계정): 지금 비밀번호가 맞아야 바꾼다(로그인된 휴대폰을 다른 사람이 집어도 못 바꾸게).
    /// 임시 비밀번호로 들어온 강제 변경(0035)은 <see cref="ChangePasswordAsync"/> 로 — 지금 비밀번호를 묻지 않는다.
    /// </summary>
    public async Task<FieldErrors> ChangeOwnPasswordAsync(string userName, string current, string password, string confirm, CancellationToken ct = default)
    {
        await using (var ctx = db.Create())
        {
            var account = await ctx.Users.AsNoTracking().SingleAsync(a => a.UserName == userName, ct);
            if (Hasher.VerifyHashedPassword(account, account.PasswordHash ?? "", current ?? "") == PasswordVerificationResult.Failed)
                return new FieldErrors { ["Current"] = "지금 비밀번호가 맞지 않아요." };
        }
        return await ChangePasswordAsync(userName, password, confirm, ct);
    }

    public const int MaxName = 50;

    /// <summary>이름 바꾸기(1 ~ 50자). 순위 · 가족팀 · 선생님 화면에 이 이름이 보인다. 바꾼 이름을 돌려준다(오류면 null).</summary>
    public async Task<(string? Name, FieldErrors Errors)> RenameAsync(string userName, string? name, CancellationToken ct = default)
    {
        var errors = new FieldErrors();
        var n = (name ?? "").Trim();
        if (n.Length == 0) errors["DisplayName"] = "이름을 써 주세요.";
        else if (n.Length > MaxName) errors["DisplayName"] = $"이름은 {MaxName}자까지 쓸 수 있어요.";
        if (!errors.Ok) return (null, errors);
        await using var ctx = db.Create();
        var account = await ctx.Users.SingleAsync(a => a.UserName == userName, ct);
        account.FirstName = n;
        account.LastName = "";
        await ctx.SaveChangesAsync(ct);
        return (n, errors);
    }

    /// <summary>통독 색 테마(ADR 0073): 고를 수 있는 키. "default" 는 파랑.</summary>
    public static readonly string[] ReadingThemes = ["default", "paper", "forest", "sand"];

    /// <summary>아직 고르지 않은 계정(null)에 보이는 테마.</summary>
    public const string DefaultReadingTheme = "forest";

    /// <summary>내 통독 테마(고르지 않았으면 null).</summary>
    public async Task<string?> ReadingThemeAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.UserSettings.Where(a => a.UserName == userName).Select(a => a.ReadingTheme).SingleOrDefaultAsync(ct);
    }

    /// <summary>통독 테마 바꾸기. 빈 값 = 고르지 않음(기본 테마). "default"(파랑)는 고른 값으로 남긴다. 모르는 키면 false.</summary>
    public async Task<bool> SetReadingThemeAsync(string userName, string? theme, CancellationToken ct = default)
    {
        var t = string.IsNullOrWhiteSpace(theme) ? null : theme;
        if (t is not null && !ReadingThemes.Contains(t)) return false;
        await using var ctx = db.Create();
        (await SettingAsync(ctx, userName, ct)).ReadingTheme = t;
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>내 계정 화면용: 이름 · 이메일 · 가입한 날(UTC).</summary>
    public async Task<(string Name, string Email, DateTime CreatedUtc)> ProfileAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var a = await ctx.Users.AsNoTracking().SingleAsync(x => x.UserName == userName, ct);
        return (a.DisplayName, a.Email ?? "", a.InsertDT);
    }

    public async Task<bool> MustChangePasswordAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.UserSettings.Where(a => a.UserName == userName).Select(a => a.MustChangePassword).SingleOrDefaultAsync(ct);
    }

    /// <summary>내 소속과 신청(R-135). 소속 표는 RLS 가 "내 줄"을 열어 주고, 앱 필터는 영역 조건이라 여기서는 사람 조건으로 바꿔 읽는다.</summary>
    public async Task<List<MyMembership>> MyMembershipsAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var userId = await ctx.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (userId is null) return [];
        var approved = await (from d in ctx.DeptRoles.IgnoreQueryFilters()
                              where d.UserId == userId
                              join t in ctx.Tenants on d.TenantId equals t.Id
                              select new MyMembership(d.Id, t.Id, t.Slug, t.Name, MembershipStatus.Approved, t.Status == TenantStatus.Active, t.MissionEnabled, t.ReadingEnabled, d.InsertDT, t.IsLobby)).ToListAsync(ct);
        var pending = await (from a in ctx.SignupApplications.IgnoreQueryFilters()
                             where a.UserName == userName && a.Status != SignupStatus.Approved
                             join t in ctx.Tenants on a.TenantId equals t.Id
                             select new MyMembership(a.Id, t.Id, t.Slug, t.Name, a.Status == SignupStatus.Pending ? MembershipStatus.Requested : MembershipStatus.Rejected, t.Status == TenantStatus.Active, t.MissionEnabled, t.ReadingEnabled, a.InsertDT, t.IsLobby)).ToListAsync(ct);
        return approved.Concat(pending).OrderBy(m => m.TenantName).ToList();
    }

    public async Task<AfterLogin> WhereToGoAsync(string userName, CancellationToken ct = default)
    {
        var approved = (await MyMembershipsAsync(userName, ct))
            .Where(m => m.Status == MembershipStatus.Approved && m.TenantActive).ToList();
        return new(approved.Count, approved.Count == 1 ? approved[0].Slug : null);
    }
}

public sealed record MyMembership(int MembershipId, int TenantId, string Slug, string TenantName, string Status,
    bool TenantActive, bool MissionEnabled, bool ReadingEnabled, DateTime RequestedUtc, bool IsLobby = false);
