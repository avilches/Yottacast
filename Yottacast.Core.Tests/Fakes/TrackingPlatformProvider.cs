using Yottacast.Core.Platform;

namespace Yottacast.Core.Tests.Fakes;

internal sealed class TrackingPlatformProvider : FakePlatformProvider {
    public List<string> LaunchedApps { get; } = new();
    public TrackingPlatformProvider() : base([]) { }
    public override void LaunchApp(string path) => LaunchedApps.Add(path);
}
