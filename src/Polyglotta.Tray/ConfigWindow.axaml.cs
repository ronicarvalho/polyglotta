using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Polyglotta.Core.Audio;
using Polyglotta.Host;

namespace Polyglotta.Tray;

/// <summary>
/// The "Configurar" screen. Edits the same <see cref="PolyglottaSettings"/> the CLI
/// reads, and validates through the same <see cref="SessionPlanner"/> — so the routing
/// guards cannot drift between the two frontends.
/// </summary>
public partial class ConfigWindow : Window
{
    /// <summary>Wraps a device so the combo shows the name instead of the record dump.</summary>
    private sealed record DeviceOption(AudioDeviceInfo Device)
    {
        public override string ToString() => Device.Name;
    }

    private static readonly string[] Locales = ["pt-BR", "en-US", "es-ES", "fr-FR", "de-DE", "it-IT"];
    private static readonly string[] Voices = ["female", "male"];

    private readonly App _app;
    private readonly DispatcherTimer _ticker;
    private bool _loading;

    public ConfigWindow() : this(null!)
    {
    }

    public ConfigWindow(App app)
    {
        _app = app;
        InitializeComponent();

        ProviderBox.ItemsSource = new[] { "azure", "gemini" };
        FromBox.ItemsSource = Locales;
        ToBox.ItemsSource = Locales;
        VoiceOutBox.ItemsSource = Voices;
        VoiceInBox.ItemsSource = Voices;
        TranscriptList.ItemsSource = _app.Transcripts.Lines;
        ConfigPathText.Text = $"Configuração: {_app.SettingsPath}";

        LoadDevices();
        ApplySettings(_app.Settings);

        ProviderBox.SelectionChanged += (_, _) => OnProviderChanged();
        RefreshDevicesButton.Click += (_, _) => LoadDevices(keepSelection: true);
        ValidateButton.Click += (_, _) => Validate();
        SaveButton.Click += (_, _) => Save();
        ToggleButton.Click += async (_, _) => await ToggleAsync();
        SaveCredentialButton.Click += (_, _) => SaveCredential();
        ForgetCredentialButton.Click += (_, _) => ForgetCredential();
        CredentialNameBox.SelectionChanged += (_, _) => RefreshCredentialStatus();
        ClearTranscriptButton.Click += (_, _) => _app.Transcripts.Clear();
        AutostartBox.IsCheckedChanged += (_, _) => OnAutostartToggled();

        // Latency is read, not pushed: a timer keeps the footer current without
        // touching the pipeline from the UI thread on every synthesized chunk.
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _ticker.Tick += (_, _) => RefreshLatency();
        _ticker.Start();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // --- loading -------------------------------------------------------------

    private void LoadDevices(bool keepSelection = false)
    {
        var previous = keepSelection
            ? (Mic: Selected(MicBox), Loop: Selected(LoopbackBox), Phones: Selected(PhonesBox))
            : default;

        var devices = _app.ListDevices();
        MicBox.ItemsSource = devices.Where(d => d.Kind == AudioDeviceKind.Capture).Select(d => new DeviceOption(d)).ToList();
        LoopbackBox.ItemsSource = devices.Where(d => d.Kind == AudioDeviceKind.Render).Select(d => new DeviceOption(d)).ToList();
        PhonesBox.ItemsSource = devices.Where(d => d.Kind == AudioDeviceKind.Render).Select(d => new DeviceOption(d)).ToList();

        if (!keepSelection)
            return;

        Select(MicBox, previous.Mic);
        Select(LoopbackBox, previous.Loop);
        Select(PhonesBox, previous.Phones);
    }

    private void ApplySettings(PolyglottaSettings settings)
    {
        _loading = true;
        try
        {
            ProviderBox.SelectedItem = settings.UsesAzure ? "azure" : "gemini";
            FromBox.SelectedItem = Locales.Contains(settings.FromLanguage) ? settings.FromLanguage : Locales[0];
            ToBox.SelectedItem = Locales.Contains(settings.ToLanguage) ? settings.ToLanguage : Locales[1];
            VoiceOutBox.SelectedItem = Voices.Contains(settings.OutboundVoice) ? settings.OutboundVoice : Voices[0];
            VoiceInBox.SelectedItem = Voices.Contains(settings.InboundVoice) ? settings.InboundVoice : Voices[0];

            Select(MicBox, settings.Microphone);
            Select(LoopbackBox, settings.Loopback);
            Select(PhonesBox, settings.Phones);

            ActivateOnStartupBox.IsChecked = settings.ActivateOnStartup;
            AutostartBox.IsChecked = WindowsAutostart.IsEnabled();
        }
        finally
        {
            _loading = false;
        }

        OnProviderChanged();
    }

    private PolyglottaSettings CollectSettings() => _app.Settings with
    {
        Provider = ProviderBox.SelectedItem as string ?? "azure",
        FromLanguage = FromBox.SelectedItem as string ?? "pt-BR",
        ToLanguage = ToBox.SelectedItem as string ?? "en-US",
        OutboundVoice = VoiceOutBox.SelectedItem as string ?? "female",
        InboundVoice = VoiceInBox.SelectedItem as string ?? "female",
        Microphone = Selected(MicBox),
        Loopback = Selected(LoopbackBox),
        Phones = Selected(PhonesBox),
        ActivateOnStartup = ActivateOnStartupBox.IsChecked == true
    };

    private static DeviceSelection Selected(ComboBox box) =>
        box.SelectedItem is DeviceOption option ? DeviceSelection.From(option.Device) : new DeviceSelection();

    private static void Select(ComboBox box, DeviceSelection selection)
    {
        if (selection.IsEmpty || box.ItemsSource is not IEnumerable<DeviceOption> options)
            return;

        var list = options.ToList();
        box.SelectedItem =
            list.FirstOrDefault(o => o.Device.Id == selection.Id)
            ?? list.FirstOrDefault(o => selection.Name is not null && o.Device.Name.Contains(selection.Name, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(o => o.Device.IsDefault);
    }

    // --- actions --------------------------------------------------------------

    private void OnProviderChanged()
    {
        var settings = CollectSettings();

        // Gemini's translation mode has no voice selection (ADR-0006).
        var azure = settings.UsesAzure;
        VoiceOutBox.IsEnabled = azure;
        VoiceInBox.IsEnabled = azure;
        VoiceNote.IsVisible = !azure;

        CredentialNameBox.ItemsSource = TranslatorFactory.CredentialNames(settings);
        CredentialNameBox.SelectedIndex = 0;
        RefreshCredentialStatus();
    }

    private void Validate() => ShowProblems(_app.Session.Validate(CollectSettings()));

    private void Save()
    {
        _app.Settings = CollectSettings();
        Validate();
    }

    private async Task ToggleAsync()
    {
        // Saving first is what the user means by "ativar": the screen is the source of truth.
        _app.Settings = CollectSettings();

        if (_app.Session.State == SessionState.Running)
            await _app.DeactivateAsync();
        else
            await _app.ActivateAsync();
    }

    /// <summary>Shows what is blocking the session, or confirms that nothing is.</summary>
    public void ShowProblems(IReadOnlyList<SessionProblem> problems)
    {
        if (problems.Count == 0)
        {
            ProblemsList.ItemsSource = new[] { "Tudo certo: dispositivos resolvidos e credenciais presentes." };
            ProblemsPanel.IsVisible = true;
            return;
        }

        ProblemsList.ItemsSource = problems.Select(p => $"• {p.Message}").ToList();
        ProblemsPanel.IsVisible = true;
    }

    public void OnSessionStateChanged(SessionState state)
    {
        StateText.Text = state switch
        {
            SessionState.Running => "ativo — dublando",
            SessionState.Starting => "iniciando...",
            SessionState.Stopping => "parando...",
            SessionState.Faulted => "erro — a sessão caiu",
            _ => "parado"
        };

        ToggleButton.Content = state == SessionState.Running ? "Desativar" : "Ativar";
        ToggleButton.IsEnabled = state is SessionState.Running or SessionState.Stopped or SessionState.Faulted;
        RefreshLatency();
    }

    private void RefreshLatency()
    {
        var parts = new List<string>();
        foreach (var pipeline in new[] { _app.Session.Outbound, _app.Session.Inbound })
        {
            if (pipeline?.Latency.P95 is { } p95)
                parts.Add($"{pipeline.Name}: p95 {p95.TotalSeconds:F2}s (n={pipeline.Latency.Count})");
        }

        LatencyText.Text = parts.Count > 0
            ? string.Join("   ", parts) + "   — meta ≤ 3 s"
            : "";
    }

    // --- credentials -----------------------------------------------------------

    private string? CurrentCredentialName => CredentialNameBox.SelectedItem as string;

    private void SaveCredential()
    {
        if (CurrentCredentialName is not { } name)
            return;

        var value = CredentialValueBox.Text;
        if (string.IsNullOrWhiteSpace(value))
        {
            CredentialStatus.Text = "Digite o valor antes de salvar.";
            return;
        }

        _app.Vault.Save(name, value);
        CredentialValueBox.Text = "";
        RefreshCredentialStatus();
    }

    private void ForgetCredential()
    {
        if (CurrentCredentialName is { } name)
        {
            _app.Vault.Delete(name);
            RefreshCredentialStatus();
        }
    }

    private void RefreshCredentialStatus()
    {
        if (CurrentCredentialName is not { } name)
        {
            CredentialStatus.Text = "";
            return;
        }

        var value = _app.Credentials.Read(name);
        CredentialStatus.Text = value is null
            ? $"{name}: não configurado."
            : _app.Credentials.ComesFromReadOnlySource(name)
                ? $"{name}: vindo da variável de ambiente ({Mask(value)}) — ela tem precedência sobre o que for salvo aqui."
                : $"{name}: salvo e cifrado ({Mask(value)}).";
    }

    /// <summary>Enough of the secret to recognize it, never enough to use it.</summary>
    private static string Mask(string value) =>
        value.Length <= 4 ? new string('*', value.Length) : $"...{value[^4..]}";

    // --- startup ----------------------------------------------------------------

    private void OnAutostartToggled()
    {
        if (_loading)
            return;

        var wanted = AutostartBox.IsChecked == true;
        var error = wanted ? WindowsAutostart.Enable() : WindowsAutostart.Disable();

        if (error is null)
        {
            AutostartStatus.Text = wanted
                ? $"Tarefa agendada \"{WindowsAutostart.TaskName}\" criada: o Polyglotta sobe ao entrar no Windows."
                : "Início automático desligado.";
            return;
        }

        AutostartStatus.Text = $"Não foi possível alterar o início automático: {error}";
        _loading = true;
        AutostartBox.IsChecked = !wanted;
        _loading = false;
    }
}
