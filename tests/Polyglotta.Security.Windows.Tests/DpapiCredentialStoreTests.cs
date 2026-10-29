using Polyglotta.Security.Windows;
using Xunit;

namespace Polyglotta.Security.Windows.Tests;

/// <summary>
/// Real DPAPI against a temporary file. Fast, local, and it exercises the thing that
/// actually matters: that a saved key comes back, and that the file never holds it
/// in the clear.
/// </summary>
public class DpapiCredentialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "polyglotta-tests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public DpapiCredentialStoreTests() => _path = Path.Combine(_dir, "credentials.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private DpapiCredentialStore Store() => new(_path);

    [Fact]
    public void Save_ThenRead_ReturnsTheValue()
    {
        Store().Save("AZURE_SPEECH_KEY", "chave-secreta-123");

        Assert.Equal("chave-secreta-123", Store().Read("AZURE_SPEECH_KEY"));
    }

    [Fact]
    public void TheFileNeverHoldsTheSecretInTheClear()
    {
        Store().Save("AZURE_SPEECH_KEY", "chave-secreta-123");

        var onDisk = File.ReadAllText(_path);

        Assert.DoesNotContain("chave-secreta-123", onDisk);
        Assert.Contains("AZURE_SPEECH_KEY", onDisk); // the name is not a secret
    }

    [Fact]
    public void Save_KeepsTheOtherCredentials()
    {
        var store = Store();
        store.Save("AZURE_SPEECH_KEY", "a");
        store.Save("AZURE_SPEECH_REGION", "eastus");

        Assert.Equal("a", store.Read("AZURE_SPEECH_KEY"));
        Assert.Equal("eastus", store.Read("AZURE_SPEECH_REGION"));
        Assert.Equal(2, store.Names().Count);
    }

    [Fact]
    public void Save_OverAnExistingName_Replaces()
    {
        var store = Store();
        store.Save("GEMINI_API_KEY", "antiga");
        store.Save("GEMINI_API_KEY", "nova");

        Assert.Equal("nova", store.Read("GEMINI_API_KEY"));
    }

    [Fact]
    public void Read_OfSomethingNeverSaved_IsNull() => Assert.Null(Store().Read("NAO_EXISTE"));

    [Fact]
    public void Read_WithoutAFile_IsNull() => Assert.Null(new DpapiCredentialStore(_path).Read("QUALQUER"));

    [Fact]
    public void Delete_RemovesOnlyThatCredential()
    {
        var store = Store();
        store.Save("A", "1");
        store.Save("B", "2");

        store.Delete("A");

        Assert.Null(store.Read("A"));
        Assert.Equal("2", store.Read("B"));
    }

    [Fact]
    public void Delete_OfSomethingAbsent_IsNotAnError()
    {
        Store().Delete("NAO_EXISTE");
        Assert.Empty(Store().Names());
    }

    [Fact]
    public void Save_RefusesABlankValue() =>
        Assert.Throws<ArgumentException>(() => Store().Save("A", "   "));

    [Fact]
    public void ADamagedFile_ReadsAsNotConfigured_SoTheAppStillStarts()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ isto não é json");

        Assert.Null(Store().Read("AZURE_SPEECH_KEY"));
    }

    [Fact]
    public void ABlobThatCannotBeDecrypted_ReadsAsNotConfigured()
    {
        // Simulates a credentials file copied from another machine or user.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """{ "AZURE_SPEECH_KEY": "bm90LWEtZHBhcGktYmxvYg==" }""");

        Assert.Null(Store().Read("AZURE_SPEECH_KEY"));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        Store().Save("A", "1");

        Assert.False(File.Exists(_path + ".tmp"));
    }
}
