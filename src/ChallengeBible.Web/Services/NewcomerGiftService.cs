using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>전도한 아이를 아직 정하지 못한 새친구(선물 처리 위에 "전도한 아이를 골라 주세요"). Written = 등록 때 적은 이름, Same = 명부에서 같은 이름의 학생 수.</summary>
public sealed record InvitePending(int NewcomerId, string NewcomerName, string Written, int Same);
/// <summary>[전도한 아이 고르기]의 후보 학생.</summary>
public sealed record InviteCandidate(int PersonId, string Name, string? ClassName, bool Linked);

/// <summary>
/// 새친구 · 전도 선물(확정 시안 전도-선물, ADR 0094 C · 정한 것 15, R-286 고침): 새친구로 등록된 아이가 첫 방문부터 출석을 **모두 NewcomerVisits 번** 채우면
/// (언제든, 연속 아니어도) 새친구 본인과 데려온 아이에게 **함께** 생긴다(열쇠 N{새친구 명부 번호}, 한 번만, 기한 없음).
/// 데려온 아이 = 등록 때 적은 "전도한 사람" 이름과 같은 학생 · 새친구가 명부에 **한 명**이면 저절로, 아니면 선생님이 고른다. 스스로 왔으면 새친구 선물만.
/// 고르기 · 링크 · 건너뛰기는 생일 선물과 같다(BirthdayGiftService).
/// </summary>
public sealed class NewcomerGiftService(IAppDb db, TenantContext tenant, TenantClock clock, RuleService rules, TimeProvider time)
{
    private int T => tenant.Current.Id;
    private DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    public static string SourceKey(int newcomerId) => $"N{newcomerId}";
    public static int? NewcomerOf(string sourceKey) => sourceKey.Length > 1 && sourceKey[0] == 'N' && int.TryParse(sourceKey[1..], out var n) ? n : null;
    /// <summary>전도한 아이를 정하지 못한 채 건너뛴 전도 선물: 받는 사람 칸에 새친구 자신이 들어 있다.</summary>
    public static bool Unassigned(Gift g) => g.Kind == GiftKinds.Invite && g.PersonId is int p && NewcomerOf(g.SourceKey) == p;

    private sealed record Nc(int Id, string Name, string? UserId, bool Voluntary, string? Evangelist, int Visits);

