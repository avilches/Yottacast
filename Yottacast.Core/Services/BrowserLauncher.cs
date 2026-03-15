using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Yottacast.Services;

public static class BrowserLauncher
{
    /// <summary>Opens <paramref name="url"/> in the given browser.</summary>
    public static void OpenUrl(string url, BrowserInfo browser)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            OpenUrlMac(url, browser);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            OpenUrlWindows(url, browser);
    }

    private static void OpenUrlMac(string url, BrowserInfo browser)
    {
        // `open -a "App Name" "url"` launches the app and passes the URL as an argument.
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            ArgumentList = { "-a", browser.Name, url },
            UseShellExecute = false,
        });
    }

    private static void OpenUrlWindows(string url, BrowserInfo browser)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            ArgumentList = { url },
            UseShellExecute = false,
        });
    }
}
