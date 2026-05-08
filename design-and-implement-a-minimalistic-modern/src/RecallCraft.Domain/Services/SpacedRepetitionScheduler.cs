using RecallCraft.Domain.Entities;
using RecallCraft.Domain.Enums;

namespace RecallCraft.Domain.Services;

public sealed class SpacedRepetitionScheduler
{
    public const int MasteredStage = 5;

    public Card ApplyReview(Card card, ReviewGrade grade, DateTimeOffset reviewedAt)
    {
        var stage = Math.Clamp(card.Interval, 0, MasteredStage);

        stage = grade switch
        {
            ReviewGrade.Again => Math.Max(0, stage - 1),
            ReviewGrade.Hard => Math.Max(1, stage),
            ReviewGrade.Good => Math.Min(MasteredStage, stage + 1),
            ReviewGrade.Easy => Math.Min(MasteredStage, stage + 2),
            _ => Math.Min(MasteredStage, stage + 1)
        };

        card.EaseFactor = grade switch
        {
            ReviewGrade.Again => Math.Max(1.3, card.EaseFactor - 0.2),
            ReviewGrade.Hard => Math.Max(1.3, card.EaseFactor - 0.05),
            ReviewGrade.Easy => Math.Min(3.0, card.EaseFactor + 0.1),
            _ => card.EaseFactor
        };

        card.Interval = stage;
        card.LastReviewed = reviewedAt;
        card.NextLearningReviewAt = reviewedAt + GetDelayForStage(stage, grade);
        card.MasteredAt = stage >= MasteredStage ? reviewedAt : null;
        return card;
    }

    private static TimeSpan GetDelayForStage(int stage, ReviewGrade grade)
    {
        if (grade == ReviewGrade.Again)
        {
            return TimeSpan.FromMinutes(30);
        }

        return stage switch
        {
            0 => TimeSpan.FromMinutes(30),
            1 => TimeSpan.FromHours(2),
            2 => TimeSpan.FromDays(1),
            3 => TimeSpan.FromDays(4),
            4 => TimeSpan.FromDays(14),
            _ => TimeSpan.FromDays(45)
        };
    }
}
