using System.Text.Json;
using System.Text.Json.Serialization;
using Polyglotta.Core.Configuration;

namespace Polyglotta.Host;

/// <summary>
/// Reads and writes <c>config.json</c> in the per-user configuration directory
/// (ADR-0007). Credentials are not in here — see <see cref="ICredentialStore"/>.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Path { get; }

    public SettingsStore(string? path = null) =>
        Path = path ?? System.IO.Path.Combine(UserPaths.ConfigDirectory, "config.json");

    /// <summary>
    /// The saved settings, or the defaults when there is no file. A damaged file also
    /// yields the defaults: the app must still start so the screen can fix it.
    /// </summary>
    public PolyglottaSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
                return new PolyglottaSettings();

            return JsonSerializer.Deserialize<PolyglottaSettings>(File.ReadAllText(Path), Options)
                   ?? new PolyglottaSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new PolyglottaSettings();
        }
    }

    /// <summary>Writes through a temporary file, so an interrupted save never truncates the config.</summary>
    public void Save(PolyglottaSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, Path, overwrite: true);
    }
}
