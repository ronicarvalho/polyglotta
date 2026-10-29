using Polyglotta.Core.Audio;

namespace Polyglotta.Host;

/// <summary>The four endpoints a session uses, already resolved to real devices.</summary>
public sealed record SessionPlan(
    AudioDeviceInfo Microphone,
    AudioDeviceInfo Loopback,
    AudioDeviceInfo Phones,
    AudioDeviceInfo VirtualMic);

public sealed record SessionPlanResult(SessionPlan? Plan, IReadOnlyList<SessionProblem> Problems)
{
    public bool IsValid => Plan is not null && Problems.Count == 0;
}

/// <summary>
/// Turns <see cref="PolyglottaSettings"/> into a runnable <see cref="SessionPlan"/>, or
/// into the list of reasons why it cannot run. Pure function over a device list: the
/// configuration screen can call it on every change to validate without touching audio.
/// </summary>
public static class SessionPlanner
{
    public static SessionPlanResult Prepare(PolyglottaSettings settings, IReadOnlyList<AudioDeviceInfo> devices)
    {
        var problems = new List<SessionProblem>();

        if (!settings.UsesAzure && !string.Equals(settings.Provider, "gemini", StringComparison.OrdinalIgnoreCase))
            problems.Add(new SessionProblem("provider",
                $"Provedor \"{settings.Provider}\" desconhecido: use \"azure\" ou \"gemini\"."));

        // Outbound destination: the virtual cable the call application listens to (ADR-0002).
        var virtualMic = devices.FirstOrDefault(d =>
            d.Kind == AudioDeviceKind.Render &&
            d.Name.Contains(settings.VirtualMicName, StringComparison.OrdinalIgnoreCase));

        if (virtualMic is null)
            problems.Add(new SessionProblem("virtualmic",
                $"Dispositivo \"{settings.VirtualMicName}\" não encontrado. Instale o VB-Cable: https://vb-audio.com/Cable/"));

        var mic = Resolve(settings.Microphone, AudioDeviceKind.Capture, "mic", devices, problems);
        var loopback = Resolve(settings.Loopback, AudioDeviceKind.Render, "loopback", devices, problems);
        var phones = Resolve(settings.Phones, AudioDeviceKind.Render, "phones", devices, problems);

        if (mic is null || loopback is null || phones is null || virtualMic is null)
            return new SessionPlanResult(null, problems);

        problems.AddRange(RoutingProblems(mic, loopback, phones, virtualMic));

        return problems.Count > 0
            ? new SessionPlanResult(null, problems)
            : new SessionPlanResult(new SessionPlan(mic, loopback, phones, virtualMic), problems);
    }

    /// <summary>
    /// Feedback guards (skill audio-realtime). These are the mistakes that produce a
    /// translation loop or an app translating its own output — all of them sound like
    /// a bug in the pipeline rather than a routing mistake, so they are refused up front.
    /// </summary>
    public static IEnumerable<SessionProblem> RoutingProblems(
        AudioDeviceInfo mic, AudioDeviceInfo loopback, AudioDeviceInfo phones, AudioDeviceInfo virtualMic)
    {
        if (phones.Id == loopback.Id)
            yield return new SessionProblem("phones",
                $"O fone ({phones.Name}) é o mesmo dispositivo capturado por loopback: a tradução seria " +
                "recapturada e retraduzida em loop. Faça a chamada tocar num dispositivo diferente do fone.");

        if (phones.Id == virtualMic.Id)
            yield return new SessionProblem("phones",
                $"\"{virtualMic.Name}\" é reservado ao microfone virtual (outbound); não use como fone.");

        if (loopback.Id == virtualMic.Id)
            yield return new SessionProblem("loopback",
                $"\"{virtualMic.Name}\" é reservado ao microfone virtual (outbound); não use como loopback.");

        if (mic.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
            yield return new SessionProblem("mic",
                "\"CABLE Output\" é a saída do microfone virtual (o que a chamada escuta): usá-lo como microfone " +
                "faria o app capturar a própria tradução. Escolha o microfone físico.");
    }

    /// <summary>
    /// Resolves one selection. Name first (it survives id changes), then the exact id,
    /// then the system default when nothing was chosen.
    /// </summary>
    private static AudioDeviceInfo? Resolve(
        DeviceSelection selection,
        AudioDeviceKind kind,
        string field,
        IReadOnlyList<AudioDeviceInfo> devices,
        List<SessionProblem> problems)
    {
        var candidates = devices.Where(d => d.Kind == kind).ToList();

        if (selection.IsEmpty)
        {
            var fallback = candidates.FirstOrDefault(d => d.IsDefault) ?? candidates.FirstOrDefault();
            if (fallback is null)
                problems.Add(new SessionProblem(field, $"Nenhum dispositivo de {kind} disponível no sistema."));
            return fallback;
        }

        if (!string.IsNullOrWhiteSpace(selection.Name))
        {
            var byName = candidates
                .Where(d => d.Name.Contains(selection.Name!, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (byName.Count == 1)
                return byName[0];

            if (byName.Count > 1)
            {
                // An exact id still disambiguates when the fragment is too broad.
                if (candidates.FirstOrDefault(d => d.Id == selection.Id) is { } exact)
                    return exact;

                problems.Add(new SessionProblem(field,
                    $"\"{selection.Name}\": ambíguo, {byName.Count} dispositivos de {kind} correspondem " +
                    $"({string.Join(" | ", byName.Select(d => d.Name))}). Seja mais específico."));
                return null;
            }
        }

        if (!string.IsNullOrWhiteSpace(selection.Id))
        {
            if (candidates.FirstOrDefault(d => d.Id == selection.Id) is { } byId)
                return byId;

            problems.Add(new SessionProblem(field,
                $"Dispositivo \"{selection}\" não está mais disponível (nem pelo nome, nem pelo id). " +
                "Escolha outro na configuração."));
            return null;
        }

        problems.Add(new SessionProblem(field,
            $"Nenhum dispositivo de {kind} com o nome \"{selection.Name}\"."));
        return null;
    }
}
