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

foreach (var name in new[] { "AZURE_SPEECH_KEY", "AZURE_SPEECH_REGION" })
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
    {
        log.LogError("Variável de ambiente {Name} não definida (ver README).", name);
        return 1;
    }
}

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

var devices = enumerator.ListDevices();

AudioDeviceInfo? Resolve(string option, AudioDeviceKind kind)
{
    var id = Arg(option, "");
    if (id.Length == 0)
        return devices.FirstOrDefault(d => d.Kind == kind && d.IsDefault);
    var found = devices.FirstOrDefault(d => d.Id == id && d.Kind == kind);
    if (found is null)
        log.LogError("{Option} {Id}: dispositivo de {Kind} não encontrado. Use --list-devices.", option, id, kind);
    return found;
}

// Inbound source: the render device Meet/Teams plays on (tapped via WASAPI loopback).
var loopback = Resolve("--loopback", AudioDeviceKind.Render);
// Inbound playback: the user's headphones.
var phones = Resolve("--phones", AudioDeviceKind.Render);
if (loopback is null || phones is null)
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

log.LogInformation("Outbound: mic -> {From}->{To} -> {Cable}", fromLang, toLang, cable.Name);
log.LogInformation("Inbound : loopback({Loopback}) -> {To}->{From} -> {Phones}", loopback.Name, toLang, fromLang, phones.Name);

await using var outbound = new DubbingPipeline(
    "outbound",
    new WasapiMicCapture(),
    AzureSpeechTranslator.FromEnvironment(),
    new WasapiRenderDevice(cable.Id),
    loggerFactory.CreateLogger<DubbingPipeline>());

await using var inbound = new DubbingPipeline(
    "inbound",
    new WasapiLoopbackSystemCapture(loopback.Id),
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
