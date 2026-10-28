using Polyglotta.Audio.Windows;
using Polyglotta.Core.Audio;
using Polyglotta.Core.Configuration;
using Polyglotta.Core.Pipeline;
using Polyglotta.Core.Translation;
using Polyglotta.Translation.Azure;
using Polyglotta.Translation.Gemini;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
}).SetMinimumLevel(LogLevel.Debug));
var log = loggerFactory.CreateLogger("cli");

var enumerator = new WindowsAudioDeviceEnumerator();

if (args.Contains("--list-devices"))
{
    foreach (var d in enumerator.ListDevices())
        Console.WriteLine($"[{d.Kind}] {(d.IsDefault ? "*" : " ")} {d.Name}\n    id: {d.Id}");
    return 0;
}

string Arg(string name, string fallback) =>
    args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;

// Device check mode needs no cloud credentials.
var checkAudio = args.Contains("--check-audio");

// ADR-0006: Azure (padrão) ou Gemini Live em modo tradução.
var provider = Arg("--provider", "azure").Trim().ToLowerInvariant();
if (provider is not ("azure" or "gemini"))
{
    log.LogError("--provider \"{Provider}\": use \"azure\" ou \"gemini\".", provider);
    return 1;
}
var useAzure = provider == "azure";

if (!checkAudio)
{
    var required = useAzure
        ? new[] { "AZURE_SPEECH_KEY", "AZURE_SPEECH_REGION" }
        : EnvironmentSettings.Read("GEMINI_API_KEY") is not null
            ? ["GEMINI_API_KEY"]
            : new[] { "GOOGLE_CLOUD_PROJECT", "GOOGLE_CLOUD_LOCATION" };

    foreach (var name in required)
    {
        if (EnvironmentSettings.Read(name) is null)
        {
            log.LogError("Variável de ambiente {Name} não definida (ver README).", name);
            return 1;
        }
    }

    if (useAzure)
        log.LogInformation("Provedor: Azure (região {Region})", EnvironmentSettings.Require("AZURE_SPEECH_REGION"));
    else
        log.LogInformation("Provedor: Gemini Live tradução (modelo {Model})",
            EnvironmentSettings.Read("GEMINI_LIVE_MODEL") ?? GeminiSpeechTranslator.DefaultModel);
}

var fromLang = Arg("--from", "pt-BR");
var toLang = Arg("--to", "en-US");

// --voice/--voice-in aceitam "male"/"female" ou o nome completo da voz Azure.
// O padrão é voz HD (bem mais natural que as neurais de 1a geração).
var resolveVoices = !checkAudio && useAzure;
var voiceKey = resolveVoices ? EnvironmentSettings.Require("AZURE_SPEECH_KEY") : "";
var voiceRegion = resolveVoices ? EnvironmentSettings.Require("AZURE_SPEECH_REGION") : "";
var outVoice = resolveVoices ? await AzureVoices.ResolveAsync(Arg("--voice", "female"), toLang, voiceKey, voiceRegion) : null;
var inVoice = resolveVoices ? await AzureVoices.ResolveAsync(Arg("--voice-in", "female"), fromLang, voiceKey, voiceRegion) : null;

// O modo de tradução do Gemini não permite escolher a voz (ADR-0006).
if (!useAzure && !checkAudio && (args.Contains("--voice") || args.Contains("--voice-in")))
    log.LogWarning("--voice/--voice-in são ignorados com --provider gemini: o modo de tradução não permite escolher a voz.");

if (args.Contains("--list-voices"))
{
    var filter = Arg("--list-voices", "");
    var wanted = filter.Length > 0 ? new[] { filter } : new[] { fromLang, toLang };
    foreach (var locale in wanted)
    {
        Console.WriteLine($"\n=== {locale}");
        foreach (var v in await AzureVoices.ListAsync(voiceKey, voiceRegion, locale))
            Console.WriteLine($"  {(v.IsHighDefinition ? "HD" : "  ")} {v.Gender,-7} {v.ShortName}");
    }
    Console.WriteLine("\nUse o nome em --voice (idioma de saída) ou --voice-in (idioma de entrada).");
    return 0;
}

// Outbound render target: the VB-Cable virtual mic input (ADR-0002).
var cable = enumerator.FindByName("CABLE Input", AudioDeviceKind.Render);
if (cable is null)
{
    log.LogError("Dispositivo \"CABLE Input\" não encontrado. Instale o VB-Cable: https://vb-audio.com/Cable/");
    return 1;
}

var devices = enumerator.ListDevices();

