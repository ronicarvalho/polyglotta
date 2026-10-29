using Microsoft.Extensions.Logging;
using Polyglotta.Core.Audio;
using Polyglotta.Core.Configuration;
using Polyglotta.Core.Pipeline;
using Polyglotta.Core.Translation;

namespace Polyglotta.Host;

public enum SessionState { Stopped, Starting, Running, Stopping, Faulted }

/// <summary>A transcript line, tagged with which direction produced it (RF-06).</summary>
public sealed record SessionTranscript(string Pipeline, TranscriptEvent Event);

/// <summary>
/// The whole dubbing session: both pipelines, their devices and their provider,
/// driven by a <see cref="PolyglottaSettings"/> (ADR-0007).
/// <para>
/// This is what "ativar" and "desativar" in the tray do, and what the CLI runs. It
/// knows nothing about Windows and nothing about any UI: a fake
/// <see cref="IAudioPlatform"/> drives it in tests.
/// </para>
/// </summary>
public sealed class DubbingSession : IAsyncDisposable
{
    private readonly IAudioPlatform _audio;
    private readonly TranslatorFactory _translators;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    private DubbingPipeline? _outbound;
    private DubbingPipeline? _inbound;
    private bool _muted;
    private bool _disposed;

    public DubbingSession(IAudioPlatform audio, ICredentialStore credentials, ILoggerFactory loggerFactory)
    {
        _audio = audio;
        _translators = new TranslatorFactory(credentials);
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<DubbingSession>();
    }

    public SessionState State { get; private set; } = SessionState.Stopped;

    public event EventHandler<SessionState>? StateChanged;
    public event EventHandler<SessionTranscript>? Transcript;

    /// <summary>
    /// A stage died while running. The session is already stopping when this fires;
    /// automatic recovery (RF-07) belongs above here.
    /// </summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>Speak without being translated (RF-05). Survives across start/stop.</summary>
    public bool OutboundMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (_outbound is not null)
                _outbound.Muted = value;
        }
    }

    public DubbingPipeline? Outbound => _outbound;
    public DubbingPipeline? Inbound => _inbound;

    /// <summary>
    /// Checks the settings against the devices actually present, without touching
    /// audio or the network. This is what the configuration screen calls while editing.
    /// </summary>
    public IReadOnlyList<SessionProblem> Validate(PolyglottaSettings settings)
    {
        var problems = new List<SessionProblem>(
            SessionPlanner.Prepare(settings, _audio.Devices.ListDevices()).Problems);
        problems.AddRange(_translators.MissingCredentials(settings));
        return problems;
    }

    /// <summary>
    /// Starts both pipelines. Returns the problems that prevented it; an empty list
    /// means the session is running.
    /// </summary>
    public async Task<IReadOnlyList<SessionProblem>> StartAsync(
        PolyglottaSettings settings, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_outbound is not null)
                return [];

            var plan = SessionPlanner.Prepare(settings, _audio.Devices.ListDevices());
            var problems = new List<SessionProblem>(plan.Problems);
            problems.AddRange(_translators.MissingCredentials(settings));
            if (problems.Count > 0 || plan.Plan is null)
                return problems;

            SetState(SessionState.Starting);
            try
            {
                await StartPipelinesAsync(settings, plan.Plan, ct);
                SetState(SessionState.Running);
                return [];
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Falha ao iniciar a sessão.");
                await TearDownAsync();
                SetState(SessionState.Faulted);
                return [new SessionProblem("provider", ex.Message)];
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StartPipelinesAsync(PolyglottaSettings settings, SessionPlan plan, CancellationToken ct)
    {
        var (outVoice, inVoice) = await _translators.ResolveVoicesAsync(settings, ct);

        _log.LogInformation("Outbound: {Mic} -> {From}->{To} ({Voice}) -> {Cable}",
            plan.Microphone.Name, settings.FromLanguage, settings.ToLanguage,
            outVoice ?? "voz padrão", plan.VirtualMic.Name);
        _log.LogInformation("Inbound : loopback({Loopback}) -> {To}->{From} ({Voice}) -> {Phones}",
            plan.Loopback.Name, settings.ToLanguage, settings.FromLanguage,
            inVoice ?? "voz padrão", plan.Phones.Name);

        _outbound = Build("outbound",
            _audio.CreateMicrophoneCapture(plan.Microphone.Id),
            _audio.CreateRenderDevice(plan.VirtualMic.Id),
            settings);

        _inbound = Build("inbound",
            _audio.CreateLoopbackCapture(plan.Loopback.Id),
            _audio.CreateRenderDevice(plan.Phones.Id),
            settings);

        _outbound.Muted = _muted;

        await _outbound.StartAsync(new TranslatorOptions(settings.FromLanguage, settings.ToLanguage, outVoice), ct);
        await _inbound.StartAsync(new TranslatorOptions(settings.ToLanguage, settings.FromLanguage, inVoice), ct);
    }

    private DubbingPipeline Build(
        string name, IAudioCaptureDevice capture, IAudioRenderDevice render, PolyglottaSettings settings)
    {
        var pipeline = new DubbingPipeline(
            name, capture, _translators.Create(settings), render,
            _loggerFactory.CreateLogger<DubbingPipeline>());

        pipeline.Transcript += (_, e) => Transcript?.Invoke(this, new SessionTranscript(name, e));
        pipeline.Faulted += OnPipelineFaulted;
        return pipeline;
    }

    /// <summary>
    /// A faulted stage ends the session. Handed to a worker on purpose: this runs on a
    /// provider or device callback, which must never be blocked by a teardown.
    /// </summary>
    private void OnPipelineFaulted(object? sender, Exception ex)
    {
        var name = (sender as DubbingPipeline)?.Name ?? "pipeline";
        _log.LogError(ex, "[{Pipeline}] falhou; encerrando a sessão.", name);

        _ = Task.Run(async () =>
        {
            try
            {
                await StopAsync();
            }
            catch (Exception stopError)
            {
                _log.LogError(stopError, "Falha ao encerrar a sessão após erro.");
            }
            finally
            {
                SetState(SessionState.Faulted);
                Faulted?.Invoke(this, ex);
            }
        });
    }

    /// <summary>Stops both pipelines and releases the devices. Idempotent.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_outbound is null && _inbound is null)
                return;

            SetState(SessionState.Stopping);
            await TearDownAsync();
            SetState(SessionState.Stopped);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Stops and disposes whatever exists. A failure to stop one stage is logged and
    /// does not prevent the other from being released.
    /// </summary>
    private async Task TearDownAsync()
    {
        foreach (var pipeline in new[] { _outbound, _inbound })
        {
            if (pipeline is null)
                continue;

            pipeline.Faulted -= OnPipelineFaulted;
            try
            {
                await pipeline.StopAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[{Pipeline}] erro ao parar.", pipeline.Name);
            }

            await pipeline.DisposeAsync();
        }

        _outbound = null;
        _inbound = null;
    }

    private void SetState(SessionState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await StopAsync();
        _lifecycle.Dispose();
    }
}
