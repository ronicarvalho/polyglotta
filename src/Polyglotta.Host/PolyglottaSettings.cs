using Polyglotta.Core.Audio;

namespace Polyglotta.Host;

/// <summary>
/// How a device was chosen. Both the name and the endpoint id are kept, and the
/// <em>name</em> is what resolution tries first: we have already lost one id to a
/// hardware swap and another to a Windows update, and the name survived both
/// (ADR-0007).
/// </summary>
public sealed record DeviceSelection(string? Name = null, string? Id = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Id);

    public static DeviceSelection From(AudioDeviceInfo device) => new(device.Name, device.Id);

    public override string ToString() => Name ?? Id ?? "(padrão do sistema)";
}

/// <summary>
/// Everything the session needs to run — what the CLI used to take as arguments and
/// what the configuration screen will edit. Credentials are deliberately absent: they
/// live in the <see cref="Core.Configuration.ICredentialStore"/> (ADR-0009).
/// </summary>
public sealed record PolyglottaSettings
{
    /// <summary>"azure" or "gemini" (ADR-0006).</summary>
    public string Provider { get; init; } = "azure";

    /// <summary>The user's own language: spoken into the microphone, heard on the headphones.</summary>
    public string FromLanguage { get; init; } = "pt-BR";

    /// <summary>The remote party's language: sent to the virtual microphone.</summary>
    public string ToLanguage { get; init; } = "en-US";

    /// <summary>Voice the remote party hears. "male", "female" or a provider voice name.</summary>
    public string OutboundVoice { get; init; } = "female";

    /// <summary>Voice the user hears for the translated remote speech.</summary>
    public string InboundVoice { get; init; } = "female";

    /// <summary>Physical microphone. Empty means the system default.</summary>
    public DeviceSelection Microphone { get; init; } = new();

    /// <summary>Output device the call plays on, tapped by loopback. Empty means the system default.</summary>
    public DeviceSelection Loopback { get; init; } = new();

    /// <summary>Where the translated remote speech is played. Empty means the system default.</summary>
    public DeviceSelection Phones { get; init; } = new();

    /// <summary>Render endpoint of the virtual cable the call app listens to (ADR-0002).</summary>
    public string VirtualMicName { get; init; } = "CABLE Input";

    /// <summary>
    /// O lado de captura do mesmo cabo virtual — o que a aplicação de chamada escuta.
    /// Separado porque no VB-Cable são dois dispositivos com nomes diferentes; no BlackHole
    /// (macOS) e num null sink do PipeWire costuma ser o mesmo nome dos dois lados.
    /// </summary>
    public string VirtualMicCaptureName { get; init; } = "CABLE Output";

    /// <summary>
    /// Whether the tray agent should activate as soon as it starts. Off by default:
    /// an app that opens the microphone at logon without being asked loses trust.
    /// </summary>
    public bool ActivateOnStartup { get; init; }

    public bool UsesAzure => string.Equals(Provider, "azure", StringComparison.OrdinalIgnoreCase);
}