AudioDeviceInfo? Resolve(string option, AudioDeviceKind kind)
{
    // Accepts an endpoint id or a fragment of the device name, so a command can be
    // written once and survive the ids changing between machines and reconnections.
    var value = Arg(option, "");
    if (value.Length == 0)
        return devices.FirstOrDefault(d => d.Kind == kind && d.IsDefault);

    var candidates = devices.Where(d => d.Kind == kind).ToList();
    if (candidates.FirstOrDefault(d => d.Id == value) is { } byId)
        return byId;

    var byName = candidates
        .Where(d => d.Name.Contains(value, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (byName.Count == 1)
        return byName[0];

    if (byName.Count == 0)
        log.LogError("{Option} \"{Value}\": nenhum dispositivo de {Kind} com esse id ou nome. Use --list-devices.",
            option, value, kind);
    else
        log.LogError("{Option} \"{Value}\": ambíguo, {Count} dispositivos de {Kind} correspondem ({Names}). Seja mais específico ou use o id.",
            option, value, byName.Count, kind, string.Join(" | ", byName.Select(d => d.Name)));
    return null;
}

// Outbound source: the physical microphone (default communications device unless --mic).
var mic = Resolve("--mic", AudioDeviceKind.Capture);

// Inbound source: the render device Meet/Teams plays on (tapped via WASAPI loopback).
var loopback = Resolve("--loopback", AudioDeviceKind.Render);
// Inbound playback: the user's headphones.
var phones = Resolve("--phones", AudioDeviceKind.Render);
if (mic is null || loopback is null || phones is null)
    return 1;

// Feedback guards (audio-realtime skill): the translated audio we play must never
// be captured back by the loopback, and nothing inbound may go to the virtual mic.
if (phones.Id == loopback.Id)
{
    log.LogError(
        "O fone ({Phones}) é o mesmo dispositivo capturado por loopback. A tradução seria " +
        "recapturada e retraduzida em loop. Faça o Meet/Teams tocar num dispositivo (--loopback) " +
        "diferente do fone (--phones). Veja os ids em --list-devices.", phones.Name);
    return 1;
}
if (phones.Id == cable.Id || loopback.Id == cable.Id)
{
    log.LogError("\"CABLE Input\" é reservado ao microfone virtual (outbound); não use como --phones nem --loopback.");
    return 1;
}
if (mic.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
{
    log.LogError(
        "\"CABLE Output\" é a saída do microfone virtual (o que o Meet/Teams escuta): usá-lo como --mic " +
        "faria o app capturar a própria tradução. Escolha o microfone físico em --list-devices.");
    return 1;
}

ISpeechTranslator CreateTranslator() => useAzure
    ? AzureSpeechTranslator.FromEnvironment()
    : GeminiSpeechTranslator.FromEnvironment();

if (checkAudio)
    return await CheckAudioAsync();

log.LogInformation("Outbound: {Mic} -> {From}->{To} ({Voice}) -> {Cable}", mic.Name, fromLang, toLang, outVoice ?? "voz padrão", cable.Name);
log.LogInformation("Inbound : loopback({Loopback}) -> {To}->{From} ({Voice}) -> {Phones}", loopback.Name, toLang, fromLang, inVoice ?? "voz padrão", phones.Name);

await using var outbound = new DubbingPipeline(
    "outbound",
    new WasapiMicCapture(mic.Id),
    CreateTranslator(),
    new WasapiRenderDevice(cable.Id),
    loggerFactory.CreateLogger<DubbingPipeline>());

await using var inbound = new DubbingPipeline(
    "inbound",
    new WasapiLoopbackSystemCapture(loopback.Id),
    CreateTranslator(),
    new WasapiRenderDevice(phones.Id),
    loggerFactory.CreateLogger<DubbingPipeline>());

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// RF-07 (auto-reconnect) is not implemented yet: a faulted stage ends the session.
var faulted = false;
foreach (var p in new[] { outbound, inbound })
    p.Faulted += (_, _) => { faulted = true; cts.Cancel(); };

await outbound.StartAsync(new TranslatorOptions(fromLang, toLang, outVoice), cts.Token);
await inbound.StartAsync(new TranslatorOptions(toLang, fromLang, inVoice), cts.Token);

log.LogInformation("Rodando. No Meet/Teams selecione \"CABLE Output\" como microfone. [M] mute outbound, Ctrl+C sai.");

var interactive = !Console.IsInputRedirected;
while (!cts.IsCancellationRequested)
{
    if (interactive && Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.M)
    {
        outbound.Muted = !outbound.Muted;
        log.LogInformation("Outbound {State}", outbound.Muted ? "MUTED" : "ativo");
    }
    await Task.Delay(100).ConfigureAwait(false);
}

await outbound.StopAsync();
await inbound.StopAsync();

foreach (var p in new[] { outbound, inbound })
{
    if (p.Latency.Count > 0)
        log.LogInformation("[{Pipeline}] latência: n={Count} última={Last:F2}s p95={P95:F2}s (meta ≤ 3 s)",
            p.Name, p.Latency.Count, p.Latency.Last!.Value.TotalSeconds, p.Latency.P95!.Value.TotalSeconds);
    else
        log.LogInformation("[{Pipeline}] latência: nenhuma frase completa medida", p.Name);
}

return faulted ? 2 : 0;

// ---------------------------------------------------------------------------
// --check-audio: proves the device setup end to end without touching the cloud.
// Reads the microphone and the loopback tap, then plays a short tone on each
// output so the whole path can be verified before a real call.
// ---------------------------------------------------------------------------
async Task<int> CheckAudioAsync()
{
    var seconds = int.TryParse(Arg("--check-audio", "8"), out var s) ? Math.Clamp(s, 3, 60) : 8;

    Console.WriteLine($"\nMicrofone : {mic.Name}");
    Console.WriteLine($"Loopback  : {loopback.Name}");
    Console.WriteLine($"Fone      : {phones.Name}");
    Console.WriteLine($"Mic virtual: {cable.Name}\n");
    Console.WriteLine($"FALE AO MICROFONE pelos próximos {seconds}s (e deixe algo tocando no loopback).\n");

    await using var micCapture = new WasapiMicCapture(mic.Id);
    await using var loopCapture = new WasapiLoopbackSystemCapture(loopback.Id);
    await micCapture.StartAsync();
    await loopCapture.StartAsync();

    double micPeak = 0, loopPeak = 0;
    int micVoiced = 0, loopVoiced = 0, micFrames = 0, loopFrames = 0;

    for (var second = 1; second <= seconds; second++)
    {
        var until = DateTime.UtcNow.AddSeconds(1);
        double micSecondPeak = 0, loopSecondPeak = 0;
        while (DateTime.UtcNow < until)
        {
            while (micCapture.Frames.TryRead(out var f))
            {
                micFrames++;
                var rms = PcmMath.Rms(f.Pcm);
                micSecondPeak = Math.Max(micSecondPeak, rms);
                if (rms > 500) micVoiced++;
            }
            while (loopCapture.Frames.TryRead(out var f))
            {
                loopFrames++;
                var rms = PcmMath.Rms(f.Pcm);
                loopSecondPeak = Math.Max(loopSecondPeak, rms);
                if (rms > 500) loopVoiced++;
            }
            await Task.Delay(10);
        }
        micPeak = Math.Max(micPeak, micSecondPeak);
        loopPeak = Math.Max(loopPeak, loopSecondPeak);
        Console.WriteLine($"  {second,2}s  mic {Bar(micSecondPeak)}   loopback {Bar(loopSecondPeak)}");
    }

    await micCapture.StopAsync();
    await loopCapture.StopAsync();

    Console.WriteLine();
    Report("Microfone", mic.Name, micFrames, micVoiced, micPeak);
    Report("Loopback", loopback.Name, loopFrames, loopVoiced, loopPeak);

    Console.WriteLine("\nTocando 1 s de tom em cada saída...");
    await PlayToneAsync(phones, "você deve ouvir no fone");
    await PlayToneAsync(cable, "vai para o Meet/Teams como microfone");
    Console.WriteLine("\nPronto.");
    return micVoiced > 0 ? 0 : 1;

    static string Bar(double rms)
    {
        var scale = Math.Clamp(rms / short.MaxValue, 0, 1);
        // Logarithmic: speech sits far below full scale on a linear bar.
        var filled = (int)Math.Round(Math.Clamp((Math.Log10(scale + 1e-6) + 4) / 4, 0, 1) * 20);
        return $"[{new string('#', filled)}{new string('.', 20 - filled)}] {scale,6:P2}";
    }

    void Report(string label, string device, int frames, int voiced, double peak)
    {
        if (frames == 0)
            Console.WriteLine($"{label}: NENHUM FRAME recebido de \"{device}\" — dispositivo ocupado ou indisponível.");
        else if (voiced == 0)
            Console.WriteLine(
                $"{label}: SEM SINAL em \"{device}\" — {frames} frames, pico {peak / short.MaxValue:P2}. " +
                "Verifique o cabo/jack, o volume e se outro app tomou o dispositivo em modo exclusivo.");
        else
            Console.WriteLine($"{label}: OK — \"{device}\", {voiced} de {frames} frames com voz, pico {peak / short.MaxValue:P2}.");
    }

    static async Task PlayToneAsync(AudioDeviceInfo device, string hint)
    {
        Console.WriteLine($"  {device.Name} ({hint})");
        var pcm = new byte[AudioFrame.SampleRate * 2]; // 1 s, 16-bit mono
        for (var i = 0; i < pcm.Length / 2; i++)
        {
            var sample = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / AudioFrame.SampleRate));
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), sample);
        }
        await using var render = new WasapiRenderDevice(device.Id);
        await render.StartAsync();
        render.Enqueue(pcm);
        await Task.Delay(1400);
        await render.StopAsync();
    }
}