    /// <summary>출석을 다 채운 새친구(명부의 첫 방문일이 있는 사람).</summary>
    private static async Task<List<Nc>> DoneAsync(AppDbContext ctx, int visits, CancellationToken ct)
    {
        var ncs = await ctx.People.AsNoTracking().Where(p => p.NewcomerFirstVisit != null)
            .Select(p => new { p.Id, p.Name, p.UserId, p.NewcomerVoluntary, p.NewcomerEvangelist, First = p.NewcomerFirstVisit!.Value }).ToListAsync(ct);
        if (ncs.Count == 0) return [];
        var ids = ncs.Select(n => n.Id).ToList();
        var checks = await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId)).Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct);
        return ncs.Select(n => new Nc(n.Id, n.Name, n.UserId, n.NewcomerVoluntary != false, n.NewcomerEvangelist?.Trim(),
                checks.Count(c => c.PersonId == n.Id && c.Sunday >= AttendanceService.SundayOf(n.First))))
            .Where(n => n.Visits >= visits).ToList();
    }

    /// <summary>명부의 학생 · 새친구(연결된 계정은 소속의 역할, 아니면 명부의 역할). 계정 이름(UserName)도 함께.</summary>
    private static async Task<List<(int Id, string Name, string? UserName)>> StudentsAsync(AppDbContext ctx, CancellationToken ct)
    {
        var people = await ctx.People.AsNoTracking().Select(p => new { p.Id, p.Name, p.UserId, p.RoleCode }).ToListAsync(ct);
        var uids = people.Select(p => p.UserId).OfType<string>().ToList();
        var linked = uids.Count == 0 ? [] : await (from d in ctx.DeptRoles join u in ctx.Users on d.UserId equals u.Id
                                                   where uids.Contains(d.UserId!) select new { u.Id, u.UserName, d.RoleCode }).ToListAsync(ct);
        return people.Select(p => (p, a: p.UserId is null ? null : linked.FirstOrDefault(l => l.Id == p.UserId)))
            .Where(x => BirthdayGiftService.BirthdayRoles.Contains(((x.p.UserId is null ? x.p.RoleCode : x.a?.RoleCode) ?? "").Trim()))
            .Select(x => (x.p.Id, x.p.Name, x.a?.UserName)).ToList();
    }

    /// <summary>선물 만들기. 미션 홈 · 선물 처리 화면을 열 때 부른다(같은 열쇠는 한 번만, UX_Gift_Source).</summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (tenant.IsLobby) return;
        await using var ctx = db.Create();
        var r = (await rules.ForDateAsync(Today, ct)).Gifts;
        var done = await DoneAsync(ctx, r.NewcomerVisits, ct);
        if (done.Count == 0) return;
        var keys = done.Select(n => SourceKey(n.Id)).ToList();
        var have = (await ctx.Gifts.AsNoTracking().Where(g => (g.Kind == GiftKinds.Newcomer || g.Kind == GiftKinds.Invite) && keys.Contains(g.SourceKey))
            .Select(g => new { g.Kind, g.SourceKey }).ToListAsync(ct)).Select(g => (g.Kind, g.SourceKey)).ToHashSet();
        var students = await StudentsAsync(ctx, ct);
        var uids = done.Select(n => n.UserId).OfType<string>().ToList();
        var accounts = await ctx.Users.AsNoTracking().Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
        var s = await ctx.GiftSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var added = new List<Gift>();
        Gift New(string kind, string key, int? personId, string? user, int min, int max) => new()
        {
            TenantId = T, Kind = kind, Year = (short)Today.Year, UserName = user, PersonId = personId, SourceKey = key, State = GiftStates.Open,
            OpenFrom = Today, OpenUntil = null, MinAmount = min, MaxAmount = max, CreatedUtc = now,   // 기한 없음(새친구 · 전도)
        };
        foreach (var n in done)
        {
            var key = SourceKey(n.Id);
            if (!have.Contains((GiftKinds.Newcomer, key)))
                added.Add(New(GiftKinds.Newcomer, key, n.Id, n.UserId is null ? null : accounts.GetValueOrDefault(n.UserId),
                    s?.NewcomerMin ?? r.Newcomer.Min, s?.NewcomerMax ?? r.Newcomer.Max));
            if (n.Voluntary || string.IsNullOrEmpty(n.Evangelist) || have.Contains((GiftKinds.Invite, key))) continue;
            var same = students.Where(x => x.Id != n.Id && x.Name.Trim() == n.Evangelist).ToList();
            if (same.Count == 1)   // 이름이 한 명과 맞으면 저절로, 아니면 선생님이 고른다(PendingAsync)
                added.Add(New(GiftKinds.Invite, key, same[0].Id, same[0].UserName, s?.InviteMin ?? r.Invite.Min, s?.InviteMax ?? r.Invite.Max));
        }
        if (added.Count == 0) return;
        ctx.Gifts.AddRange(added);
        try
        {
            await ctx.SaveChangesAsync(ct);
            foreach (var g in added) ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Open, AtUtc = now, Detail = $"{g.Kind} {g.SourceKey} {g.MinAmount}-{g.MaxAmount}" });
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { }   // 다른 요청이 같은 선물을 먼저 만들었다
    }

    /// <summary>전도한 아이를 정하지 못한 새친구: 데려왔다고 적었는데 이름이 명부의 학생 한 명과 맞지 않고, 아직 전도 선물이 없는 것.</summary>
    public async Task<IReadOnlyList<InvitePending>> PendingAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var r = (await rules.ForDateAsync(Today, ct)).Gifts;
        var done = (await DoneAsync(ctx, r.NewcomerVisits, ct)).Where(n => !n.Voluntary && !string.IsNullOrEmpty(n.Evangelist)).ToList();
        if (done.Count == 0) return [];
        var keys = done.Select(n => SourceKey(n.Id)).ToList();
        var have = await ctx.Gifts.AsNoTracking().Where(g => g.Kind == GiftKinds.Invite && keys.Contains(g.SourceKey)).Select(g => g.SourceKey).ToListAsync(ct);
        var students = await StudentsAsync(ctx, ct);
        return done.Where(n => !have.Contains(SourceKey(n.Id)))
            .Select(n => new InvitePending(n.Id, n.Name, n.Evangelist!, students.Count(x => x.Id != n.Id && x.Name.Trim() == n.Evangelist))).ToList();
    }

    /// <summary>[전도한 아이 고르기] 후보: 적힌 이름과 같은 학생 먼저, q 가 있으면 이름에 q 가 든 학생(새친구 자신은 뺌), 최대 20명.</summary>
    public async Task<IReadOnlyList<InviteCandidate>> CandidatesAsync(int newcomerId, string? q, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var written = await ctx.People.Where(p => p.Id == newcomerId).Select(p => p.NewcomerEvangelist).SingleOrDefaultAsync(ct);
        var term = string.IsNullOrWhiteSpace(q) ? written?.Trim() : q.Trim();
        if (string.IsNullOrEmpty(term)) return [];
        var students = (await StudentsAsync(ctx, ct)).Where(x => x.Id != newcomerId && x.Name.Contains(term)).OrderBy(x => x.Name != term).ThenBy(x => x.Name).Take(20).ToList();
        var ids = students.Select(x => x.Id).ToList();
        var termId = await ctx.ClassTerms.AsNoTracking().OrderByDescending(t => t.StartDate).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        var classes = termId is null ? [] : await (from m in ctx.ClassMembers join c in ctx.Classes on m.ClassId equals c.Id
                                                   where m.TermId == termId && ids.Contains(m.PersonId) select new { m.PersonId, c.Name }).ToListAsync(ct);
        return students.Select(x => new InviteCandidate(x.Id, x.Name, classes.FirstOrDefault(c => c.PersonId == x.Id)?.Name, x.UserName is not null)).ToList();
    }

    /// <summary>선생님이 고른 아이에게 전도 선물을 만든다(아직 없을 때만).</summary>
    public async Task<bool> AssignAsync(string actor, int newcomerId, int personId, CancellationToken ct = default)
    {
        if ((await PendingAsync(ct)).All(p => p.NewcomerId != newcomerId) || personId == newcomerId) return false;
        await using var ctx = db.Create();
        var who = (await StudentsAsync(ctx, ct)).FirstOrDefault(x => x.Id == personId);
        if (who == default) return false;
        var r = (await rules.ForDateAsync(Today, ct)).Gifts;
        var s = await ctx.GiftSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var g = new Gift
        {
            TenantId = T, Kind = GiftKinds.Invite, Year = (short)Today.Year, UserName = who.UserName, PersonId = who.Id, SourceKey = SourceKey(newcomerId), State = GiftStates.Open,
            OpenFrom = Today, MinAmount = s?.InviteMin ?? r.Invite.Min, MaxAmount = s?.InviteMax ?? r.Invite.Max, CreatedUtc = now,
        };
        ctx.Gifts.Add(g);
        await ctx.SaveChangesAsync(ct);
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Open, ActorUserName = actor, AtUtc = now, Detail = $"Invite {g.SourceKey} by teacher" });
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>전도한 아이를 정하지 않고 [건너뛰기]: 받는 사람 칸에 새친구 자신을 넣은 건너뜀 줄(되돌리면 지운다, BirthdayGiftService.SkipAsync).</summary>
    public async Task<bool> SkipPendingAsync(string actor, int newcomerId, CancellationToken ct = default)
    {
        if ((await PendingAsync(ct)).All(p => p.NewcomerId != newcomerId)) return false;
        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        var g = new Gift { TenantId = T, Kind = GiftKinds.Invite, Year = (short)Today.Year, PersonId = newcomerId, SourceKey = SourceKey(newcomerId), State = GiftStates.Skipped, CreatedUtc = now };
        ctx.Gifts.Add(g);
        await ctx.SaveChangesAsync(ct);
        ctx.GiftLogs.Add(new GiftLog { TenantId = T, GiftId = g.Id, Action = GiftActions.Skip, ActorUserName = actor, AtUtc = now, Detail = "no inviter" });
        await ctx.SaveChangesAsync(ct);
        return true;
    }
}
