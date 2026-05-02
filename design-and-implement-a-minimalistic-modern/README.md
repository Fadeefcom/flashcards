# Flashcards

Minimal offline-first study app implemented as a Blazor WebAssembly Progressive Web App.

## Structure

- `src/RecallCraft.Domain` - entities, enums, and business logic such as spaced repetition.
- `src/RecallCraft.Application` - use cases, service interfaces, sync orchestration, audio flow, and `SyncScheduler`.
- `src/RecallCraft.Pwa` - Blazor WebAssembly PWA, browser storage implementations, fake cloud API, and UI.
- `src/Flashcards.Functions` - Azure Functions isolated-worker backend for speech generation and module card synchronization.

## Architecture Notes

- Local-first writes go to browser storage first and enqueue `SyncQueueItem` records for later sync.
- Startup sync is owned by `SyncScheduler.Start()`, with `ForceSync()` for manual sync and exponential backoff after failures.
- The cloud layer calls Azure Functions through `ICloudSyncClient`; the user-provided app key is sent as `x-functions-key`.
- First launch stores the Azure Function URL and function key in browser local storage for the PWA scenario.
- New cards start with `AudioStatus.Pending`; sync requests real Azure Speech audio through the Function app, downloads bytes, caches locally, and updates the card to `Ready`.
- Modules are queried ordered by `NextReviewDate ASC`, so the nearest review appears first.

## Run

```powershell
dotnet build Flashcards.sln
```

```powershell
dotnet run --project src\RecallCraft.Pwa\Flashcards.Pwa.csproj --urls http://0.0.0.0:5173
```

Open `http://localhost:5173` on this computer. From an iPhone on the same Wi-Fi, open `http://192.168.1.239:5173`.

On iPhone, open the URL in Safari, tap Share, then tap **Add to Home Screen**.

For full PWA install/offline service-worker behavior on iPhone, host the published app over HTTPS:

```powershell
dotnet publish src\RecallCraft.Pwa\Flashcards.Pwa.csproj -c Release
```

## Azure Functions

Configure `src\Flashcards.Functions\local.settings.json` for local development:

- `AzureWebJobsStorage` - storage account or Azurite connection string.
- `SpeechKey` - Azure AI Speech key.
- `SpeechRegion` - speech region, default `francecentral`.
- `ContainerName` - blob container, default `flashcards`.

Run locally with Azure Functions Core Tools:

```powershell
cd src\Flashcards.Functions
func start
```

Endpoints:

- `POST /api/GenerateSpeech` with `{ "text": "...", "cardId": "..." }` returns `audio/mpeg` and caches `audio/{cardId}.mp3` in blob storage.
- `GET /api/modules/sync?moduleId={id}` returns the full remote card list for a module.
- `POST /api/modules/sync` with `{ "moduleId": "...", "lastKnownUpdate": "...", "localCards": [...] }` merges newer local cards, returns changed cards, and includes `AudioStatus`, `AudioUrl`, and `AudioUpdatedAt` when audio exists.
