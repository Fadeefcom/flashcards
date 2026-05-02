using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RecallCraft.Application.Abstractions;
using RecallCraft.Application.Services;
using RecallCraft.Domain.Services;
using RecallCraft.Pwa;
using RecallCraft.Pwa.Services;
using RecallCraft.Pwa.ViewModels;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ILocalStorageService, BrowserLocalStorageService>();
builder.Services.AddScoped<ISecureCredentialStore, BrowserCredentialStore>();
builder.Services.AddScoped<IConnectivityService, BrowserConnectivityService>();
builder.Services.AddScoped<ICloudConfigurationStore, BrowserCloudConfigurationStore>();
builder.Services.AddScoped<ICloudSyncClient, AzureFunctionCloudSyncClient>();
builder.Services.AddScoped<IAudioFileStore, BrowserAudioFileStore>();
builder.Services.AddScoped<IAudioPlayback, BrowserAudioPlayback>();
builder.Services.AddScoped<SpacedRepetitionScheduler>();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<StudyService>();
builder.Services.AddScoped<AudioService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped(sp => new SyncScheduler(sp.GetRequiredService<SyncService>(), TimeSpan.FromMinutes(15)));
builder.Services.AddTransient<ModuleViewModel>();

var host = builder.Build();
await host.Services.GetRequiredService<ILocalStorageService>().InitializeAsync(CancellationToken.None);
host.Services.GetRequiredService<SyncScheduler>().Start();
await host.RunAsync();
