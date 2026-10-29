using System.Diagnostics;

namespace Polyglotta.Tray;

/// <summary>
/// Registers the agent to start at logon through a Scheduled Task (ADR-0007).
/// <para>
/// Preferred over the <c>Run</c> registry key: the task survives a restart, can be
/// delayed, and is visible to the user in a place they can turn off without regedit.
/// A logon task that runs as the current user needs no elevation.
/// </para>
/// </summary>
public static class WindowsAutostart
{
    public const string TaskName = "Polyglotta";

    /// <summary>Path of the running executable, which is what the task will launch.</summary>
    private static string ExecutablePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("Não foi possível descobrir o caminho do executável.");

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName).ExitCode == 0;

    /// <summary>Creates or replaces the logon task. Returns null on success, or the error output.</summary>
    public static string? Enable()
    {
        var result = Run(
            "/Create", "/F",
            "/SC", "ONLOGON",
            "/TN", TaskName,
            "/TR", $"\"{ExecutablePath}\"",
            // A couple of seconds of slack so the audio endpoints are up before we look.
            "/DELAY", "0000:30");

        return result.ExitCode == 0 ? null : Describe(result);
    }

    /// <summary>Removes the logon task. Returns null on success, or the error output.</summary>
    public static string? Disable()
    {
        var result = Run("/Delete", "/F", "/TN", TaskName);
        return result.ExitCode == 0 ? null : Describe(result);
    }

    private static string Describe((int ExitCode, string Output, string Error) result)
    {
        var text = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        return string.IsNullOrWhiteSpace(text)
            ? $"schtasks falhou (código {result.ExitCode})."
            : text.Trim();
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] arguments)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(info)
                ?? throw new InvalidOperationException("schtasks.exe não pôde ser iniciado.");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);
            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
