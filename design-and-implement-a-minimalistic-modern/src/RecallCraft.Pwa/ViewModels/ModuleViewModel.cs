using RecallCraft.Application.Services;
using RecallCraft.Domain.Entities;
using RecallCraft.Domain.Enums;

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
    private List<Card> _allCards = [];

    public Module? Module { get; private set; }
    public List<Card> VisibleCards { get; } = [];
    public ModuleSyncStatus SyncStatus { get; private set; } = ModuleSyncStatus.NotSynced;
    public LearningMode LearningMode { get; private set; }
    public bool FlipLearningSides { get; set; }
    public bool IsAnswerVisible { get; private set; }
    public string WritingAnswer { get; set; } = string.Empty;
    public string WritingResult { get; private set; } = string.Empty;
    public Card? CurrentLearningCard { get; private set; }

    public int TotalWordsCount => _allCards.Sum(card => CountWords(card.FrontText) + CountWords(card.BackText));

    public int LearningProgress
    {
        get
        {
            if (_allCards.Count == 0)
            {
                return 0;
            }

            var reviewed = _allCards.Count(card => card.LastReviewed is not null && card.Interval > 0);
            return (int)Math.Round(reviewed * 100d / _allCards.Count);
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
        CurrentLearningCard = _allCards.FirstOrDefault();
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

        await study.ReviewAsync(CurrentLearningCard.Id, grade, cancellationToken);
        _allCards.Remove(CurrentLearningCard);
        VisibleCards.Remove(CurrentLearningCard);
        CurrentLearningCard = _allCards.FirstOrDefault();
        IsAnswerVisible = false;
        SyncStatus = ModuleSyncStatus.NotSynced;
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

    private static int CountWords(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private static string Normalize(string value) => string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
