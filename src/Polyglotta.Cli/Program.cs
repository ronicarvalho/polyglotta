using Microsoft.Extensions.Logging;
using Polyglotta.Audio.Windows;
using Polyglotta.Core.Audio;
using Polyglotta.Core.Configuration;
using Polyglotta.Core.Pipeline;
using Polyglotta.Host;
using Polyglotta.Security.Windows;
using Polyglotta.Translation.Azure;

// The CLI is one of two frontends over Polyglotta.Host (ADR-0007); the tray is the
// other. Everything here is argument parsing, console output and diagnostics — the
// orchestration lives in DubbingSession.

var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
}).SetMinimumLevel(LogLevel.Debug));
var log = loggerFactory.CreateLogger("cli");

var platform = new WindowsAudioPlatform();

// Environment first, OS vault second (ADR-0009): an exported variable keeps winning,
// so every command in the README goes on working exactly as before.
var vault = new DpapiCredentialStore();
var credentials = new ChainedCredentialStore(new EnvironmentCredentialStore(), vault);

var settingsStore = new SettingsStore();

string Arg(string name, string fallback) =>
    args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return 0;
}

if (args.Contains("--list-devices"))
{
    foreach (var d in platform.Devices.ListDevices())
        Console.WriteLine($"[{d.Kind}] {(d.IsDefault ? "*" : " ")} {d.Name}\n    id: {d.Id}");
    return 0;
}

// --- credentials (ADR-0009) -------------------------------------------------

if (args.Contains("--set-credential"))
    return SetCredential(Arg("--set-credential", ""));

if (args.Contains("--forget-credential"))
{
    var name = Arg("--forget-credential", "");
    if (name.Length == 0)
    {
        log.LogError("--forget-credential exige o nome, ex.: --forget-credential AZURE_SPEECH_KEY");
        return 1;
    }
    vault.Delete(name);
    Console.WriteLine($"{name}: removido do cofre do Windows.");
    return 0;
}

// --- settings: saved file, overridden by whatever came on the command line ----

var saved = settingsStore.Load();

DeviceSelection Device(string option, DeviceSelection fallback) =>
    args.Contains(option) && Arg(option, "") is { Length: > 0 } value
        // The value may be an endpoint id or a fragment of the name; the planner tries
        // the name first and falls back to the id, so both are offered.
        ? new DeviceSelection(value, value)
        : fallback;

var settings = saved with
{
    Provider = Arg("--provider", saved.Provider).Trim().ToLowerInvariant(),
    FromLanguage = Arg("--from", saved.FromLanguage),
    ToLanguage = Arg("--to", saved.ToLanguage),
    OutboundVoice = Arg("--voice", saved.OutboundVoice),
    InboundVoice = Arg("--voice-in", saved.InboundVoice),
    Microphone = Device("--mic", saved.Microphone),
    Loopback = Device("--loopback", saved.Loopback),
    Phones = Device("--phones", saved.Phones)
};

if (settings.Provider is not ("azure" or "gemini"))
{
    log.LogError("--provider \"{Provider}\": use \"azure\" ou \"gemini\".", settings.Provider);
    return 1;
}

if (args.Contains("--save-config"))
{
    settingsStore.Save(settings);
    Console.WriteLine($"Configuração salva em {settingsStore.Path}");
    PrintConfig(settings);
    return 0;
}

if (args.Contains("--show-config"))
{
    Console.WriteLine($"Versão: {AppVersion.Current}");
    Console.WriteLine($"Arquivo: {settingsStore.Path}{(File.Exists(settingsStore.Path) ? "" : " (ainda não existe)")}");
    PrintConfig(settings);
    return 0;
}

if (args.Contains("--list-voices"))
{
    var key = credentials.Read("AZURE_SPEECH_KEY");
    var region = credentials.Read("AZURE_SPEECH_REGION");
    if (key is null || region is null)
    {
        log.LogError("--list-voices precisa de AZURE_SPEECH_KEY e AZURE_SPEECH_REGION (ver --set-credential).");
        return 1;
    }

    var filter = Arg("--list-voices", "");
    var wanted = filter.Length > 0 && !filter.StartsWith("--")
        ? [filter]
        : new[] { settings.FromLanguage, settings.ToLanguage };

    foreach (var locale in wanted)
    {
        Console.WriteLine($"\n=== {locale}");
        foreach (var v in await AzureVoices.ListAsync(key, region, locale))
            Console.WriteLine($"  {(v.IsHighDefinition ? "HD" : "  ")} {v.Gender,-7} {v.ShortName}");
    }
    Console.WriteLine("\nUse o nome em --voice (idioma de saída) ou --voice-in (idioma de entrada).");
    return 0;
}

