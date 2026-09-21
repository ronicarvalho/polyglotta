using Polyglotta.Audio.Windows;
using Polyglotta.Core.Audio;
using Polyglotta.Core.Pipeline;
using Polyglotta.Core.Translation;
using Polyglotta.Translation.Azure;
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

var fromLang = Arg("--from", "pt-BR");
var toLang = Arg("--to", "en-US");
var outVoice = Arg("--voice", "en-US-JennyNeural");
var inVoice = Arg("--voice-in", "pt-BR-FranciscaNeural");

// Outbound render target: the VB-Cable virtual mic input (ADR-0002).
var cable = enumerator.FindByName("CABLE Input", AudioDeviceKind.Render);
if (cable is null)
{
    log.LogError("Dispositivo \"CABLE Input\" não encontrado. Instale o VB-Cable: https://vb-audio.com/Cable/");
    return 1;
}

// Inbound playback: user's headphones. IMPORTANT: to avoid a feedback loop,
// this must NOT be the same device captured by loopback (audio-realtime skill).
var phonesId = Arg("--phones", "");
var phones = phonesId.Length > 0
    ? enumerator.ListDevices().First(d => d.Id == phonesId)
    : enumerator.ListDevices().First(d => d.Kind == AudioDeviceKind.Render && d.IsDefault);

log.LogInformation("Outbound: mic -> {From}->{To} -> {Cable}", fromLang, toLang, cable.Name);
log.LogInformation("Inbound : loopback -> {To}->{From} -> {Phones}", toLang, fromLang, phones.Name);

await using var outbound = new DubbingPipeline(
    "outbound",
    new WasapiMicCapture(),
    AzureSpeechTranslator.FromEnvironment(),
    new WasapiRenderDevice(cable.Id),
    loggerFactory.CreateLogger<DubbingPipeline>());

await using var inbound = new DubbingPipeline(
    "inbound",
    new WasapiLoopbackSystemCapture(),
    AzureSpeechTranslator.FromEnvironment(),
    new WasapiRenderDevice(phones.Id),
    loggerFactory.CreateLogger<DubbingPipeline>());

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await outbound.StartAsync(new TranslatorOptions(fromLang, toLang, outVoice), cts.Token);
await inbound.StartAsync(new TranslatorOptions(toLang, fromLang, inVoice), cts.Token);

log.LogInformation("Rodando. No Meet/Teams selecione \"CABLE Output\" como microfone. [M] mute outbound, Ctrl+C sai.");

while (!cts.IsCancellationRequested)
{
    if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.M)
    {
        outbound.Muted = !outbound.Muted;
        log.LogInformation("Outbound {State}", outbound.Muted ? "MUTED" : "ativo");
    }
    await Task.Delay(100).ConfigureAwait(false);
}

await outbound.StopAsync();
await inbound.StopAsync();
return 0;
