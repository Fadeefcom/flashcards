using RecallCraft.Application.Abstractions;
using RecallCraft.Domain.Entities;
using RecallCraft.Domain.Enums;
using RecallCraft.Domain.Services;

namespace RecallCraft.Application.Services;

public sealed class StudyService(ILocalStorageService storage, SpacedRepetitionScheduler scheduler)
{
    public async Task<Card?> ReviewAsync(Guid cardId, ReviewGrade grade, CancellationToken cancellationToken)
    {
        var card = await storage.GetCardAsync(cardId, cancellationToken);
        if (card is null)
        {
            return null;
        }

        scheduler.ApplyReview(card, grade, DateTimeOffset.UtcNow);
        await storage.UpsertCardAsync(card, cancellationToken);
        return card;
    }
}
