using Polyglotta.Host;
using Xunit;

namespace Polyglotta.Host.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "polyglotta-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WithoutAFile_ReturnsTheDefaults()
    {
        var settings = new SettingsStore(Path_).Load();

        Assert.Equal("azure", settings.Provider);
        Assert.Equal("pt-BR", settings.FromLanguage);
        Assert.Equal("en-US", settings.ToLanguage);
        Assert.True(settings.Microphone.IsEmpty);
        Assert.False(settings.ActivateOnStartup);
    }

    [Fact]
    public void Save_ThenLoad_PreservesEverything()
    {
        var store = new SettingsStore(Path_);
        var original = new PolyglottaSettings
        {
            Provider = "gemini",
            FromLanguage = "pt-BR",
            ToLanguage = "en-US",
            OutboundVoice = "male",
            InboundVoice = "female",
            Microphone = new DeviceSelection("USB PnP", "mic-usb"),
            Loopback = new DeviceSelection("27HC5R (2-", "monitor-1"),
            Phones = new DeviceSelection("Speakers", "spk-usb"),
            ActivateOnStartup = true
        };

        store.Save(original);

        Assert.Equal(original, new SettingsStore(Path_).Load());
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var store = new SettingsStore(Path_);
        store.Save(new PolyglottaSettings());

        Assert.True(File.Exists(Path_));
        Assert.False(File.Exists(Path_ + ".tmp"));
    }

    [Fact]
    public void Save_OverExistingConfig_ReplacesIt()
    {
        var store = new SettingsStore(Path_);
        store.Save(new PolyglottaSettings { Provider = "azure" });
        store.Save(new PolyglottaSettings { Provider = "gemini" });

        Assert.Equal("gemini", store.Load().Provider);
    }

    [Fact]
    public void Load_WithADamagedFile_ReturnsTheDefaults()
    {
        // The app has to start anyway, so the screen can fix it.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, "{ isto não é json");

        Assert.Equal("azure", new SettingsStore(Path_).Load().Provider);
    }
}
