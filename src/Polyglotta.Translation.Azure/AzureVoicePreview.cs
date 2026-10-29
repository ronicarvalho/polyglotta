using Microsoft.CognitiveServices.Speech;

namespace Polyglotta.Translation.Azure;

/// <summary>
/// Sintetiza uma frase curta com uma voz específica, para a pessoa ouvir antes de
/// escolher. Devolve PCM no formato canônico do pipeline (16 kHz, 16 bits, mono), que é
/// o que os dispositivos de saída do projeto aceitam — a reprodução é nossa, não do SDK.
/// </summary>
public static class AzureVoicePreview
{
    public static async Task<ReadOnlyMemory<byte>> SynthesizeAsync(
        string voiceName, string text, string key, string region, CancellationToken ct = default)
    {
        var config = SpeechConfig.FromSubscription(key, region);
        config.SpeechSynthesisVoiceName = voiceName;
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);

        // AudioConfig nulo: o SDK não toca nada sozinho. Quem toca é o dispositivo que o
        // usuário escolheu como fone, para a amostra sair exatamente onde a chamada sairia.
        using var synthesizer = new SpeechSynthesizer(config, null);

        // O SDK não aceita CancellationToken aqui; WaitAsync abandona a espera e a
        // síntese pendente morre com o synthesizer no dispose.
        using var result = await synthesizer.SpeakTextAsync(text).WaitAsync(ct);

        if (result.Reason == ResultReason.Canceled)
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(result);
            throw new InvalidOperationException(
                $"Azure recusou a síntese com \"{voiceName}\": {details.Reason} — {details.ErrorDetails}");
        }

        return result.AudioData;
    }
}
