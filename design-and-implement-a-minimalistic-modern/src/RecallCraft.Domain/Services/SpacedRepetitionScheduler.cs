using RecallCraft.Domain.Entities;
using RecallCraft.Domain.Enums;

namespace RecallCraft.Domain.Services;

public sealed class SpacedRepetitionScheduler
{
    public const int MasteredStage = 5;

    public Card ApplyReview(Card card, ReviewGrade grade, DateTimeOffset reviewedAt)
    {
        var stage = Math.Clamp(card.Interval, 0, MasteredStage);
        var canAdvanceStage = card.NextLearningReviewAt is null || card.NextLearningReviewAt <= reviewedAt;

        stage = grade switch
        {
            ReviewGrade.Again => Math.Max(0, stage - 1),
            ReviewGrade.Hard => stage,
            ReviewGrade.Good when canAdvanceStage => Math.Min(MasteredStage, stage + 1),
            ReviewGrade.Easy when canAdvanceStage => Math.Min(MasteredStage, stage + 1),
            _ => stage
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
        card.NextLearningReviewAt = reviewedAt + GetNextStageDelay(stage, grade);
        card.MasteredAt = stage >= MasteredStage ? reviewedAt : null;
        return card;
    }

    public static TimeSpan GetNextStageDelay(int completedStage, ReviewGrade grade)
    {
        if (grade == ReviewGrade.Again)
        {
            return TimeSpan.FromMinutes(30);
        }

        return completedStage switch
        {
            0 => TimeSpan.FromMinutes(30),
            1 => TimeSpan.FromDays(1),
            2 => TimeSpan.FromDays(4),
            3 => TimeSpan.FromDays(14),
            4 => TimeSpan.FromDays(45),
            _ => TimeSpan.Zero
        };
    }

    public static TimeSpan GetCurrentStageDeadlineWindow(int stage)
    {
        return Math.Clamp(stage, 0, MasteredStage) switch
        {
            0 => TimeSpan.FromHours(2),
            1 => TimeSpan.FromDays(1),
            2 => TimeSpan.FromDays(5),
            3 => TimeSpan.FromDays(14),
            4 => TimeSpan.FromDays(60),
            _ => TimeSpan.Zero
        };
    }
}
