namespace Polyglotta.Core.Configuration;

/// <summary>
/// Where the per-user configuration and credentials live (ADR-0007/ADR-0009).
/// Spelled out per platform instead of relying on <see cref="Environment.SpecialFolder"/>,
/// whose mapping outside Windows does not match the convention each desktop expects.
/// </summary>
public static class UserPaths
{
    /// <summary>Directory holding <c>config.json</c> and <c>credentials.json</c>. Not created here.</summary>
    public static string ConfigDirectory { get; } = Resolve();

    private static string Resolve()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Polyglotta");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", "Polyglotta");

        // Linux and anything else: XDG.
        var xdg = EnvironmentSettings.Read("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
        return Path.Combine(xdg, "polyglotta");
    }
}
