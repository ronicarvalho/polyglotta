using Polyglotta.Core.Audio;
using Polyglotta.Core.Configuration;
using Polyglotta.Translation.Azure;

namespace Polyglotta.Host;

/// <summary>
/// Toca uma frase de amostra com a voz configurada, no dispositivo que o usuário
/// escolheu como fone.
/// <para>
/// A amostra usa a voz <b>efetiva</b>: se a configuração guarda um apelido de gênero,
/// ele é resolvido no catálogo exatamente como a sessão resolveria. Ouvir uma coisa e a
/// chamada usar outra seria pior que não ter amostra.
/// </para>
/// </summary>
public sealed class VoicePreview(IAudioPlatform audio, ICredentialStore credentials)
{
    /// <summary>Bytes por segundo no formato canônico: 16 kHz × 2 bytes × 1 canal.</summary>
    private const int BytesPerSecond = 32_000;

    private static readonly (string Prefix, string Text)[] Samples =
    [
        ("pt", "Esta é a voz que vai falar durante a chamada. Um, dois, três."),
        ("en", "This is the voice that will speak during the call. One, two, three."),
        ("es", "Esta es la voz que hablará durante la llamada. Uno, dos, tres."),
        ("fr", "Voici la voix qui parlera pendant l'appel. Un, deux, trois."),
        ("de", "Das ist die Stimme, die während des Anrufs spricht. Eins, zwei, drei."),
        ("it", "Questa è la voce che parlerà durante la chiamata. Uno, due, tre.")
    ];

    /// <summary>Frase na língua da voz — uma voz en-US lendo português não diz nada sobre ela.</summary>
    public static string SampleFor(string locale)
    {
        var prefix = (locale ?? "").Split('-')[0];
        foreach (var (p, text) in Samples)
        {
            if (string.Equals(p, prefix, StringComparison.OrdinalIgnoreCase))
                return text;
        }

        return Samples[1].Text;
    }

    /// <summary>
    /// Sintetiza e toca a amostra. <paramref name="outbound"/> escolhe qual das duas
    /// vozes: a que o interlocutor ouve (idioma de destino) ou a que o usuário ouve.
    /// </summary>
    public async Task PlayAsync(PolyglottaSettings settings, bool outbound, CancellationToken ct = default)
    {
        if (!settings.UsesAzure)
            throw new InvalidOperationException(
                "O modo de tradução do Gemini não permite escolher nem demonstrar a voz (ADR-0006).");

        var key = credentials.Read("AZURE_SPEECH_KEY")
            ?? throw new InvalidOperationException("AZURE_SPEECH_KEY não está configurada.");
        var region = credentials.Read("AZURE_SPEECH_REGION")
            ?? throw new InvalidOperationException("AZURE_SPEECH_REGION não está configurada.");

        var locale = outbound ? settings.ToLanguage : settings.FromLanguage;
        var configured = outbound ? settings.OutboundVoice : settings.InboundVoice;

        var voice = await AzureVoices.ResolveAsync(configured, locale, key, region)
            ?? throw new InvalidOperationException(
                $"Nenhuma voz encontrada para {locale}: o serviço usaria a voz padrão dele, que não dá para demonstrar.");

        var pcm = await AzureVoicePreview.SynthesizeAsync(voice, SampleFor(locale), key, region, ct);

        var phones = SessionPlanner.ResolveOne(settings.Phones, AudioDeviceKind.Render, audio.Devices.ListDevices())
            ?? throw new InvalidOperationException(
                "Nenhum fone resolvido: escolha o dispositivo de saída na aba Dispositivos.");

        await using var render = audio.CreateRenderDevice(phones.Id);
        await render.StartAsync(ct);
        render.Enqueue(pcm);

        // A fila é tocada em segundo plano; esperamos o áudio terminar antes de fechar o
        // dispositivo, com uma cauda para o buffer do WASAPI esvaziar.
        var duration = TimeSpan.FromSeconds((double)pcm.Length / BytesPerSecond)
                       + TimeSpan.FromMilliseconds(400);
        await Task.Delay(duration, ct);

        await render.StopAsync(CancellationToken.None);
    }
}