// --- device check: proves the routing without touching the cloud --------------

var plan = SessionPlanner.Prepare(settings, platform.Devices.ListDevices());
if (args.Contains("--check-audio"))
{
    if (plan.Plan is null)
    {
        foreach (var p in plan.Problems)
            log.LogError("{Field}: {Message}", p.Field, p.Message);
        return 1;
    }
    return await CheckAudioAsync(plan.Plan);
}

// --- run ----------------------------------------------------------------------

if (!settings.UsesAzure && (args.Contains("--voice") || args.Contains("--voice-in")))
    log.LogWarning("--voice/--voice-in são ignorados com --provider gemini: o modo de tradução não permite escolher a voz.");

log.LogInformation("Provedor: {Provider}", settings.UsesAzure
    ? $"Azure (região {credentials.Read("AZURE_SPEECH_REGION") ?? "não configurada"})"
    : $"Gemini Live tradução (modelo {credentials.Read("GEMINI_LIVE_MODEL") ?? "padrão"})");

await using var session = new DubbingSession(platform, credentials, loggerFactory);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var faulted = false;
session.Faulted += (_, _) => { faulted = true; cts.Cancel(); };

var problems = await session.StartAsync(settings, cts.Token);
if (problems.Count > 0)
{
    foreach (var p in problems)
        log.LogError("{Field}: {Message}", p.Field, p.Message);
    return 1;
}

log.LogInformation("Rodando. No Meet/Teams selecione \"CABLE Output\" como microfone. [M] mute outbound, Ctrl+C sai.");

var interactive = !Console.IsInputRedirected;
while (!cts.IsCancellationRequested)
{
    if (interactive && Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.M)
    {
        session.OutboundMuted = !session.OutboundMuted;
        log.LogInformation("Outbound {State}", session.OutboundMuted ? "MUTED" : "ativo");
    }
    await Task.Delay(100).ConfigureAwait(false);
}

foreach (var pipeline in new[] { session.Outbound, session.Inbound })
    ReportLatency(pipeline);

await session.StopAsync();
return faulted ? 2 : 0;

// -----------------------------------------------------------------------------

void ReportLatency(DubbingPipeline? pipeline)
{
    if (pipeline is null)
        return;

    if (pipeline.Latency.Count > 0)
        log.LogInformation("[{Pipeline}] latência: n={Count} última={Last:F2}s p95={P95:F2}s (meta ≤ 3 s)",
            pipeline.Name, pipeline.Latency.Count,
            pipeline.Latency.Last!.Value.TotalSeconds, pipeline.Latency.P95!.Value.TotalSeconds);
    else
        log.LogInformation("[{Pipeline}] latência: nenhuma frase completa medida", pipeline.Name);
}

void PrintConfig(PolyglottaSettings s)
{
    Console.WriteLine($"  provedor   : {s.Provider}");
    Console.WriteLine($"  idiomas    : {s.FromLanguage} -> {s.ToLanguage}");
    Console.WriteLine($"  vozes      : saída {s.OutboundVoice}, entrada {s.InboundVoice}");
    Console.WriteLine($"  microfone  : {s.Microphone}");
    Console.WriteLine($"  loopback   : {s.Loopback}");
    Console.WriteLine($"  fone       : {s.Phones}");
    Console.WriteLine($"  mic virtual: {s.VirtualMicName}");
    Console.WriteLine($"  ativar ao iniciar: {(s.ActivateOnStartup ? "sim" : "não")}");
    Console.WriteLine("\n  credenciais:");
    foreach (var name in TranslatorFactory.CredentialNames(s))
    {
        var value = credentials.Read(name);
        var origin = value is null ? "não configurada"
            : credentials.ComesFromReadOnlySource(name) ? $"variável de ambiente ({Mask(value)})"
            : $"cofre do Windows ({Mask(value)})";
        Console.WriteLine($"    {name,-22} {origin}");
    }
}

/// <summary>Shows just enough of a secret to recognize it, never enough to use it.</summary>
static string Mask(string value) =>
    value.Length <= 4 ? new string('*', value.Length) : $"...{value[^4..]}";

