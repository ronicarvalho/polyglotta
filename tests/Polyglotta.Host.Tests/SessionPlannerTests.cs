using Polyglotta.Core.Audio;
using Polyglotta.Host;
using Xunit;

namespace Polyglotta.Host.Tests;

/// <summary>
/// Resolution of devices and the feedback guards. Pure logic over a device list —
/// no audio hardware involved, which is the point of <see cref="SessionPlanner"/>.
/// </summary>
public class SessionPlannerTests
{
    private static AudioDeviceInfo Capture(string name, string id, bool isDefault = false) =>
        new(id, name, AudioDeviceKind.Capture, isDefault);

    private static AudioDeviceInfo Render(string name, string id, bool isDefault = false) =>
        new(id, name, AudioDeviceKind.Render, isDefault);

    /// <summary>The user's real setup, as listed on 2026-10-18.</summary>
    private static readonly AudioDeviceInfo[] Devices =
    [
        Capture("Microphone (USB PnP Sound Device)", "mic-usb", isDefault: true),
        Capture("CABLE Output (VB-Audio Virtual Cable)", "cable-out"),
        Render("Speakers (USB PnP Sound Device)", "spk-usb", isDefault: true),
        Render("CABLE Input (VB-Audio Virtual Cable)", "cable-in"),
        Render("27HC5R (2- HD Audio Driver for Display Audio)", "monitor-1"),
        Render("27HC5R (3- HD Audio Driver for Display Audio)", "monitor-2")
    ];

    private static PolyglottaSettings Working() => new()
    {
        Microphone = new DeviceSelection("USB PnP"),
        Loopback = new DeviceSelection("27HC5R (2-"),
        Phones = new DeviceSelection("Speakers")
    };

    [Fact]
    public void Prepare_ResolvesEachDeviceByNameFragment()
    {
        var result = SessionPlanner.Prepare(Working(), Devices);

        Assert.Empty(result.Problems);
        Assert.True(result.IsValid);
        Assert.Equal("mic-usb", result.Plan!.Microphone.Id);
        Assert.Equal("monitor-1", result.Plan.Loopback.Id);
        Assert.Equal("spk-usb", result.Plan.Phones.Id);
        Assert.Equal("cable-in", result.Plan.VirtualMic.Id);
    }

    [Fact]
    public void Prepare_PrefersTheNameOverTheId()
    {
        // The id changed (hardware swap / Windows update) but the name did not.
        var settings = Working() with { Microphone = new DeviceSelection("USB PnP", "id-que-nao-existe-mais") };

        var result = SessionPlanner.Prepare(settings, Devices);

        Assert.Empty(result.Problems);
        Assert.Equal("mic-usb", result.Plan!.Microphone.Id);
    }

    [Fact]
    public void Prepare_FallsBackToTheId_WhenTheNameNoLongerMatches()
    {
        // The CLI passes the same value as both name and id, since it cannot tell them apart.
        var settings = Working() with { Microphone = new DeviceSelection("mic-usb", "mic-usb") };

        var result = SessionPlanner.Prepare(settings, Devices);

        Assert.Empty(result.Problems);
        Assert.Equal("mic-usb", result.Plan!.Microphone.Id);
    }

    [Fact]
    public void Prepare_UsesTheSystemDefault_WhenNothingWasChosen()
    {
        var settings = new PolyglottaSettings { Loopback = new DeviceSelection("27HC5R (2-") };

        var result = SessionPlanner.Prepare(settings, Devices);

        Assert.Equal("mic-usb", result.Plan!.Microphone.Id);
        Assert.Equal("spk-usb", result.Plan.Phones.Id);
    }

    [Fact]
    public void Prepare_ReportsAnAmbiguousName_InsteadOfGuessing()
    {
        var settings = Working() with { Loopback = new DeviceSelection("27HC5R") };

        var result = SessionPlanner.Prepare(settings, Devices);

        var problem = Assert.Single(result.Problems);
        Assert.Equal("loopback", problem.Field);
        Assert.Contains("ambíguo", problem.Message);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void Prepare_ReportsADeviceThatDisappeared()
    {
        var settings = Working() with { Phones = new DeviceSelection("Kuba", "jack-123") };

        var result = SessionPlanner.Prepare(settings, Devices);

        var problem = Assert.Single(result.Problems);
        Assert.Equal("phones", problem.Field);
        Assert.Contains("não está mais disponível", problem.Message);
    }

    [Fact]
    public void Prepare_ReportsAMissingVirtualCable()
    {
        var withoutCable = Devices.Where(d => d.Id != "cable-in").ToList();

        var result = SessionPlanner.Prepare(Working(), withoutCable);

        Assert.Contains(result.Problems, p => p.Field == "virtualmic" && p.Message.Contains("VB-Cable"));
    }

    [Fact]
    public void Prepare_RejectsAnUnknownProvider()
    {
        var result = SessionPlanner.Prepare(Working() with { Provider = "openai" }, Devices);

        Assert.Contains(result.Problems, p => p.Field == "provider");
    }

    // --- feedback guards ----------------------------------------------------

    [Fact]
    public void Prepare_RejectsHeadphonesThatAreTheLoopbackDevice()
    {
        // The dubbed audio would be captured and translated again, forever.
        var settings = Working() with { Phones = new DeviceSelection("27HC5R (2-") };

        var result = SessionPlanner.Prepare(settings, Devices);

        Assert.Contains(result.Problems, p => p.Field == "phones" && p.Message.Contains("loop"));
        Assert.Null(result.Plan);
    }

    [Fact]
    public void Prepare_RejectsTheVirtualCableAsHeadphones()
    {
        var settings = Working() with { Phones = new DeviceSelection("CABLE Input") };

        var result = SessionPlanner.Prepare(settings, Devices);

        Assert.Contains(result.Problems, p => p.Field == "phones" && p.Message.Contains("reservado"));
    }

    [Fact]
    public void Prepare_RejectsTheVirtualCableAsLoopback()
    {
        var settings = Working() with { Loopback = new DeviceSelection("CABLE Input") };

        var result = SessionPlanner.Prepare(settings, Devices);

        Assert.Contains(result.Problems, p => p.Field == "loopback" && p.Message.Contains("reservado"));
    }

    [Fact]
    public void Prepare_RejectsTheVirtualMicOutputAsTheMicrophone()
    {
        // "CABLE Output" is what the call app hears: capturing it feeds our own
        // translation back into the pipeline.
        var settings = Working() with { Microphone = new DeviceSelection("CABLE Output") };

        var result = SessionPlanner.Prepare(settings, Devices);

        Assert.Contains(result.Problems, p => p.Field == "mic" && p.Message.Contains("própria tradução"));
    }
}
