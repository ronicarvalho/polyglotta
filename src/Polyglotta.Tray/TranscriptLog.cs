using System.Collections.ObjectModel;
using Avalonia.Threading;
using Polyglotta.Host;

namespace Polyglotta.Tray;

/// <summary>
/// The last few transcript lines, for the configuration window to show (RF-06).
/// <para>
/// Fed from provider callbacks, so every mutation is marshalled to the UI thread and
/// the list is capped: a long call must not grow this without bound.
/// </para>
/// </summary>
public sealed class TranscriptLog
{
    private const int MaxLines = 100;

    public ObservableCollection<string> Lines { get; } = [];

    public void Add(SessionTranscript transcript)
    {
        if (!transcript.Event.IsFinal || string.IsNullOrWhiteSpace(transcript.Event.SourceText))
            return;

        var line = $"[{DateTime.Now:HH:mm:ss}] {transcript.Pipeline}: " +
                   $"{transcript.Event.SourceText} → {transcript.Event.TranslatedText}";

        Dispatcher.UIThread.Post(() =>
        {
            Lines.Add(line);
            while (Lines.Count > MaxLines)
                Lines.RemoveAt(0);
        });
    }

    public void Clear() => Dispatcher.UIThread.Post(Lines.Clear);
}
