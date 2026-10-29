namespace Polyglotta.Host;

/// <summary>
/// Something that stops the session from running, phrased for the person reading it.
/// Returned instead of logged so the configuration screen can show it next to the
/// field that caused it, and the CLI can print it.
/// </summary>
/// <param name="Field">Which setting is at fault: "mic", "loopback", "phones", "provider", "virtualmic".</param>
public sealed record SessionProblem(string Field, string Message)
{
    public override string ToString() => Message;
}
