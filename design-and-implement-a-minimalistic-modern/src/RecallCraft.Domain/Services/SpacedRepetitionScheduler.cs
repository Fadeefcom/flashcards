using RecallCraft.Domain.Entities;
using RecallCraft.Domain.Enums;

namespace RecallCraft.Domain.Services;

public sealed class SpacedRepetitionScheduler
{
    public Card ApplyReview(Card card, ReviewGrade grade, DateTimeOffset reviewedAt)
    {
        var quality = grade switch
        {
            ReviewGrade.Again => 1,
            ReviewGrade.Hard => 3,
            ReviewGrade.Good => 4,
            ReviewGrade.Easy => 5,
            _ => 4
        };

        var ease = Math.Max(1.3, card.EaseFactor + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02)));
        var interval = card.Interval;

        if (grade == ReviewGrade.Again)
        {
            interval = 0;
        }
        else if (interval <= 0)
        {
            interval = grade == ReviewGrade.Easy ? 4 : 1;
        }
        else if (interval == 1)
        {
            interval = grade == ReviewGrade.Hard ? 2 : grade == ReviewGrade.Easy ? 6 : 3;
        }
        else
        {
            var multiplier = grade switch
            {
                ReviewGrade.Hard => 1.2,
                ReviewGrade.Easy => ease * 1.3,
                _ => ease
            };

            interval = Math.Max(1, (int)Math.Round(interval * multiplier));
        }

        card.EaseFactor = ease;
        card.Interval = interval;
        card.LastReviewed = reviewedAt;
        card.MarkDirty();
        return card;
    }
}
