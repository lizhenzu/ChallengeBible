using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record SignUpInput(string DisplayName, string LoginId, string Password, string PasswordConfirm);

/// <summary>칸 이름 → 오류 문구. 비어 있으면 성공.</summary>
public sealed class FieldErrors : Dictionary<string, string>
{
    public bool Ok => Count == 0;
}

public sealed record LoginResult(Account? Account, string? Error);

/// <summary>로그인한 뒤 갈 곳(0045): 승인된 소속 수로 정한다.</summary>
public sealed record AfterLogin(int ApprovedCount, string? OnlyTenantSlug);

/// <summary>
/// 가입 · 로그인. 받는 정보는 이름(또는 별명) · 아이디 · 비밀번호뿐이다(R-137).
/// 계정은 플랫폼 표라 영역과 상관없이 다룬다.
/// </summary>
public sealed partial class AccountService(IAppDb db, TimeProvider time)
{
    private static readonly PasswordHasher<Account> Hasher = new();

    [GeneratedRegex("^[a-z0-9]{4,20}$")]
    private static partial Regex LoginIdRule();

    public const int MinPassword = 4;

    public static string NormalizeLoginId(string loginId) => loginId.Trim().ToLowerInvariant();

    public async Task<(Account? Account, FieldErrors Errors)> SignUpAsync(SignUpInput input, CancellationToken ct = default)
    {
        var errors = new FieldErrors();
        var name = input.DisplayName?.Trim() ?? "";
        var loginId = NormalizeLoginId(input.LoginId ?? "");

        if (name.Length == 0) errors["DisplayName"] = "이름이나 별명을 써 주세요.";
        else if (name.Length > 20) errors["DisplayName"] = "이름은 20자까지 쓸 수 있어요.";
        if (!LoginIdRule().IsMatch(loginId)) errors["LoginId"] = "영어 소문자와 숫자로 4~20자를 써 주세요.";
        if ((input.Password ?? "").Length < MinPassword) errors["Password"] = $"비밀번호는 {MinPassword}자 이상으로 정해 주세요.";
        else if (input.Password != input.PasswordConfirm) errors["PasswordConfirm"] = "비밀번호가 서로 달라요.";
        if (!errors.Ok) return (null, errors);

        await using var ctx = db.Create();
        if (await ctx.Accounts.AnyAsync(a => a.LoginId == loginId, ct))
        {
            errors["LoginId"] = "이미 누가 쓰고 있어요. 다른 아이디를 써 주세요.";
            return (null, errors);
        }

        var account = new Account
        {
            LoginId = loginId,
            DisplayName = name,
            PasswordHash = "",
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        account.PasswordHash = Hasher.HashPassword(account, input.Password!);
        ctx.Accounts.Add(account);
        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)   // 동시에 같은 아이디로 가입한 경우(UQ_Account_LoginId)
        {
            errors["LoginId"] = "이미 누가 쓰고 있어요. 다른 아이디를 써 주세요.";
            return (null, errors);
        }
        return (account, errors);
    }

    public async Task<LoginResult> LoginAsync(string loginId, string password, CancellationToken ct = default)
    {
        const string wrong = "아이디 또는 비밀번호가 맞지 않아요. 다시 확인해 주세요.";
        var id = NormalizeLoginId(loginId ?? "");
        if (id.Length == 0) return new(null, "아이디를 넣어 주세요.");
        if (string.IsNullOrEmpty(password)) return new(null, "비밀번호를 넣어 주세요.");

        await using var ctx = db.Create();
        var account = await ctx.Accounts.SingleOrDefaultAsync(a => a.LoginId == id, ct);
        if (account is null || account.Status != "Active") return new(null, wrong);

        var verdict = Hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verdict == PasswordVerificationResult.Failed) return new(null, wrong);
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = Hasher.HashPassword(account, password);
            await ctx.SaveChangesAsync(ct);
        }
        return new(account, null);
    }

    /// <summary>새 비밀번호로 바꾼다. 임시 비밀번호(0035) 뒤 첫 로그인에서 반드시 거친다.</summary>
    public async Task<FieldErrors> ChangePasswordAsync(int accountId, string password, string confirm, CancellationToken ct = default)
    {
        var errors = new FieldErrors();
        if ((password ?? "").Length < MinPassword) errors["Password"] = $"비밀번호는 {MinPassword}자 이상으로 정해 주세요.";
        else if (password != confirm) errors["PasswordConfirm"] = "비밀번호가 서로 달라요.";
        if (!errors.Ok) return errors;

        await using var ctx = db.Create();
        var account = await ctx.Accounts.SingleAsync(a => a.Id == accountId, ct);
        account.PasswordHash = Hasher.HashPassword(account, password!);
        account.MustChangePassword = false;
        await ctx.SaveChangesAsync(ct);
        return errors;
    }

    public async Task<bool> MustChangePasswordAsync(int accountId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Accounts.Where(a => a.Id == accountId).Select(a => a.MustChangePassword).SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// 내 소속(R-135). 소속 표는 RLS 가 "내 계정의 소속"을 열어 주고(fn_Membership),
    /// 앱 필터는 영역 조건이라 여기서는 계정 조건으로 바꿔 읽는다.
    /// </summary>
    public async Task<List<MyMembership>> MyMembershipsAsync(int accountId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Memberships.IgnoreQueryFilters()
            .Where(m => m.AccountId == accountId)
            .Join(ctx.Tenants, m => m.TenantId, t => t.Id, (m, t) => new MyMembership(
                m.Id, t.Id, t.Slug, t.Name, m.Status, t.Status == TenantStatus.Active, t.MissionEnabled, t.ReadingEnabled, m.RequestedUtc))
            .OrderBy(x => x.TenantName)
            .ToListAsync(ct);
    }

    public async Task<AfterLogin> WhereToGoAsync(int accountId, CancellationToken ct = default)
    {
        var approved = (await MyMembershipsAsync(accountId, ct))
            .Where(m => m.Status == MembershipStatus.Approved && m.TenantActive).ToList();
        return new(approved.Count, approved.Count == 1 ? approved[0].Slug : null);
    }
}

public sealed record MyMembership(int MembershipId, int TenantId, string Slug, string TenantName, string Status,
    bool TenantActive, bool MissionEnabled, bool ReadingEnabled, DateTime RequestedUtc);
