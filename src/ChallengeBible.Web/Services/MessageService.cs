using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>쪽지 받는 사람 찾기 결과: 이름 · 부서 · 역할만(아이디는 보내기에만 쓰고 화면에는 보이지 않는다, R-328).</summary>
public sealed record MsgPerson(string UserName, string Name, string Where);

/// <summary>쪽지함 한 줄.</summary>
public sealed record MsgRow(long Id, string Kind, string Who, string? Where, string Body, DateTime SentUtc)
{
    public bool Unread { get; init; }
    public bool Anonymous { get; init; }
    public bool Broadcast => Kind == MessageKinds.Broadcast;
    public bool Feedback => Kind == MessageKinds.Feedback;
    /// <summary>자동 알림(회원 가입 · 부서 참여 신청).</summary>
    public bool System => Kind == MessageKinds.System;
    /// <summary>전체 쪽지 대상("모든 사용자" · "유년부 · 초등부").</summary>
    public string? Target { get; init; }
    /// <summary>보낸 쪽지: 상대가 읽었나(1:1) · 읽은 사람 수 / 받는 사람 수(전체).</summary>
    public bool Read { get; init; }
    public int ReadCount { get; init; }
    public int Total { get; init; }
    public int Replies { get; init; }
    public bool Canceled { get; init; }
    /// <summary>답장 받을 사람(없으면 답장 불가 — 익명).</summary>
    public string? ReplyTo { get; init; }
}

public enum MsgError { None, Empty, TooLong, NoRecipient, NotFound, NotAllowed }

/// <summary>
/// 쪽지(ADR 0112, R-325 ~ R-328): 계정끼리 1:1 · 개선 · 불편 접수(superadmin, 익명 가능) · 플랫폼 관리자 전체 쪽지(한 줄 + 대상 + 열거나 지울 때만 생기는 받은 기록).
/// 부서를 넘나들어서 시스템 연결로 읽고, 늘 "나에게 온 것 · 내가 보낸 것"으로만 거른다.
/// </summary>
public sealed class MessageService(ISystemDb systemDb, TimeProvider time)
{
    public const int MaxLength = 1000;
    public const string PlatformName = "플랫폼 관리자";
    public const string SystemName = "도전!!!성경말씀";
    /// <summary>개선 · 불편 쪽지를 받는 계정(사용자 2026-11-17 "superadmin 계정 하나"). 없으면 가장 먼저 만든 플랫폼 관리자.</summary>
    public const string FeedbackAccount = "superadmin";

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private static IQueryable<string> PlatformAdminNames(AppDbContext ctx) =>
        from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id join u in ctx.Users on ur.UserId equals u.Id
        where r.Name == IdentityRoles.SuperAdmin select u.UserName!;

    public static string ShortDept(string name, bool lobby) => lobby ? "처음 오신 분" : name.StartsWith("잠실새내교회 ") ? name["잠실새내교회 ".Length..] : name;

    /// <summary>계정 → 표시 이름 · "부서 · 역할"(임시 부서보다 진짜 부서를 먼저). 플랫폼 관리자는 "플랫폼 관리자".</summary>
    private static async Task<Dictionary<string, (string Name, string? Where, int? TenantId, string? Dept)>> WhoAsync(AppDbContext ctx, IEnumerable<string> names, CancellationToken ct)
    {
        var list = names.Distinct().ToList();
        if (list.Count == 0) return [];
        var users = await ctx.Users.AsNoTracking().Where(u => list.Contains(u.UserName!)).Select(u => new { u.Id, u.UserName, u.LastName, u.FirstName }).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var admins = await PlatformAdminNames(ctx).Where(n => list.Contains(n)).ToListAsync(ct);
        var roles = await (from d in ctx.DeptRoles.IgnoreQueryFilters() join t in ctx.Tenants on d.TenantId equals t.Id
                           where ids.Contains(d.UserId!) select new { d.UserId, d.TenantId, d.RoleCode, t.Name, t.IsLobby }).ToListAsync(ct);
        return users.ToDictionary(u => u.UserName!, u =>
        {
            if (admins.Contains(u.UserName!)) return (PlatformName, (string?)null, (int?)null, (string?)null);
            var r = roles.Where(x => x.UserId == u.Id).OrderBy(x => x.IsLobby).FirstOrDefault();
            var dept = r is null ? null : ShortDept(r.Name, r.IsLobby);
            return (AppUser.FullName(u.LastName, u.FirstName), r is null ? null : $"{dept} · {UserRole.LabelOf(UserRole.KindOf(r.RoleCode))}", r?.TenantId, dept);
        });
    }

