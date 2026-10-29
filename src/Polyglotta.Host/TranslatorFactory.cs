using Polyglotta.Core.Configuration;
using Polyglotta.Core.Translation;
using Polyglotta.Translation.Azure;
using Polyglotta.Translation.Gemini;

namespace Polyglotta.Host;

/// <summary>
/// Builds the configured <see cref="ISpeechTranslator"/> and answers what is still
/// missing to build it (ADR-0003/ADR-0006). Keeps the provider switch in one place,
/// so the CLI and the tray cannot drift apart on it.
/// </summary>
public sealed class TranslatorFactory(ICredentialStore credentials)
{
    /// <summary>Credential names each provider needs, for a screen that shows what is configured.</summary>
    public static IReadOnlyList<string> CredentialNames(PolyglottaSettings settings) =>
        settings.UsesAzure
            ? ["AZURE_SPEECH_KEY", "AZURE_SPEECH_REGION"]
            : ["GEMINI_API_KEY", "GOOGLE_CLOUD_PROJECT", "GOOGLE_CLOUD_LOCATION"];

    /// <summary>What is missing before the session can start. Empty means ready.</summary>
    public IReadOnlyList<SessionProblem> MissingCredentials(PolyglottaSettings settings)
    {
        var missing = new List<SessionProblem>();

        if (settings.UsesAzure)
        {
            foreach (var name in new[] { "AZURE_SPEECH_KEY", "AZURE_SPEECH_REGION" })
            {
                if (credentials.Read(name) is null)
                    missing.Add(new SessionProblem("provider", $"{name} não configurado."));
            }
            return missing;
        }

        // Gemini accepts either the API key or a Vertex AI project/location pair.
        if (credentials.Read("GEMINI_API_KEY") is not null)
            return missing;

        if (credentials.Read("GOOGLE_CLOUD_PROJECT") is not null &&
            credentials.Read("GOOGLE_CLOUD_LOCATION") is not null)
            return missing;

        missing.Add(new SessionProblem("provider",
            "Configure GEMINI_API_KEY, ou GOOGLE_CLOUD_PROJECT e GOOGLE_CLOUD_LOCATION para o Vertex AI."));
        return missing;
    }

    public ISpeechTranslator Create(PolyglottaSettings settings) =>
        settings.UsesAzure
            ? AzureSpeechTranslator.FromCredentials(credentials)
            : GeminiSpeechTranslator.FromCredentials(credentials);

    /// <summary>
    /// Turns the configured voice values ("male"/"female" or a full voice name) into
    /// real voices. Gemini's translation mode has no voice selection (ADR-0006), so
    /// there both come back null.
    /// </summary>
    public async Task<(string? Outbound, string? Inbound)> ResolveVoicesAsync(
        PolyglottaSettings settings, CancellationToken ct = default)
    {
        if (!settings.UsesAzure)
            return (null, null);

        var key = credentials.Read("AZURE_SPEECH_KEY") ?? "";
        var region = credentials.Read("AZURE_SPEECH_REGION") ?? "";

        var outbound = await AzureVoices.ResolveAsync(settings.OutboundVoice, settings.ToLanguage, key, region);
        ct.ThrowIfCancellationRequested();
        var inbound = await AzureVoices.ResolveAsync(settings.InboundVoice, settings.FromLanguage, key, region);
        return (outbound, inbound);
    }
}
