using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>쪽지 종류(ADR 0112).</summary>
public static class MessageKinds
{
    /// <summary>사람끼리 1:1.</summary>
    public const string Direct = "dm";
    /// <summary>개선 · 불편 접수대 → superadmin(익명이면 보낸 사람 칸이 빈다).</summary>
    public const string Feedback = "feedback";
    /// <summary>플랫폼 관리자 전체 쪽지 — 한 줄만, 받는 사람은 대상(전체 · 부서)으로 참조.</summary>
    public const string Broadcast = "broadcast";
}

/// <summary>
/// 쪽지 한 통(ADR 0112, R-325 ~ R-328). 계정끼리 주고받는 것이라 부서(영역)에 묶이지 않는다 — 필사 기록(CopyVerse)처럼 계정 단위.
/// 보낸 사람이 있던 부서는 표시용으로만 남긴다(SenderTenantId · SenderLabel). 익명 개선 쪽지는 SenderUserName · 라벨의 이름 · 역할이 비고 부서만 남는다.
/// 전체 쪽지(Broadcast)는 받는 사람 칸이 비고 MessageTarget(없으면 모든 사용자)으로 받는 사람을 정한다 — 100명이어도 한 줄.
/// </summary>
public sealed class Message
{
    public long Id { get; set; }
    public required string Kind { get; set; }
    /// <summary>보낸 계정. 익명 개선 쪽지는 null(신상정보를 남기지 않는다).</summary>
    public string? SenderUserName { get; set; }
    /// <summary>보낼 때 있던 부서(표시용, 익명도 남긴다).</summary>
    public int? SenderTenantId { get; set; }
    /// <summary>보낼 때의 "부서 · 역할"(익명은 부서만).</summary>
    public string? SenderLabel { get; set; }
    /// <summary>받는 계정. 전체 쪽지는 null.</summary>
    public string? RecipientUserName { get; set; }
    public required string Body { get; set; }
    public DateTime SentUtc { get; set; }
    /// <summary>답장이면 원래 쪽지.</summary>
    public long? ReplyToId { get; set; }
    /// <summary>1:1 쪽지를 받은 사람이 연 때. 전체 쪽지는 MessageReceipt.</summary>
    public DateTime? ReadUtc { get; set; }
    public bool SenderDeleted { get; set; }
    public bool RecipientDeleted { get; set; }
    /// <summary>전체 쪽지 [보내기 취소] — 모두에게서 사라진다.</summary>
    public DateTime? CanceledUtc { get; set; }
}

/// <summary>전체 쪽지의 대상 부서. 한 줄도 없으면 모든 사용자.</summary>
public sealed class MessageTarget
{
    public long MessageId { get; set; }
    public int TenantId { get; set; }
}

/// <summary>전체 쪽지를 받은 사람이 열거나 지웠을 때만 생기는 한 줄(받는 사람마다 미리 만들지 않는다).</summary>
public sealed class MessageReceipt
{
    public long MessageId { get; set; }
    public required string UserName { get; set; }
    public DateTime? ReadUtc { get; set; }
    public DateTime? DeletedUtc { get; set; }
}

internal sealed class MessageConfig : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> e)
    {
        e.ToTable("Message");
        e.HasKey(x => x.Id);
        e.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false);
        e.Property(x => x.SenderUserName).HasMaxLength(256);
        e.Property(x => x.SenderLabel).HasMaxLength(100);
        e.Property(x => x.RecipientUserName).HasMaxLength(256);
        e.Property(x => x.Body).HasMaxLength(1000);
        e.Property(x => x.SentUtc).HasPrecision(3);
        e.Property(x => x.ReadUtc).HasPrecision(3);
        e.Property(x => x.CanceledUtc).HasPrecision(3);
        e.HasIndex(x => new { x.RecipientUserName, x.SentUtc });
        e.HasIndex(x => new { x.SenderUserName, x.SentUtc });
        e.HasIndex(x => new { x.Kind, x.SentUtc });
    }
}

internal sealed class MessageTargetConfig : IEntityTypeConfiguration<MessageTarget>
{
    public void Configure(EntityTypeBuilder<MessageTarget> e)
    {
        e.ToTable("MessageTarget");
        e.HasKey(x => new { x.MessageId, x.TenantId });
        e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class MessageReceiptConfig : IEntityTypeConfiguration<MessageReceipt>
{
    public void Configure(EntityTypeBuilder<MessageReceipt> e)
    {
        e.ToTable("MessageReceipt");
        e.HasKey(x => new { x.MessageId, x.UserName });
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.ReadUtc).HasPrecision(3);
        e.Property(x => x.DeletedUtc).HasPrecision(3);
        e.HasIndex(x => x.UserName);
        e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}