    /// <summary>나에게 보이는 전체 쪽지(취소 안 됨, 대상이 없거나 내 소속 부서가 대상).</summary>
    private static IQueryable<Message> Broadcasts(AppDbContext ctx, string userName)
    {
        var myTenants = from d in ctx.DeptRoles.IgnoreQueryFilters() join u in ctx.Users on d.UserId equals u.Id where u.UserName == userName select d.TenantId;
        return ctx.Messages.Where(m => m.Kind == MessageKinds.Broadcast && m.CanceledUtc == null && m.SenderUserName != userName
            && (!ctx.MessageTargets.Any(t => t.MessageId == m.Id) || ctx.MessageTargets.Any(t => t.MessageId == m.Id && myTenants.Contains(t.TenantId))));
    }

    /// <summary>
    /// 안 읽은 쪽지 수(머리말 ✉ · 홈 띠 · 팝업). 모든 화면 머리말에서 부르므로, 쪽지 표가 아직 DB 에 없거나(배포 뒤 마이그레이션 전) 읽기에 실패하면
    /// 화면 전체가 멈추지 않게 0 으로 본다(2026-11-17 운영: SQL 실행 화면까지 열리지 않던 일).
    /// </summary>
    public async Task<int> UnreadAsync(string userName, CancellationToken ct = default)
    {
        try { return await CountUnreadAsync(userName, ct); }
        catch (Exception e) when (e is System.Data.Common.DbException or InvalidOperationException && e is not OperationCanceledException) { return 0; }
    }

    private async Task<int> CountUnreadAsync(string userName, CancellationToken ct)
    {
        await using var ctx = systemDb.Create(userName);
        var direct = await ctx.Messages.CountAsync(m => m.RecipientUserName == userName && !m.RecipientDeleted && m.ReadUtc == null && m.CanceledUtc == null, ct);
        var bc = await Broadcasts(ctx, userName).CountAsync(m => !ctx.MessageReceipts.Any(r => r.MessageId == m.Id && r.UserName == userName && (r.ReadUtc != null || r.DeletedUtc != null)), ct);
        return direct + bc;
    }

