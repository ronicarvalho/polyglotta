using Microsoft.CognitiveServices.Speech;

namespace Polyglotta.Translation.Azure;

/// <summary>Um item do catálogo de vozes do recurso Azure.</summary>
public sealed record VoiceOption(string ShortName, string Locale, string Gender, bool IsHighDefinition, string LocalName);

/// <summary>
/// Resolução de vozes de síntese do Azure (ADR-0003).
/// <para>
/// Aceita o nome completo da voz ("en-US-Andrew:DragonHDLatestNeural") ou os apelidos
/// "male"/"female"/"masculina"/"feminina", resolvidos para vozes HD do locale alvo.
/// As vozes HD (famílias DragonHD e DragonHDOmni) soam bem mais naturais que as
/// neurais de primeira geração como Jenny e Francisca.
/// </para>
/// </summary>
public static class AzureVoices
{
    /// <summary>Vozes padrão por locale e gênero. DragonHD onde existe, DragonHDOmni no resto.</summary>
    private static readonly Dictionary<(string Locale, bool Male), string> Defaults = new(LocaleGenderComparer.Instance)
    {
        [("en-US", false)] = "en-US-Ava:DragonHDLatestNeural",
        [("en-US", true)] = "en-US-Andrew:DragonHDLatestNeural",
        [("pt-BR", false)] = "pt-BR-Francisca:DragonHDOmniLatestNeural",
        [("pt-BR", true)] = "pt-BR-Antonio:DragonHDOmniLatestNeural",
        [("es-ES", false)] = "es-ES-Ximena:DragonHDLatestNeural",
        [("es-ES", true)] = "es-ES-Tristan:DragonHDLatestNeural",
        [("fr-FR", false)] = "fr-FR-Vivienne:DragonHDLatestNeural",
        [("fr-FR", true)] = "fr-FR-Remy:DragonHDLatestNeural",
        [("de-DE", false)] = "de-DE-Seraphina:DragonHDLatestNeural",
        [("de-DE", true)] = "de-DE-Florian:DragonHDLatestNeural",
    };

    private static readonly string[] MaleAliases = ["male", "m", "masculina", "masculino", "homem"];
    private static readonly string[] FemaleAliases = ["female", "f", "feminina", "feminino", "mulher"];

    /// <summary>
    /// Traduz o valor recebido na linha de comando num nome de voz do Azure.
    /// Retorna null quando não há padrão conhecido para o locale — nesse caso o
    /// serviço escolhe a voz dele.
    /// </summary>
    public static string? Resolve(string? value, string targetLocale)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        var male = MaleAliases.Contains(value, StringComparer.OrdinalIgnoreCase);
        var female = FemaleAliases.Contains(value, StringComparer.OrdinalIgnoreCase);
        if (!male && !female)
            return value; // nome completo da voz, repassado como veio

        return Defaults.TryGetValue((targetLocale, male), out var name) ? name : null;
    }

    /// <summary>True quando o valor é um apelido de gênero e não um nome de voz.</summary>
    public static bool IsAlias(string? value) =>
        value is not null &&
        (MaleAliases.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase) ||
         FemaleAliases.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>Consulta o catálogo de vozes do recurso; <paramref name="locale"/> vazio traz todas.</summary>
    public static async Task<IReadOnlyList<VoiceOption>> ListAsync(string key, string region, string locale = "")
    {
        var config = SpeechConfig.FromSubscription(key, region);
        using var synthesizer = new SpeechSynthesizer(config, null);
        using var result = await synthesizer.GetVoicesAsync(locale);

        if (result.Reason == ResultReason.Canceled)
            throw new InvalidOperationException($"Falha ao listar vozes: {result.ErrorDetails}");

        return result.Voices
            .Select(v => new VoiceOption(
                v.ShortName,
                v.Locale,
                v.Gender.ToString(),
                v.ShortName.Contains("DragonHD", StringComparison.OrdinalIgnoreCase),
                v.LocalName))
            .OrderBy(v => v.Locale)
            .ThenByDescending(v => v.IsHighDefinition)
            .ThenBy(v => v.ShortName)
            .ToList();
    }

    private sealed class LocaleGenderComparer : IEqualityComparer<(string Locale, bool Male)>
    {
        public static readonly LocaleGenderComparer Instance = new();
        public bool Equals((string Locale, bool Male) x, (string Locale, bool Male) y) =>
            string.Equals(x.Locale, y.Locale, StringComparison.OrdinalIgnoreCase) && x.Male == y.Male;
        public int GetHashCode((string Locale, bool Male) obj) =>
            HashCode.Combine(obj.Locale.ToLowerInvariant(), obj.Male);
    }
}
