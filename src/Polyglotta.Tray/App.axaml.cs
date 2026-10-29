using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Polyglotta.Audio.Windows;
using Polyglotta.Core.Configuration;
using Polyglotta.Host;
using Polyglotta.Security.Windows;

namespace Polyglotta.Tray;

/// <summary>
/// The background agent itself (ADR-0007): owns the tray icon, the session and the
/// configuration window. There is no main window — the app lives in the tray, and
/// closing the configuration window does not end it.
/// </summary>
public partial class App : Application
{
    private readonly WindowsAudioPlatform _platform = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly DpapiCredentialStore _vault = new();

    private ChainedCredentialStore _credentials = null!;
    private ILoggerFactory _loggerFactory = null!;
    private ILogger _log = null!;
    private DubbingSession _session = null!;
    private VoicePreview _preview = null!;

    private TrayIcon? _tray;
    private NativeMenuItem? _toggleItem;
    private ConfigWindow? _config;
    private TranscriptWindow? _transcript;
    private PolyglottaSettings _settings = new();

    /// <summary>Latest transcript lines, for the configuration window to show (RF-06).</summary>
    public TranscriptLog Transcripts { get; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No main window: the tray is the application.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += async (_, _) => await _session.StopAsync();

            // Environment first, OS vault second (ADR-0009).
            _credentials = new ChainedCredentialStore(new EnvironmentCredentialStore(), _vault);
            _loggerFactory = FileLogger.CreateFactory(
                System.IO.Path.Combine(UserPaths.ConfigDirectory, "logs"));
            _log = _loggerFactory.CreateLogger("tray");

            _settings = _settingsStore.Load();
            _session = new DubbingSession(_platform, _credentials, _loggerFactory);
            _preview = new VoicePreview(_platform, _credentials);
            _session.StateChanged += (_, state) => Dispatcher.UIThread.Post(() => OnStateChanged(state));
            _session.Transcript += (_, t) => Transcripts.Add(t);
            _session.Faulted += (_, ex) => Dispatcher.UIThread.Post(() => OnFaulted(ex));

            BuildTray();
            _log.LogInformation("Polyglotta na bandeja. Configuração: {Path}", _settingsStore.Path);

            if (_settings.ActivateOnStartup)
                _ = ActivateAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // --- tray -----------------------------------------------------------------

    private void BuildTray()
    {
        _toggleItem = new NativeMenuItem("Ativar");
        _toggleItem.Click += async (_, _) => await GuardedAsync(ToggleAsync, "ativar/desativar");

        var configure = new NativeMenuItem("Configurar...");
        configure.Click += (_, _) => Guarded(ShowConfig, "abrir a configuração");

        var transcript = new NativeMenuItem("Transcrição...");
        transcript.Click += (_, _) => Guarded(ShowTranscript, "abrir a transcrição");

        var quit = new NativeMenuItem("Fechar");
        quit.Click += async (_, _) => await GuardedAsync(QuitAsync, "fechar");

        var menu = new NativeMenu();
        menu.Add(_toggleItem);
        menu.Add(configure);
        menu.Add(transcript);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quit);

        _tray = new TrayIcon
        {
            Icon = LoadIcon("idle"),
            ToolTipText = "Polyglotta — parado",
            Menu = menu,
            IsVisible = true
        };

        // macOS does not raise Clicked (ADR-0008), so the menu is the only control path
        // there; on Windows a click opening the settings is the expected shortcut.
        _tray.Clicked += (_, _) => Guarded(ShowConfig, "abrir a configuração");

        TrayIcon.SetIcons(this, [_tray]);
    }

    /// <summary>
    /// An exception escaping a tray handler takes the whole agent down with it — the
    /// user is left with no icon and no message. Log it and stay alive instead.
    /// </summary>
    private void Guarded(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao {What}.", what);
        }
    }

    private async Task GuardedAsync(Func<Task> action, string what)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao {What}.", what);
        }
    }

    private static WindowIcon LoadIcon(string state) =>
        new(AssetLoader.Open(new Uri($"avares://Polyglotta.Tray/Assets/icon-{state}.png")));

    private void OnStateChanged(SessionState state)
    {
        if (_tray is null || _toggleItem is null)
            return;

        var (icon, text) = state switch
        {
            SessionState.Running => ("active", "ativo"),
            SessionState.Starting => ("idle", "iniciando..."),
            SessionState.Stopping => ("idle", "parando..."),
            SessionState.Faulted => ("error", "erro — veja Configurar"),
            _ => ("idle", "parado")
        };

        _tray.Icon = LoadIcon(icon);
        _tray.ToolTipText = $"Polyglotta — {text}";
        _toggleItem.Header = state == SessionState.Running ? "Desativar" : "Ativar";
        _toggleItem.IsEnabled = state is SessionState.Running or SessionState.Stopped or SessionState.Faulted;

        _config?.OnSessionStateChanged(state);
    }

    private void OnFaulted(Exception ex)
    {
        _log.LogError(ex, "Sessão encerrada por falha.");
        // RF-07 (reconnection) is not implemented: the user has to be told, not left
        // staring at an icon that looks fine.
        ShowConfig();
        _config?.ShowProblems([new SessionProblem("provider", $"A sessão caiu: {ex.Message}")]);
    }

    // --- commands --------------------------------------------------------------

    private async Task ToggleAsync()
    {
        if (_session.State == SessionState.Running)
            await _session.StopAsync();
        else
            await ActivateAsync();
    }

    /// <summary>Starts the session, or opens the configuration on whatever is missing.</summary>
    public async Task ActivateAsync()
    {
        var problems = await _session.StartAsync(_settings);
        if (problems.Count == 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            ShowConfig();
            _config?.ShowProblems(problems);
        });
    }

    public Task DeactivateAsync() => _session.StopAsync();

    private async Task QuitAsync()
    {
        _log.LogInformation("Encerrando a pedido do usuário.");
        await _session.DisposeAsync();
        _tray?.Dispose();
        _loggerFactory.Dispose();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.TryShutdown();
    }

    // --- configuration window ---------------------------------------------------

    private void ShowConfig()
    {
        if (_config is null)
        {
            _config = new ConfigWindow(this);
            // Hidden instead of destroyed, so reopening is instant and state survives.
            _config.Closing += (_, e) =>
            {
                e.Cancel = true;
                _config.Hide();
            };
        }

        _config.Show();
        _config.Activate();
        _config.OnSessionStateChanged(_session.State);
    }

    private void ShowTranscript()
    {
        if (_transcript is null)
        {
            _transcript = new TranscriptWindow(this);
            _transcript.Closing += (_, e) =>
            {
                e.Cancel = true;
                _transcript.Hide();
            };
        }

        _transcript.Show();
        _transcript.Activate();
    }

    // --- services the window needs ------------------------------------------------

    public IReadOnlyList<Core.Audio.AudioDeviceInfo> ListDevices() => _platform.Devices.ListDevices();
    public ChainedCredentialStore Credentials => _credentials;
    public DpapiCredentialStore Vault => _vault;
    public DubbingSession Session => _session;
    public VoicePreview Preview => _preview;

    public PolyglottaSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _settingsStore.Save(value);
            _log.LogInformation("Configuração salva.");
        }
    }

    public string SettingsPath => _settingsStore.Path;
}
