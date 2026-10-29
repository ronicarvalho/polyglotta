namespace Polyglotta.Translation.Gemini;

/// <summary>
/// Mapeia os códigos BCP-47 usados no pipeline para os que o modo de tradução do
/// Gemini aceita (ADR-0006).
/// <para>
/// A lista do modo tradução traz `en` sem variante regional, enquanto o português
/// aparece como `pt-BR` e `pt-PT`. Por isso a regra não é "sempre encurtar": só
/// reduzimos ao idioma quando a variante não é distinguida pelo serviço.
/// </para>
/// </summary>
public static class GeminiLanguages
{
    /// <summary>
    /// Idiomas em que o serviço distingue a variante regional e o código completo
    /// deve ser preservado.
    /// </summary>
    private static readonly string[] RegionalLanguages = ["pt", "zh"];

    /// <summary>
    /// Converte, por exemplo, "en-US" em "en" e preserva "pt-BR". Entrada vazia
    /// devolve o padrão do serviço ("en").
    /// </summary>
    public static string ToTargetCode(string? bcp47)
    {
        if (string.IsNullOrWhiteSpace(bcp47))
            return "en";

        var value = bcp47.Trim();
        var dash = value.IndexOf('-');
        if (dash <= 0)
            return value.ToLowerInvariant();

        var language = value[..dash].ToLowerInvariant();
        if (!RegionalLanguages.Contains(language))
            return language;

        // Normaliza para a forma "pt-BR": idioma minúsculo, região maiúscula.
        var region = value[(dash + 1)..].ToUpperInvariant();
        return $"{language}-{region}";
    }
}
