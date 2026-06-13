using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Services;

public class PluginServiceTests {
    // BUG 2 regression: GetIcon must read _icons under the same lock that ReloadAsync uses
    // to mutate it. Without the lock, reading the Dictionary while another thread does
    // Clear() + repopulate (as ReloadAsync does) can throw InvalidOperationException or
    // return corrupted state. This test drives the private _icons field directly to mimic
    // a concurrent reload and asserts GetIcon never throws.
    [Fact]
    public void GetIcon_IsThreadSafeWhileIconsAreBeingReplaced() {
        var svc = new PluginService(NullLogger<PluginService>.Instance);
        var iconsField = typeof(PluginService).GetField(
            "_icons", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var icons = (Dictionary<string, byte[]?>)iconsField.GetValue(svc)!;

        using var cts = new CancellationTokenSource();
        Exception? failure = null;

        // Writer: repeatedly Clear() + repopulate under lock(_icons), like ReloadAsync.
        var writer = Task.Run(() => {
            try {
                while (!cts.IsCancellationRequested) {
                    lock (icons) {
                        icons.Clear();
                        for (var i = 0; i < 50; i++)
                            icons["id" + i] = [1, 2, 3];
                    }
                }
            } catch (Exception ex) {
                failure = ex;
            }
        });

        // Reader: hammer GetIcon from another thread.
        for (var i = 0; i < 100_000 && failure is null; i++) {
            _ = svc.GetIcon("id" + (i % 50));
        }

        cts.Cancel();
        writer.Wait();

        Assert.Null(failure);
    }
}
