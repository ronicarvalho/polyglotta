using System.Collections.Specialized;
using Avalonia.Controls;

namespace Polyglotta.Tray;

/// <summary>
/// The live transcript (RF-06), in its own window: it is something you watch during a
/// call, not something you configure, and it grows while the configuration screen is closed.
/// </summary>
public partial class TranscriptWindow : Window
{
    private readonly App _app;

    public TranscriptWindow(App app)
    {
        _app = app;
        InitializeComponent();

        Lines.ItemsSource = _app.Transcripts.Lines;
        ClearButton.Click += (_, _) => _app.Transcripts.Clear();

        // A live log is only useful if it follows the newest line on its own.
        _app.Transcripts.Lines.CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || Lines.ItemCount == 0)
            return;

        Lines.ScrollIntoView(Lines.ItemCount - 1);
    }
}
