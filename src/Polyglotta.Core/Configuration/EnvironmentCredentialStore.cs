namespace Polyglotta.Core.Configuration;

/// <summary>
/// Read-only view of the environment variables, reusing <see cref="EnvironmentSettings"/>
/// so the quote-stripping that cost us a DNS failure in 2026-09-22 applies here too.
/// </summary>
public sealed class EnvironmentCredentialStore : ICredentialStore
{
    public string? Read(string name) => EnvironmentSettings.Read(name);

    public bool CanSave => false;

    public void Save(string name, string value) =>
        throw new NotSupportedException("Environment variables are read-only here; save to the OS-backed store instead.");

    public void Delete(string name) =>
        throw new NotSupportedException("Environment variables are read-only here.");
}
