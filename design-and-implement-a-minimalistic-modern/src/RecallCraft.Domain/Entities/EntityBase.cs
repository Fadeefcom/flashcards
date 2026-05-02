namespace RecallCraft.Domain.Entities;

public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    public void MarkDirty()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        IsDirty = true;
    }
}
