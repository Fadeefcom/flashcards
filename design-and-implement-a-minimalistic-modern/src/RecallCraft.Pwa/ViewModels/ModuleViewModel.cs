using RecallCraft.Application.Services;
using RecallCraft.Domain.Entities;
using RecallCraft.Domain.Enums;
using RecallCraft.Domain.Services;

namespace RecallCraft.Pwa.ViewModels;

public enum ModuleSyncStatus
{
    NotSynced,
    Syncing,
    Synced,
    Error
}

public enum LearningMode
{
    None,
    Card,
    Writing
}

public sealed class ModuleViewModel(LibraryService library, StudyService study, AudioService audio, SyncScheduler syncScheduler)
{
    private const int PageSize = 30;
    private const int SessionMasteryTarget = 2;
    private List<Card> _allCards = [];
    private readonly List<Guid> _learningQueue = [];
    private readonly Dictionary<Guid, int> _sessionMastery = [];
    private readonly Random _random = new();

    public Module? Module { get; private set; }
    public List<Card> VisibleCards { get; } = [];
    public ModuleSyncStatus SyncStatus { get; private set; } = ModuleSyncStatus.NotSynced;
    public LearningMode LearningMode { get; private set; }
    public bool FlipLearningSides { get; set; }
    public bool IsAnswerVisible { get; private set; }
    public string WritingAnswer { get; set; } = string.Empty;
    public string WritingResult { get; private set; } = string.Empty;
    public Card? CurrentLearningCard { get; private set; }
    public int SessionTotalCount { get; private set; }
    public int SessionMasteredCount => _sessionMastery.Count(x => x.Value >= SessionMasteryTarget);
    public int SessionRemainingCount => _learningQueue.Count + (CurrentLearningCard is null ? 0 : 1);
    public bool HasAnyCards => _allCards.Any(card => !card.IsDeleted);

    public int TotalWordsCount => _allCards.Sum(card => CountWords(card.FrontText) + CountWords(card.BackText));

    public int LearningProgress
    {
        get
        {
            if (_allCards.Count == 0)
            {
                return 0;
            }

            var totalStage = _allCards.Sum(card => Math.Clamp(card.Interval, 0, SpacedRepetitionScheduler.MasteredStage));
            return (int)Math.Round(totalStage * 100d / (_allCards.Count * SpacedRepetitionScheduler.MasteredStage));
        }
    }

    public IReadOnlyList<LearningStageStat> LearningStageStats =>
        Enumerable.Range(0, SpacedRepetitionScheduler.MasteredStage + 1)
            .Select(stage => new LearningStageStat(stage, _allCards.Count(card => Math.Clamp(card.Interval, 0, SpacedRepetitionScheduler.MasteredStage) == stage)))
            .ToList();

    public string StageDateLabel
    {
        get
        {
            var state = GetModuleStageDateState();
            return state.Label;
        }
    }

    public string StageDateText
    {
        get
        {
            var state = GetModuleStageDateState();
            return state.Date is null ? "-" : state.Date.Value.LocalDateTime.ToString("MMM d, HH:mm");
        }
    }

    public bool HasMoreCards => VisibleCards.Count < _allCards.Count;

    public async Task LoadAsync(Guid moduleId, CancellationToken cancellationToken)
    {
        Module = await library.GetModuleAsync(moduleId, cancellationToken);
        if (Module is null)
        {
            return;
        }

        _allCards = (await library.GetCardsAsync(Module.Id, cancellationToken)).ToList();
        VisibleCards.Clear();
        LoadMoreCards();
        CurrentLearningCard = _allCards.FirstOrDefault();
    }

    public void LoadMoreCards()
    {
        foreach (var card in _allCards.Skip(VisibleCards.Count).Take(PageSize))
        {
            VisibleCards.Add(card);
        }
    }

