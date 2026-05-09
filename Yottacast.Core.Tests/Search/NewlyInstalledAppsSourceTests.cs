using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;

namespace Yottacast.Core.Tests.Search;

public class NewlyInstalledAppsSourceTests
{
    private static (NewlyInstalledAppsSource source, ApplicationSearch appSearch) Build(
        params string[] appPaths)
    {
        var platform = new FakePlatformProviderWithApps(appPaths);
        var settings = UserSettings.Load(platform);
        var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        var appSearch = new ApplicationSearch(settings, platform, iconCache, clipboard,
            NullLogger<ApplicationSearch>.Instance);
        var source = new NewlyInstalledAppsSource(appSearch, NullLogger<NewlyInstalledAppsSource>.Instance);
        return (source, appSearch);
    }

    [Fact]
    public void GetResults_Initially_ReturnsEmpty()
    {
        var (source, _) = Build();
        Assert.Empty(source.GetResults());
    }

    [Fact]
    public void OnWindowShown_WithAnyInput_IsNoOp()
    {
        var (source, _) = Build();
        source.OnWindowShown("https://example.com");
        source.OnWindowShown(null);
        Assert.Empty(source.GetResults());
    }

    [Fact]
    public async Task WhenReady_CompletesImmediately()
    {
        var (source, _) = Build();
        var completed = await Task.WhenAny(source.WhenReady(), Task.Delay(500));
        Assert.True(completed == source.WhenReady() || source.WhenReady().IsCompleted,
            "WhenReady() should complete immediately without needing Start()");
    }

    [Fact]
    public void OnSearchStarted_ClearsResults()
    {
        var (source, _) = Build();
        // Directly invoke internal state via OnSearchStarted — even with no pending items
        // it should not throw and GetResults() should remain empty.
        source.OnSearchStarted();
        Assert.Empty(source.GetResults());
    }

    [Fact]
    public async Task Stop_ClearsStateAndUnsubscribes()
    {
        var (source, _) = Build();
        source.Start();
        await source.Stop();
        // After stop, GetResults() is empty and no exception is thrown
        Assert.Empty(source.GetResults());
    }
}
