using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Polyglotta.Core.Configuration;

namespace Polyglotta.Security.Windows;

/// <summary>
/// Stores credentials on Windows, each value encrypted with DPAPI in the
/// <see cref="DataProtectionScope.CurrentUser"/> scope (ADR-0009).
/// <para>
/// What this buys: the file is useless on another machine or under another account.
/// What it does not buy: protection from code running as the user — anything of yours
/// can call <c>Unprotect</c>. Same guarantee a browser gives its saved cookies.
/// </para>
/// </summary>
public sealed class DpapiCredentialStore : ICredentialStore
{
    /// <summary>
    /// Extra entropy mixed into the encryption so a blob of ours is not interchangeable
    /// with one another application protected for the same user.
    /// </summary>
    private static readonly byte[] Entropy = "Polyglotta/credentials/v1"u8.ToArray();

    private readonly string _path;

    public DpapiCredentialStore(string? path = null) =>
        _path = path ?? Path.Combine(UserPaths.ConfigDirectory, "credentials.json");

    public bool CanSave => true;

    public string? Read(string name)
    {
        if (!Load().TryGetValue(name, out var encoded))
            return null;

        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(encoded), Entropy, DataProtectionScope.CurrentUser);
            return EnvironmentSettings.Clean(Encoding.UTF8.GetString(plain));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Blob written by another user, or a corrupted file. Treat it as "not
            // configured" so the app still starts and the screen can ask again.
            return null;
        }
    }

    public void Save(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A credential cannot be blank.", nameof(value));

        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value.Trim()), Entropy, DataProtectionScope.CurrentUser);

        var all = Load();
        all[name] = Convert.ToBase64String(encrypted);
        Write(all);
    }

    public void Delete(string name)
    {
        var all = Load();
        if (all.Remove(name))
            Write(all);
    }

    /// <summary>The names currently stored, for a screen that lists what is configured.</summary>
    public IReadOnlyCollection<string> Names() => Load().Keys;

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return [];
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Writes through a temporary file so an interrupted save never leaves the user
    /// without the credential they already had.
    /// </summary>
    private void Write(Dictionary<string, string> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
