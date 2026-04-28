using Yottacast.Core.Platform;

namespace Yottacast.Core.Tests.Fakes;

internal sealed class TrackingPlatformProvider : FakePlatformProvider {
    public List<string> LaunchedUrls { get; } = new();
    public TrackingPlatformProvider() : base([]) { }
    public override void LaunchUrl(string url) => LaunchedUrls.Add(url);
}
