namespace Polyglotta.Core.Configuration;

/// <summary>
/// Reads from several stores in order and writes to the first one that accepts writes
/// (ADR-0009). The intended order is environment first, OS vault second: an environment
/// variable keeps overriding whatever the configuration screen saved, which is both the
/// backward-compatible behaviour and the escape hatch when the UI is not available.
/// </summary>
public sealed class ChainedCredentialStore : ICredentialStore
{
    private readonly IReadOnlyList<ICredentialStore> _stores;

    public ChainedCredentialStore(params ICredentialStore[] stores)
    {
        if (stores.Length == 0)
            throw new ArgumentException("At least one store is required.", nameof(stores));
        _stores = stores;
    }

    public string? Read(string name)
    {
        foreach (var store in _stores)
        {
            if (store.Read(name) is { } value)
                return value;
        }
        return null;
    }

    public bool CanSave => _stores.Any(s => s.CanSave);

    public void Save(string name, string value) => Writable().Save(name, value);

    /// <summary>Deletes from every writable store, so nothing is left behind in a second one.</summary>
    public void Delete(string name)
    {
        foreach (var store in _stores.Where(s => s.CanSave))
            store.Delete(name);
    }

    /// <summary>
    /// Tells whether a name resolves to a read-only source, which is why the configuration
    /// screen must be able to say "this one comes from the environment" instead of silently
    /// saving a value that will keep being overridden.
    /// </summary>
    public bool ComesFromReadOnlySource(string name) =>
        _stores.FirstOrDefault(s => s.Read(name) is not null) is { CanSave: false };

    private ICredentialStore Writable() =>
        _stores.FirstOrDefault(s => s.CanSave)
        ?? throw new NotSupportedException("No writable credential store is configured on this platform.");
}