    public async Task RenameModuleAsync(string name, CancellationToken cancellationToken)
    {
        if (Module is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Module.Name = name.Trim();
        await library.SaveModuleAsync(Module, cancellationToken);
        SyncStatus = ModuleSyncStatus.NotSynced;
    }

    public async Task<Card?> CreateCardAsync(CancellationToken cancellationToken)
    {
        if (Module is null)
        {
            return null;
        }

        var card = await library.CreateCardAsync(Module.Id, string.Empty, string.Empty, cancellationToken);
        _allCards.Add(card);
        VisibleCards.Add(card);
        SyncStatus = ModuleSyncStatus.NotSynced;
        return card;
    }

    public async Task UpdateCardAsync(Card card, CancellationToken cancellationToken)
    {
        await library.SaveCardAsync(card, cancellationToken);
        SyncStatus = ModuleSyncStatus.NotSynced;
    }

    public async Task ForceSyncAsync(CancellationToken cancellationToken)
    {
        SyncStatus = ModuleSyncStatus.Syncing;
        var result = await syncScheduler.ForceSync(cancellationToken);
        SyncStatus = result.Success ? ModuleSyncStatus.Synced : ModuleSyncStatus.Error;
        if (Module is not null)
        {
            await LoadAsync(Module.Id, cancellationToken);
        }
    }

    public void StartLearning(LearningMode mode)
    {
        LearningMode = mode;
        StartSessionQueue();
        IsAnswerVisible = false;
        WritingAnswer = string.Empty;
        WritingResult = string.Empty;
    }

    public void StopLearning()
    {
        LearningMode = LearningMode.None;
        IsAnswerVisible = false;
        WritingAnswer = string.Empty;
        WritingResult = string.Empty;
        CurrentLearningCard = null;
        _learningQueue.Clear();
        _sessionMastery.Clear();
        SessionTotalCount = 0;
    }

    public async Task DeleteModuleAsync(CancellationToken cancellationToken)
    {
        if (Module is null)
        {
            return;
        }

        await library.DeleteAsync(Module.Id, SyncEntityType.Module, cancellationToken);

        Module = null;
    }

    public void FlipCard() => IsAnswerVisible = !IsAnswerVisible;

    public async Task PlayAudioAsync(CancellationToken cancellationToken)
    {
        if (CurrentLearningCard is not null)
        {
            await audio.PlayAudioAsync(CurrentLearningCard.Id, cancellationToken);
        }
    }

    public async Task GradeCurrentAsync(ReviewGrade grade, CancellationToken cancellationToken)
    {
        if (CurrentLearningCard is null)
        {
            return;
        }

        var reviewedCardId = CurrentLearningCard.Id;
        var score = _sessionMastery.GetValueOrDefault(reviewedCardId);
        score = grade switch
        {
            ReviewGrade.Easy => SessionMasteryTarget,
            ReviewGrade.Good => Math.Min(SessionMasteryTarget, score + 1),
            ReviewGrade.Hard => 0,
            ReviewGrade.Again => 0,
            _ => score
        };

        _sessionMastery[reviewedCardId] = score;
        if (score >= SessionMasteryTarget)
        {
            var finalGrade = grade == ReviewGrade.Easy ? ReviewGrade.Easy : ReviewGrade.Good;
            var updatedCard = await study.ReviewAsync(reviewedCardId, finalGrade, cancellationToken);
            if (updatedCard is not null)
            {
                ReplaceCard(updatedCard);
            }
        }
        else
        {
            ReinsertForPractice(reviewedCardId, grade);
        }

        CurrentLearningCard = DequeueNextLearningCard();
        IsAnswerVisible = false;
        WritingAnswer = string.Empty;

        await CompleteModuleIfMasteredAsync(cancellationToken);
    }

    public async Task SubmitWritingAsync(CancellationToken cancellationToken)
    {
        if (CurrentLearningCard is null)
        {
            return;
        }

        var expected = FlipLearningSides ? CurrentLearningCard.FrontText : CurrentLearningCard.BackText;
        var correct = string.Equals(Normalize(WritingAnswer), Normalize(expected), StringComparison.OrdinalIgnoreCase);
        WritingResult = correct ? "Correct" : $"Answer: {expected}";
        await GradeCurrentAsync(correct ? ReviewGrade.Good : ReviewGrade.Again, cancellationToken);
    }

    public string PromptText(Card card) => FlipLearningSides ? card.BackText : card.FrontText;

    public string AnswerText(Card card) => FlipLearningSides ? card.FrontText : card.BackText;

    public string SessionCompleteTitle =>
        HasAnyCards ? "Session complete" : "No cards yet";

    public string SessionCompleteText =>
        HasAnyCards
            ? "Early practice is saved locally, but stages advance only on schedule."
            : "Add cards to start learning.";

    private static int CountWords(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private static string Normalize(string value) => string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private void StartSessionQueue()
    {
        _learningQueue.Clear();
        _sessionMastery.Clear();

        var cards = _allCards
            .Where(card => !card.IsDeleted)
            .Where(card => card.MasteredAt is null)
            .OrderBy(_ => _random.Next())
            .ToList();
        foreach (var card in cards)
        {
            _learningQueue.Add(card.Id);
            _sessionMastery[card.Id] = 0;
        }

        SessionTotalCount = cards.Count;
        CurrentLearningCard = DequeueNextLearningCard();
    }

    private Card? DequeueNextLearningCard()
    {
        while (_learningQueue.Count > 0)
        {
            var cardId = _learningQueue[0];
            _learningQueue.RemoveAt(0);
            var card = _allCards.FirstOrDefault(x => x.Id == cardId && !x.IsDeleted);
            if (card is not null)
            {
                return card;
            }
        }

        return null;
    }

    private void ReinsertForPractice(Guid cardId, ReviewGrade grade)
    {
        if (_learningQueue.Count == 0)
        {
            _learningQueue.Add(cardId);
            return;
        }

        if (grade is ReviewGrade.Again or ReviewGrade.Hard)
        {
            var start = _learningQueue.Count / 2;
            var index = _random.Next(start, _learningQueue.Count + 1);
            _learningQueue.Insert(index, cardId);
            return;
        }

        _learningQueue.Add(cardId);
    }

    private void ReplaceCard(Card updatedCard)
    {
        ReplaceInList(_allCards, updatedCard);
        ReplaceInList(VisibleCards, updatedCard);
    }

    private static void ReplaceInList(List<Card> cards, Card updatedCard)
    {
        var index = cards.FindIndex(card => card.Id == updatedCard.Id);
        if (index >= 0)
        {
            cards[index] = updatedCard;
        }
    }

    private async Task CompleteModuleIfMasteredAsync(CancellationToken cancellationToken)
    {
        if (Module is null || _allCards.Count == 0)
        {
            return;
        }

        if (_allCards.All(card => card.Interval >= SpacedRepetitionScheduler.MasteredStage))
        {
            Module.LearningCompletedAt ??= DateTimeOffset.UtcNow;
            await library.SaveModuleLearningProgressAsync(Module, cancellationToken);
        }
    }

    private (string Label, DateTimeOffset? Date) GetModuleStageDateState()
    {
        var activeCards = _allCards
            .Where(card => !card.IsDeleted && card.MasteredAt is null)
            .ToList();

        if (activeCards.Count == 0)
        {
            return Module?.LearningCompletedAt is null
                ? ("Stage", null)
                : ("Completed", Module.LearningCompletedAt);
        }

        var now = DateTimeOffset.UtcNow;
        var dueCards = activeCards
            .Where(card => card.NextLearningReviewAt is null || card.NextLearningReviewAt <= now)
            .ToList();

        if (dueCards.Count > 0)
        {
            var dueBy = dueCards
                .Select(card =>
                {
                    var openedAt = card.NextLearningReviewAt ?? card.CreatedAt;
                    return openedAt + SpacedRepetitionScheduler.GetCurrentStageDeadlineWindow(card.Interval);
                })
                .Min();

            return ("Due By", dueBy);
        }

        return (
            "Next Opens",
            activeCards
                .Where(card => card.NextLearningReviewAt is not null)
                .Min(card => card.NextLearningReviewAt));
    }
}

public sealed record LearningStageStat(int Stage, int Count);
