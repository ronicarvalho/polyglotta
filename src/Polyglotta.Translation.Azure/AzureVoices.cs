using Microsoft.CognitiveServices.Speech;

namespace Polyglotta.Translation.Azure;

/// <summary>Um item do catálogo de vozes do recurso Azure.</summary>
public sealed record VoiceOption(string ShortName, string Locale, string Gender, bool IsHighDefinition, string LocalName);

/// <summary>
/// Resolução de vozes de síntese do Azure (ADR-0003).
/// <para>
/// Aceita o nome completo da voz ("en-US-Andrew:DragonHDLatestNeural") ou os apelidos
/// "male"/"female"/"masculina"/"feminina". Os apelidos são resolvidos contra o catálogo
/// do próprio recurso, preferindo vozes HD — as famílias DragonHD soam bem mais naturais
/// que as neurais de primeira geração (Jenny, Francisca).
/// </para>
/// <para>
/// Consultar o catálogo em vez de manter uma tabela fixa evita nomes inventados: a
/// documentação da Microsoft lista as vozes HD de forma incompleta (em 2026-09-22 ela
/// omitia `pt-BR-Thalita` e `pt-BR-Macerio`, que existem), e a disponibilidade varia
/// por região e por recurso.
/// </para>
/// </summary>
public static class AzureVoices
{
    private static readonly string[] MaleAliases = ["male", "m", "masculina", "masculino", "homem"];
    private static readonly string[] FemaleAliases = ["female", "f", "feminina", "feminino", "mulher"];

    /// <summary>
    /// Padrões verificados no catálogo em 2026-09-22, usados quando a consulta ao
    /// serviço falha. Fora desses locales, cai na voz padrão do serviço.
    /// </summary>
    private static readonly (string Locale, bool Male, string Voice)[] KnownGood =
    [
        ("en-US", false, "en-US-Ava:DragonHDLatestNeural"),
        ("en-US", true, "en-US-Andrew:DragonHDLatestNeural"),
        ("pt-BR", false, "pt-BR-Thalita:DragonHDLatestNeural"),
        ("pt-BR", true, "pt-BR-Macerio:DragonHDLatestNeural"),
    ];

    /// <summary>True quando o valor é um apelido de gênero e não um nome de voz.</summary>
    public static bool IsAlias(string? value) => AsGender(value) is not null;

    /// <summary>null quando não é apelido; true para masculino, false para feminino.</summary>
    private static bool? AsGender(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var v = value.Trim();
        if (MaleAliases.Contains(v, StringComparer.OrdinalIgnoreCase)) return true;
        if (FemaleAliases.Contains(v, StringComparer.OrdinalIgnoreCase)) return false;
        return null;
    }

    /// <summary>
    /// Resolve o valor da linha de comando num nome de voz, consultando o catálogo
    /// quando for apelido de gênero. Nomes completos passam direto, sem validação —
    /// quem escolheu o nome manda. Retorna null quando não há voz adequada, caso em
    /// que o serviço usa a voz padrão dele.
    /// </summary>
    public static async Task<string?> ResolveAsync(string? value, string locale, string key, string region)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (AsGender(value) is not { } male)
            return value.Trim();

        try
        {
            var catalog = await ListAsync(key, region, locale);
            if (Select(catalog, locale, male) is { } fromCatalog)
                return fromCatalog;
        }
        catch (Exception)
        {
            // Sem catálogo (offline, credencial inválida, região sem TTS): usa o
            // padrão conhecido. Um erro real aparece de novo ao iniciar a tradução.
        }

        return KnownGood.FirstOrDefault(k =>
            string.Equals(k.Locale, locale, StringComparison.OrdinalIgnoreCase) && k.Male == male).Voice;
    }

    /// <summary>
    /// Escolhe a melhor voz do catálogo para o locale e gênero pedidos: HD antes de
    /// neural comum, versões GA antes de preview.
    /// </summary>
    public static string? Select(IReadOnlyList<VoiceOption> catalog, string locale, bool male)
    {
        var gender = male ? "Male" : "Female";
        return catalog
            .Where(v => string.Equals(v.Locale, locale, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(v.Gender, gender, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Rank)
            .ThenBy(v => v.ShortName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.ShortName;
    }

    /// <summary>Preferência entre famílias de voz; maior é melhor.</summary>
    private static int Rank(VoiceOption voice)
    {
        var name = voice.ShortName;
        var score = name switch
        {
            _ when Has(name, "DragonHDLatestNeural") => 100,   // HD, geração atual
            _ when Has(name, "DragonHDOmniLatestNeural") => 90, // HD multilíngue
            _ when Has(name, "DragonHDFlashLatestNeural") => 80,
            _ when Has(name, "DragonHD") => 70,                 // outras versões HD
            _ when Has(name, "DragonLatestNeural") => 50,
            _ when Has(name, "MultilingualNeural") => 30,
            _ => 10,
        };
        // "-Preview" e multitalker não servem para uma conversa a dois.
        if (Has(name, "-Preview")) score -= 25;
        if (Has(name, "Multitalker")) score -= 50;
        return score;
    }

    private static bool Has(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

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
}
