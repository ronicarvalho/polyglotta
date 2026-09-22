namespace Polyglotta.Core.Configuration;

/// <summary>
/// Reads settings from environment variables, tolerating the usual quoting mistakes.
/// <para>
/// `set VAR="eastus"` in cmd.exe stores the quotes as part of the value, which produced
/// a request to <c>wss://"eastus".stt.speech.microsoft.com</c> and a DNS failure that
/// looks nothing like a configuration error (2026-09-22). Values are therefore trimmed
/// and unwrapped before use.
/// </para>
/// </summary>
public static class EnvironmentSettings
{
    /// <summary>Reads and cleans a variable; null when unset or empty after cleaning.</summary>
    public static string? Read(string name) => Clean(Environment.GetEnvironmentVariable(name));

    /// <summary>Reads and cleans a variable, throwing when it is unset or blank.</summary>
    public static string Require(string name) =>
        Read(name) ?? throw new InvalidOperationException($"{name} not set");

    /// <summary>
    /// Trims whitespace and one matching pair of surrounding quotes. Returns null when
    /// nothing is left. Quotes inside the value are preserved.
    /// </summary>
    public static string? Clean(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;

        if (value.Length >= 2 &&
            (value[0] == '"' || value[0] == '\'') &&
            value[^1] == value[0])
        {
            value = value[1..^1].Trim();
        }

        return value.Length == 0 ? null : value;
    }
}
