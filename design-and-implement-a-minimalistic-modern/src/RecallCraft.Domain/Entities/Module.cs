namespace RecallCraft.Domain.Entities;

public sealed class Module : EntityBase
{
    public Guid FolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset NextReviewDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LearningCompletedAt { get; set; }
}
