using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Yottacast.Core.Services;

public static class TerminalLauncher
{
    /// <summary>Opens the terminal and runs <paramref name="command"/> in a new window.</summary>
    public static void ExecuteCommand(string command, TerminalInfo terminal)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ExecuteCommandMac(command, terminal);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ExecuteCommandWindows(command, terminal);
    }

    private static void ExecuteCommandMac(string command, TerminalInfo terminal)
    {
        // Each terminal has a different AppleScript / URL-scheme API.
        switch (terminal.Name)
        {
            case "Terminal":
                RunAppleScript($"""tell application "Terminal" to do script "{EscapeAppleScript(command)}" """);
                break;

            case "iTerm":
                RunAppleScript($"""
                    tell application "iTerm"
                        create window with default profile command "{EscapeAppleScript(command)}"
                    end tell
                    """);
                break;

            case "Warp":
                // Warp supports a custom URL scheme.
                var warpUrl = $"warp://action/new_tab?command={Uri.EscapeDataString(command)}";
                System.Diagnostics.Process.Start(new ProcessStartInfo { FileName = "open", ArgumentList = { warpUrl }, UseShellExecute = false });
                break;

            default:
                // Generic fallback: write a temp script and open it with the app.
                var script = Path.GetTempFileName() + ".command";
                File.WriteAllText(script, $"#!/bin/sh\n{command}\n");
                File.SetAttributes(script, File.GetAttributes(script)); // keep, chmod below
                System.Diagnostics.Process.Start("chmod", $"+x \"{script}\"")?.WaitForExit();
                System.Diagnostics.Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { "-a", terminal.Name, script },
                    UseShellExecute = false,
                });
                break;
        }
    }

    private static void ExecuteCommandWindows(string command, TerminalInfo terminal)
    {
        var (fileName, args) = terminal.Name switch
        {
            "PowerShell" => (terminal.ExecutablePath, $"-NoExit -Command \"{command.Replace("\"", "\\\"")}\""),
            "Command Prompt" => (terminal.ExecutablePath, $"/K \"{command}\""),
            _ => (terminal.ExecutablePath, command),
        };

        System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = true,
        });
    }

    private static void RunAppleScript(string script)
    {
        System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = "osascript",
            ArgumentList = { "-e", script },
            UseShellExecute = false,
        });
    }

    /// Escapes double-quotes and backslashes for use inside an AppleScript string literal.
    private static string EscapeAppleScript(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