    public async Task<List<MsgRow>> InboxAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        var direct = await ctx.Messages.AsNoTracking().Where(m => m.RecipientUserName == userName && !m.RecipientDeleted && m.CanceledUtc == null).ToListAsync(ct);
        var bc = await Broadcasts(ctx, userName).AsNoTracking().ToListAsync(ct);
        var receipts = await ctx.MessageReceipts.AsNoTracking().Where(r => r.UserName == userName).ToDictionaryAsync(r => r.MessageId, ct);
        bc = bc.Where(m => receipts.GetValueOrDefault(m.Id)?.DeletedUtc is null).ToList();
        var who = await WhoAsync(ctx, direct.Concat(bc).Select(m => m.SenderUserName).OfType<string>(), ct);
        var targets = await TargetTextAsync(ctx, bc.Select(m => m.Id).ToList(), ct);
        return direct.Select(m => Row(m, m.SenderUserName, who) with { Unread = m.ReadUtc is null })
            .Concat(bc.Select(m => Row(m, m.SenderUserName, who) with { Unread = receipts.GetValueOrDefault(m.Id)?.ReadUtc is null, Target = targets.GetValueOrDefault(m.Id) }))
            .OrderByDescending(r => r.SentUtc).ToList();
    }

    public async Task<List<MsgRow>> SentAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        var list = await ctx.Messages.AsNoTracking().Where(m => m.SenderUserName == userName && !m.SenderDeleted).OrderByDescending(m => m.SentUtc).Take(200).ToListAsync(ct);
        var who = await WhoAsync(ctx, list.Select(m => m.RecipientUserName).OfType<string>(), ct);
        var bcIds = list.Where(m => m.Kind == MessageKinds.Broadcast).Select(m => m.Id).ToList();
        var targets = await TargetTextAsync(ctx, bcIds, ct);
        var reads = await ctx.MessageReceipts.Where(r => bcIds.Contains(r.MessageId) && r.ReadUtc != null).GroupBy(r => r.MessageId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var replies = await ctx.Messages.Where(m => m.ReplyToId != null && bcIds.Contains(m.ReplyToId.Value)).GroupBy(m => m.ReplyToId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var rows = new List<MsgRow>();
        foreach (var m in list)
        {
            if (m.Kind == MessageKinds.Broadcast)
                rows.Add(new MsgRow(m.Id, m.Kind, targets.GetValueOrDefault(m.Id) ?? "모든 사용자", null, m.Body, m.SentUtc)
                {
                    Target = targets.GetValueOrDefault(m.Id) ?? "모든 사용자", Canceled = m.CanceledUtc is not null, ReadCount = reads.GetValueOrDefault(m.Id),
                    Total = await AudienceAsync(ctx, m.Id, m.SenderUserName, ct), Replies = replies.GetValueOrDefault(m.Id),
                });
            else
            {
                var (name, where, _, _) = m.RecipientUserName is { } r && who.TryGetValue(r, out var w) ? w : ("(지운 계정)", null, null, null);
                rows.Add(new MsgRow(m.Id, m.Kind, name, where, m.Body, m.SentUtc) { Read = m.ReadUtc is not null, ReplyTo = m.RecipientUserName });
            }
        }
        return rows;
    }

    private static MsgRow Row(Message m, string? from, Dictionary<string, (string Name, string? Where, int? TenantId, string? Dept)> who)
    {
        if (m.Kind == MessageKinds.System) return new MsgRow(m.Id, m.Kind, SystemName, m.SenderLabel, m.Body, m.SentUtc);
        if (from is null)   // 익명 개선 쪽지: 부서만
            return new MsgRow(m.Id, m.Kind, "익명", m.SenderLabel, m.Body, m.SentUtc) { Anonymous = true };
        var (name, where, _, _) = who.TryGetValue(from, out var w) ? w : ("(지운 계정)", m.SenderLabel, null, null);
        return new MsgRow(m.Id, m.Kind, name, m.Kind == MessageKinds.Broadcast ? null : where ?? m.SenderLabel, m.Body, m.SentUtc) { ReplyTo = from };
    }

    private static async Task<Dictionary<long, string>> TargetTextAsync(AppDbContext ctx, List<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var rows = await (from t in ctx.MessageTargets join d in ctx.Tenants on t.TenantId equals d.Id where ids.Contains(t.MessageId) select new { t.MessageId, d.Name, d.IsLobby }).ToListAsync(ct);
        return ids.ToDictionary(i => i, i => rows.Where(r => r.MessageId == i).Select(r => ShortDept(r.Name, r.IsLobby)).ToList() is { Count: > 0 } l ? string.Join(" · ", l.OrderBy(DeptOrder.Key)) : "모든 사용자");
    }

    /// <summary>전체 쪽지를 받는 사람 수(지금 기준).</summary>
    private static async Task<int> AudienceAsync(AppDbContext ctx, long id, string? sender, CancellationToken ct)
    {
        var tids = await ctx.MessageTargets.Where(t => t.MessageId == id).Select(t => t.TenantId).ToListAsync(ct);
        return await AudienceCountAsync(ctx, tids, sender, ct);
    }

    private static async Task<int> AudienceCountAsync(AppDbContext ctx, IReadOnlyCollection<int> tenantIds, string? sender, CancellationToken ct) =>
        tenantIds.Count == 0
            ? await ctx.Users.CountAsync(u => u.UserName != sender, ct)
            : await (from d in ctx.DeptRoles.IgnoreQueryFilters() join u in ctx.Users on d.UserId equals u.Id
                     where tenantIds.Contains(d.TenantId) && u.UserName != sender select u.Id).Distinct().CountAsync(ct);

    /// <summary>보내기 전에 몇 명에게 가는지(확정 시안 쪽지 ⑦).</summary>
    public async Task<int> AudienceCountAsync(string actor, IReadOnlyCollection<int> tenantIds, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        return await AudienceCountAsync(ctx, tenantIds, actor, ct);
    }

    /// <summary>쪽지 한 통 열기: 내가 보냈거나 받은(전체 쪽지면 대상인) 것만. 받은 사람이 열면 읽음.</summary>
    public async Task<MsgRow?> OpenAsync(string userName, long id, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        var m = await ctx.Messages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return null;
        var mine = m.SenderUserName == userName && !m.SenderDeleted;
        var toMe = m.RecipientUserName == userName && !m.RecipientDeleted && m.CanceledUtc is null;
        var bc = !toMe && m.Kind == MessageKinds.Broadcast && await Broadcasts(ctx, userName).AnyAsync(x => x.Id == id, ct)
            && !await ctx.MessageReceipts.AnyAsync(r => r.MessageId == id && r.UserName == userName && r.DeletedUtc != null, ct);
        if (!mine && !toMe && !bc) return null;
        if (toMe && m.ReadUtc is null) { m.ReadUtc = Now; await ctx.SaveChangesAsync(ct); }
        if (bc)
        {
            var r = await ctx.MessageReceipts.SingleOrDefaultAsync(x => x.MessageId == id && x.UserName == userName, ct);
            if (r is null) ctx.MessageReceipts.Add(new MessageReceipt { MessageId = id, UserName = userName, ReadUtc = Now });
            else r.ReadUtc ??= Now;
            await ctx.SaveChangesAsync(ct);
        }
        if (mine && !toMe)
            return (await SentAsync(userName, ct)).FirstOrDefault(x => x.Id == id);
        var who = await WhoAsync(ctx, m.SenderUserName is { } s ? [s] : [], ct);
        var targets = bc ? await TargetTextAsync(ctx, [id], ct) : [];
        return Row(m, m.SenderUserName, who) with { Target = targets.GetValueOrDefault(id) };
    }

    private static MsgError Check(string? body) =>
        string.IsNullOrWhiteSpace(body) ? MsgError.Empty : body.Trim().Length > MaxLength ? MsgError.TooLong : MsgError.None;

    /// <summary>1:1 쪽지 · 답장(R-325). 받는 사람은 아무 계정(제한 없음, 사용자 2026-11-17 질문 1 → 3).</summary>
    public async Task<MsgError> SendAsync(string userName, string? to, string? body, long? replyTo = null, CancellationToken ct = default)
    {
        if (Check(body) is var e and not MsgError.None) return e;
        if (string.IsNullOrWhiteSpace(to) || to == userName) return MsgError.NoRecipient;
        await using var ctx = systemDb.Create(userName);
        if (!await ctx.Users.AnyAsync(u => u.UserName == to, ct)) return MsgError.NoRecipient;
        var who = await WhoAsync(ctx, [userName], ct);
        var me = who.GetValueOrDefault(userName);
        ctx.Messages.Add(new Message
        {
            Kind = MessageKinds.Direct, SenderUserName = userName, SenderTenantId = me.TenantId, SenderLabel = me.Where, RecipientUserName = to,
            Body = body!.Trim(), SentUtc = Now, ReplyToId = replyTo,
        });
        await ctx.SaveChangesAsync(ct);
        return MsgError.None;
    }

    private static async Task<string?> FeedbackTargetAsync(AppDbContext ctx, CancellationToken ct) =>
        await PlatformAdminNames(ctx).Where(n => n == FeedbackAccount).FirstOrDefaultAsync(ct)
        ?? await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id join u in ctx.Users on ur.UserId equals u.Id
                  where r.Name == IdentityRoles.SuperAdmin orderby u.InsertDT select u.UserName).FirstOrDefaultAsync(ct);

    /// <summary>
    /// 자동 알림(R-329, 2026-11-17 사용자): 회원 가입 · 부서 참여 신청이 생기면 superadmin 에게 쪽지 한 통. 보낸 사람 "도전!!!성경말씀", 답장 없음.
    /// 알림이 실패해도 가입 · 신청은 그대로 되게 오류는 삼킨다.
    /// </summary>
    public async Task NotifyAsync(string actor, string label, string body, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = systemDb.Create(actor);
            if (await FeedbackTargetAsync(ctx, ct) is not { } to) return;
            ctx.Messages.Add(new Message { Kind = MessageKinds.System, SenderLabel = label, RecipientUserName = to, Body = body.Length > MaxLength ? body[..MaxLength] : body, SentUtc = Now });
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is System.Data.Common.DbException or InvalidOperationException or DbUpdateException) { }
    }

    /// <summary>
    /// 부서 알림(2026-11-20 사용자 "승인 대신 통보 쪽지"): 그 부서 관리자들과 플랫폼 관리자(superadmin)에게 쪽지 한 통씩. 보낸 사람 = label, 답장 없음.
    /// 알림이 실패해도 참여 · 연결은 그대로 되게 오류는 삼킨다.
    /// </summary>
    public async Task NotifyDeptAsync(string actor, int tenantId, string label, string body, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = systemDb.Create(actor);
            var adminIds = await DeptMembership.AdminIdsAsync(ctx, ct);
            var to = await (from d in ctx.DeptRoles join u in ctx.Users on d.UserId equals u.Id
                            where d.TenantId == tenantId && adminIds.Contains(u.Id) select u.UserName!).ToListAsync(ct);
            if (await FeedbackTargetAsync(ctx, ct) is { } boss) to.Add(boss);
            var text = body.Length > MaxLength ? body[..MaxLength] : body;
            foreach (var n in to.Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !n.Equals(actor, StringComparison.OrdinalIgnoreCase)))
                ctx.Messages.Add(new Message { Kind = MessageKinds.System, SenderLabel = label, RecipientUserName = n, Body = text, SentUtc = Now });
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is System.Data.Common.DbException or InvalidOperationException or DbUpdateException) { }
    }

    /// <summary>한 사람에게 자동 알림 쪽지 한 통(예: 탈퇴 반려). 실패해도 오류는 삼킨다.</summary>
    public async Task SendSystemAsync(string to, string label, string body, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = systemDb.Create("system");
            ctx.Messages.Add(new Message { Kind = MessageKinds.System, SenderLabel = label, RecipientUserName = to, Body = body.Length > MaxLength ? body[..MaxLength] : body, SentUtc = Now });
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is System.Data.Common.DbException or InvalidOperationException or DbUpdateException) { }
    }

    /// <summary>
    /// 개선 · 불편 접수(R-326): superadmin 에게. 익명이면 보낸 계정 · 이름 · 역할을 남기지 않고 지금 부서 이름만 남긴다(답장 불가).
    /// </summary>
    public async Task<MsgError> FeedbackAsync(string userName, int tenantId, string? body, bool anonymous, CancellationToken ct = default)
    {
        if (Check(body) is var e and not MsgError.None) return e;
        await using var ctx = systemDb.Create(userName);
        var to = await FeedbackTargetAsync(ctx, ct);
        if (to is null) return MsgError.NoRecipient;
        // 보내는 부서는 내 소속 부서만(남의 부서 이름으로 익명 쪽지를 보내지 못하게)
        var member = await (from d in ctx.DeptRoles.IgnoreQueryFilters() join u in ctx.Users on d.UserId equals u.Id where u.UserName == userName && d.TenantId == tenantId select d.Id).AnyAsync(ct);
        var t = member ? await ctx.Tenants.AsNoTracking().Where(x => x.Id == tenantId).Select(x => new { x.Name, x.IsLobby }).FirstOrDefaultAsync(ct) : null;
        var dept = t is null ? null : ShortDept(t.Name, t.IsLobby);
        string? label;
        if (anonymous) label = dept;
        else label = (await WhoAsync(ctx, [userName], ct)).GetValueOrDefault(userName).Where ?? dept;
        ctx.Messages.Add(new Message
        {
            Kind = MessageKinds.Feedback, SenderUserName = anonymous ? null : userName, SenderTenantId = t is null ? null : tenantId, SenderLabel = label,
            RecipientUserName = to, Body = body!.Trim(), SentUtc = Now,
        });
        await ctx.SaveChangesAsync(ct);
        return MsgError.None;
    }

    /// <summary>전체 쪽지(R-327): 플랫폼 관리자만. 한 줄 + 대상 부서(없으면 모든 사용자). 나중에 들어온 사람에게도 보인다.</summary>
    public async Task<MsgError> BroadcastAsync(string actor, IReadOnlyCollection<int> tenantIds, string? body, CancellationToken ct = default)
    {
        if (Check(body) is var e and not MsgError.None) return e;
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) return MsgError.NotAllowed;
        var m = new Message { Kind = MessageKinds.Broadcast, SenderUserName = actor, Body = body!.Trim(), SentUtc = Now };
        ctx.Messages.Add(m);
        await ctx.SaveChangesAsync(ct);
        foreach (var tid in tenantIds.Distinct()) ctx.MessageTargets.Add(new MessageTarget { MessageId = m.Id, TenantId = tid });
        await ctx.SaveChangesAsync(ct);
        return MsgError.None;
    }

    /// <summary>
    /// 단체쪽지를 보낼 수 있는 부서(R-359, 2026-11-21 사용자): 그 부서에서 교사 · 임원(부서 관리자 포함) 역할인 곳. 임시 부서 · 쉬는 부서는 빼고, 받을 사람 수와 함께.
    /// 학생 · 학부모 · 기타는 없다(받기와 답장만).
    /// </summary>
    public async Task<List<(int Id, string Name, int Count)>> GroupDeptsAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        var rows = await (from d in ctx.DeptRoles.IgnoreQueryFilters() join u in ctx.Users on d.UserId equals u.Id join t in ctx.Tenants on d.TenantId equals t.Id
                          where u.UserName == userName && !t.IsLobby && t.Status == TenantStatus.Active
                          select new { t.Id, t.Name, d.RoleCode }).ToListAsync(ct);
        var list = new List<(int, string, int)>();
        foreach (var r in rows.Where(r => UserRole.KindOf(r.RoleCode) is UserRoleKind.Teacher or UserRoleKind.Officer).DistinctBy(r => r.Id).OrderBy(r => DeptOrder.Key(r.Name)))
            list.Add((r.Id, ShortDept(r.Name, false), await AudienceCountAsync(ctx, [r.Id], userName, ct)));
        return list;
    }

    /// <summary>단체쪽지(R-359): 그 부서 사람 모두에게 한 줄(전체 쪽지와 같은 모양, 대상 = 그 부서). 그 부서의 교사 · 임원만. 받은 사람의 [답장]은 보낸 사람에게만.</summary>
    public async Task<MsgError> GroupAsync(string actor, int tenantId, string? body, CancellationToken ct = default)
    {
        if (Check(body) is var e and not MsgError.None) return e;
        if ((await GroupDeptsAsync(actor, ct)).All(d => d.Id != tenantId)) return MsgError.NotAllowed;
        await using var ctx = systemDb.Create(actor);
        var who = await WhoAsync(ctx, [actor], ct);
        var m = new Message { Kind = MessageKinds.Broadcast, SenderUserName = actor, SenderTenantId = tenantId, SenderLabel = who.GetValueOrDefault(actor).Where, Body = body!.Trim(), SentUtc = Now };
        ctx.Messages.Add(m);
        await ctx.SaveChangesAsync(ct);
        ctx.MessageTargets.Add(new MessageTarget { MessageId = m.Id, TenantId = tenantId });
        await ctx.SaveChangesAsync(ct);
        return MsgError.None;
    }

    /// <summary>[보내기 취소](R-327 · R-359): 전체 쪽지 · 단체쪽지 — 플랫폼 관리자, 또는 보낸 사람 본인. 모두의 쪽지함에서 사라진다(이미 온 답장은 남는다).</summary>
    public async Task<MsgError> CancelAsync(string actor, long id, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var m = await ctx.Messages.SingleOrDefaultAsync(x => x.Id == id && x.Kind == MessageKinds.Broadcast, ct);
        if (m is null) return MsgError.NotFound;
        // 플랫폼 관리자는 모든 전체 쪽지, 선생님은 내가 보낸 단체쪽지만(R-359)
        if (m.SenderUserName != actor && !await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) return MsgError.NotAllowed;
        m.CanceledUtc ??= Now;
        await ctx.SaveChangesAsync(ct);
        return MsgError.None;
    }

    /// <summary>지우기(R-325): 내 쪽지함에서만. 상대 쪽지함에는 남는다. 전체 쪽지는 내 받은 기록에 지운 때를 남긴다.</summary>
    public async Task<MsgError> DeleteAsync(string userName, long id, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        var m = await ctx.Messages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return MsgError.NotFound;
        if (m.SenderUserName == userName) m.SenderDeleted = true;
        else if (m.RecipientUserName == userName) m.RecipientDeleted = true;
        else if (m.Kind == MessageKinds.Broadcast && await Broadcasts(ctx, userName).AnyAsync(x => x.Id == id, ct))
        {
            var r = await ctx.MessageReceipts.SingleOrDefaultAsync(x => x.MessageId == id && x.UserName == userName, ct);
            if (r is null) ctx.MessageReceipts.Add(new MessageReceipt { MessageId = id, UserName = userName, ReadUtc = Now, DeletedUtc = Now });
            else r.DeletedUtc ??= Now;
        }
        else return MsgError.NotFound;
        await ctx.SaveChangesAsync(ct);
        return MsgError.None;
    }

    /// <summary>받는 사람 찾기(R-328): 이름에 q 가 든 계정 — 이름 · 부서 · 역할만. 나는 빼고, 20명까지.</summary>
    public async Task<List<MsgPerson>> SearchAsync(string userName, string? q, CancellationToken ct = default)
    {
        var key = new string((q ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (key.Length == 0) return [];
        await using var ctx = systemDb.Create(userName);
        var hits = (await ctx.Users.AsNoTracking().Where(u => u.UserName != userName).Select(u => new { u.UserName, u.LastName, u.FirstName }).ToListAsync(ct))
            .Where(u => AppUser.FullName(u.LastName, u.FirstName).Replace(" ", "").Contains(key, StringComparison.Ordinal)).Take(20).ToList();
        var who = await WhoAsync(ctx, hits.Select(h => h.UserName!), ct);
        return hits.Select(h => who.TryGetValue(h.UserName!, out var w) ? new MsgPerson(h.UserName!, w.Name, w.Where ?? (w.Name == PlatformName ? "플랫폼" : "소속 없음")) : null)
            .OfType<MsgPerson>().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>받는 사람 한 명(답장 · 고른 뒤 표시).</summary>
    public async Task<MsgPerson?> PersonAsync(string userName, string to, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        var who = await WhoAsync(ctx, [to], ct);
        return who.TryGetValue(to, out var w) ? new MsgPerson(to, w.Name, w.Where ?? (w.Name == PlatformName ? "플랫폼" : "소속 없음")) : null;
    }

    public static string Text(MsgError e) => e switch
    {
        MsgError.Empty => "내용을 써 주세요.",
        MsgError.TooLong => $"쪽지는 {MaxLength:N0}자까지 써요.",
        MsgError.NoRecipient => "받는 사람을 골라 주세요.",
        MsgError.NotFound => "쪽지를 찾을 수 없어요.",
        MsgError.NotAllowed => "플랫폼 관리자만 할 수 있어요.",
        _ => "",
    };
}
