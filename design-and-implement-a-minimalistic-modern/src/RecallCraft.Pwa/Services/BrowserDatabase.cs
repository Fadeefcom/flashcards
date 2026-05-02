using RecallCraft.Domain.Entities;

namespace RecallCraft.Pwa.Services;

public sealed class BrowserDatabase
{
    public List<Folder> Folders { get; set; } = [];
    public List<Module> Modules { get; set; } = [];
    public List<Card> Cards { get; set; } = [];
    public List<SyncQueueItem> SyncQueue { get; set; } = [];
    public DateTimeOffset? LastSyncAt { get; set; }
}