int SetCredential(string name)
{
    if (name.Length == 0 || name.StartsWith("--"))
    {
        log.LogError("--set-credential exige o nome, ex.: --set-credential AZURE_SPEECH_KEY");
        return 1;
    }

    if (Console.IsInputRedirected)
    {
        log.LogError("--set-credential precisa de um terminal interativo para ler o valor sem ecoá-lo.");
        return 1;
    }

    Console.Write($"{name}: ");
    var value = ReadSecret();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(value))
    {
        log.LogError("Valor vazio; nada foi salvo.");
        return 1;
    }

    vault.Save(name, value);
    Console.WriteLine($"{name} salvo cifrado em {UserPaths.ConfigDirectory}\\credentials.json ({Mask(value)}).");

    if (EnvironmentSettings.Read(name) is not null)
        Console.WriteLine(
            $"Atenção: a variável de ambiente {name} existe e tem precedência sobre o valor salvo.");

    return 0;
}

/// <summary>Reads a line without echoing it, so the key never lands in the scrollback.</summary>
static string ReadSecret()
{
    var value = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            return value.ToString();
        if (key.Key == ConsoleKey.Backspace)
        {
            if (value.Length > 0)
                value.Length--;
            continue;
        }
        if (!char.IsControl(key.KeyChar))
            value.Append(key.KeyChar);
    }
}

void PrintHelp() => Console.WriteLine("""
    Polyglotta — dublagem simultânea em chamadas

      --list-devices                 lista dispositivos de captura e reprodução
      --list-voices [locale]         lista as vozes do Azure (HD primeiro)
      --check-audio [segundos]       testa mic, loopback e saídas sem usar a nuvem

      --provider azure|gemini        provedor de tradução (ADR-0006)
      --from pt-BR  --to en-US       par de idiomas
      --voice male|female|<nome>     voz que o interlocutor ouve
      --voice-in male|female|<nome>  voz que você ouve
      --mic / --loopback / --phones  dispositivos, por nome (fragmento) ou id

      --show-config                  mostra a configuração efetiva e as credenciais
      --save-config                  grava as opções acima como configuração padrão
      --set-credential NOME          salva uma chave cifrada (não ecoa o valor)
      --forget-credential NOME       remove a chave salva

    Sem argumentos, usa a configuração salva. Opções de linha de comando têm
    precedência sobre o arquivo; variáveis de ambiente têm precedência sobre as
    credenciais salvas.
    """);

// -----------------------------------------------------------------------------
// --check-audio: proves the device setup end to end without touching the cloud.
// Reads the microphone and the loopback tap, then plays a short tone on each
// output so the whole path can be verified before a real call.
// -----------------------------------------------------------------------------
async Task<int> CheckAudioAsync(SessionPlan p)
{
    var raw = Arg("--check-audio", "8");
    var seconds = int.TryParse(raw, out var s) ? Math.Clamp(s, 3, 60) : 8;

    Console.WriteLine($"\nMicrofone : {p.Microphone.Name}");
    Console.WriteLine($"Loopback  : {p.Loopback.Name}");
    Console.WriteLine($"Fone      : {p.Phones.Name}");
    Console.WriteLine($"Mic virtual: {p.VirtualMic.Name}\n");
    Console.WriteLine($"FALE AO MICROFONE pelos próximos {seconds}s (e deixe algo tocando no loopback).\n");

    await using var micCapture = platform.CreateMicrophoneCapture(p.Microphone.Id);
    await using var loopCapture = platform.CreateLoopbackCapture(p.Loopback.Id);
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
    Report("Microfone", p.Microphone.Name, micFrames, micVoiced, micPeak);
    Report("Loopback", p.Loopback.Name, loopFrames, loopVoiced, loopPeak);

    Console.WriteLine("\nTocando 1 s de tom em cada saída...");
    await PlayToneAsync(p.Phones, "você deve ouvir no fone");
    await PlayToneAsync(p.VirtualMic, "vai para o Meet/Teams como microfone");
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

    async Task PlayToneAsync(AudioDeviceInfo device, string hint)
    {
        Console.WriteLine($"  {device.Name} ({hint})");
        var pcm = new byte[AudioFrame.SampleRate * 2]; // 1 s, 16-bit mono
        for (var i = 0; i < pcm.Length / 2; i++)
        {
            var sample = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / AudioFrame.SampleRate));
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), sample);
        }
        await using var render = platform.CreateRenderDevice(device.Id);
        await render.StartAsync();
        render.Enqueue(pcm);
        await Task.Delay(1400);
        await render.StopAsync();
    }
}
