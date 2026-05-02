using RecallCraft.Domain.Enums;

namespace RecallCraft.Domain.Entities;

public sealed class SyncQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    public SyncEntityType EntityType { get; set; }
    public SyncOperation Operation { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
