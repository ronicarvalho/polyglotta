namespace Polyglotta.Core.Configuration;

/// <summary>
/// Where the provider keys live (ADR-0009). Values are secrets: implementations must
/// never log them, and must never put them in an exception message.
/// </summary>
public interface ICredentialStore
{
    /// <summary>The stored value, or null when absent.</summary>
    string? Read(string name);

    /// <summary>True when this store accepts writes; an environment-backed store does not.</summary>
    bool CanSave { get; }

    void Save(string name, string value);

    /// <summary>Removes the credential. Removing one that is not there is not an error.</summary>
    void Delete(string name);
}
