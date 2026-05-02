using RecallCraft.Domain.Enums;

namespace RecallCraft.Domain.Entities;

public sealed class Card : EntityBase
{
    public Guid ModuleId { get; set; }
    public string FrontText { get; set; } = string.Empty;
    public string BackText { get; set; } = string.Empty;
    public AudioStatus AudioStatus { get; set; } = AudioStatus.None;
    public string? AudioLocalPath { get; set; }
    public DateTimeOffset? LastReviewed { get; set; }
    public int Interval { get; set; }
    public double EaseFactor { get; set; } = 2.5;
}
