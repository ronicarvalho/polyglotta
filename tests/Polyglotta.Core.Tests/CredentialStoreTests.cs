using Polyglotta.Core.Configuration;
using Xunit;

namespace Polyglotta.Core.Tests;

public class ChainedCredentialStoreTests
{
    /// <summary>In-memory stand-in for an OS vault.</summary>
    private sealed class FakeStore(bool canSave = true) : ICredentialStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool CanSave { get; } = canSave;

        public string? Read(string name) => Values.TryGetValue(name, out var v) ? v : null;
        public void Save(string name, string value) => Values[name] = value;
        public void Delete(string name) => Values.Remove(name);
    }

    [Fact]
    public void Read_PrefersTheFirstStoreThatHasTheValue()
    {
        var first = new FakeStore(canSave: false);
        first.Values["AZURE_SPEECH_KEY"] = "do-ambiente";
        var second = new FakeStore();
        second.Values["AZURE_SPEECH_KEY"] = "do-cofre";

        var chain = new ChainedCredentialStore(first, second);

        // The environment keeps winning: that is the documented escape hatch (ADR-0009).
        Assert.Equal("do-ambiente", chain.Read("AZURE_SPEECH_KEY"));
    }

    [Fact]
    public void Read_FallsThroughToTheNextStore()
    {
        var second = new FakeStore();
        second.Values["AZURE_SPEECH_KEY"] = "do-cofre";

        var chain = new ChainedCredentialStore(new FakeStore(canSave: false), second);

        Assert.Equal("do-cofre", chain.Read("AZURE_SPEECH_KEY"));
    }

    [Fact]
    public void Read_OfSomethingNobodyHas_IsNull() =>
        Assert.Null(new ChainedCredentialStore(new FakeStore(canSave: false), new FakeStore()).Read("NADA"));

    [Fact]
    public void Save_GoesToTheFirstWritableStore()
    {
        var readOnly = new FakeStore(canSave: false);
        var vault = new FakeStore();

        new ChainedCredentialStore(readOnly, vault).Save("GEMINI_API_KEY", "abc");

        Assert.Empty(readOnly.Values);
        Assert.Equal("abc", vault.Values["GEMINI_API_KEY"]);
    }

    [Fact]
    public void Save_WithNoWritableStore_Throws() =>
        Assert.Throws<NotSupportedException>(() =>
            new ChainedCredentialStore(new FakeStore(canSave: false)).Save("X", "y"));

    [Fact]
    public void Delete_ClearsEveryWritableStore()
    {
        var a = new FakeStore();
        var b = new FakeStore();
        a.Values["X"] = b.Values["X"] = "valor";

        new ChainedCredentialStore(a, b).Delete("X");

        Assert.Empty(a.Values);
        Assert.Empty(b.Values);
    }

    [Fact]
    public void ComesFromReadOnlySource_TellsTheScreenThatSavingWouldBePointless()
    {
        var environment = new FakeStore(canSave: false);
        environment.Values["AZURE_SPEECH_REGION"] = "eastus";
        var vault = new FakeStore();
        vault.Values["AZURE_SPEECH_KEY"] = "k";

        var chain = new ChainedCredentialStore(environment, vault);

        Assert.True(chain.ComesFromReadOnlySource("AZURE_SPEECH_REGION"));
        Assert.False(chain.ComesFromReadOnlySource("AZURE_SPEECH_KEY"));
        Assert.False(chain.ComesFromReadOnlySource("INEXISTENTE"));
    }

    [Fact]
    public void AnEmptyChain_IsRejected() =>
        Assert.Throws<ArgumentException>(() => new ChainedCredentialStore());
}

public class EnvironmentCredentialStoreTests
{
    [Fact]
    public void Read_CleansTheValue_LikeEnvironmentSettingsDoes()
    {
        // `set VAR="eastus"` in cmd.exe keeps the quotes; that cost us a DNS failure.
        Environment.SetEnvironmentVariable("POLYGLOTTA_TEST_CRED", "\"eastus\"");
        try
        {
            Assert.Equal("eastus", new EnvironmentCredentialStore().Read("POLYGLOTTA_TEST_CRED"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("POLYGLOTTA_TEST_CRED", null);
        }
    }

    [Fact]
    public void Read_OfAnUnsetVariable_IsNull() =>
        Assert.Null(new EnvironmentCredentialStore().Read("POLYGLOTTA_NAO_DEFINIDA"));

    [Fact]
    public void ItIsReadOnly()
    {
        var store = new EnvironmentCredentialStore();

        Assert.False(store.CanSave);
        Assert.Throws<NotSupportedException>(() => store.Save("X", "y"));
        Assert.Throws<NotSupportedException>(() => store.Delete("X"));
    }
}
