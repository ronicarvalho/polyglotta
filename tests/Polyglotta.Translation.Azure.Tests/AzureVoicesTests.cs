using Polyglotta.Translation.Azure;
using Xunit;

namespace Polyglotta.Translation.Azure.Tests;

/// <summary>
/// Seleção de voz a partir do catálogo. O fixture reproduz um recorte real do
/// catálogo do recurso (região eastus, capturado em 2026-09-22); a consulta ao
/// serviço em si (<c>ListAsync</c>) exige credenciais e é verificada manualmente
/// com <c>--list-voices</c>.
/// </summary>
public class AzureVoicesTests
{
    private static VoiceOption V(string shortName, string locale, string gender) =>
        new(shortName, locale, gender, shortName.Contains("DragonHD", StringComparison.OrdinalIgnoreCase), shortName);

    private static readonly VoiceOption[] Catalog =
    [
        // pt-BR
        V("pt-BR-Macerio:DragonHDLatestNeural", "pt-BR", "Male"),
        V("pt-BR-Thalita:DragonHDLatestNeural", "pt-BR", "Female"),
        V("pt-BR-AntonioNeural", "pt-BR", "Male"),
        V("pt-BR-FranciscaNeural", "pt-BR", "Female"),
        V("pt-BR-Francisca:DragonLatestNeural", "pt-BR", "Female"),
        V("pt-BR-MacerioMultilingualNeural", "pt-BR", "Male"),
        V("pt-BR-Caio:MAI-Voice-2", "pt-BR", "Male"),
        // en-US
        V("en-US-Andrew:DragonHDLatestNeural", "en-US", "Male"),
        V("en-US-Andrew-Preview:DragonHDLatestNeural", "en-US", "Male"),
        V("en-US-Andrew:DragonHDOmniLatestNeural", "en-US", "Male"),
        V("en-US-Ava:DragonHDLatestNeural", "en-US", "Female"),
        V("en-US-Ava-Preview:DragonHDLatestNeural", "en-US", "Female"),
        V("en-US-Tiana:DragonHDFlashLatestNeural", "en-US", "Female"),
        V("en-US-JennyNeural", "en-US", "Female"),
        V("en-US-GuyNeural", "en-US", "Male"),
        V("en-Multitalker:DragonHDLatestNeural", "en-US", "Neutral"),
        // outro locale, para garantir o filtro
        V("es-ES-Tristan:DragonHDLatestNeural", "es-ES", "Male"),
    ];

    [Theory]
    [InlineData("pt-BR", true, "pt-BR-Macerio:DragonHDLatestNeural")]
    [InlineData("pt-BR", false, "pt-BR-Thalita:DragonHDLatestNeural")]
    [InlineData("en-US", true, "en-US-Andrew:DragonHDLatestNeural")]
    [InlineData("en-US", false, "en-US-Ava:DragonHDLatestNeural")]
    public void Select_PrefersCurrentGenerationHdVoices(string locale, bool male, string expected) =>
        Assert.Equal(expected, AzureVoices.Select(Catalog, locale, male));

    [Fact]
    public void Select_IsCaseInsensitiveOnLocale() =>
        Assert.Equal("pt-BR-Thalita:DragonHDLatestNeural", AzureVoices.Select(Catalog, "PT-br", false));

    [Fact]
    public void Select_FallsBackToTheBestNonHdVoice_WhenNoHdExists()
    {
        VoiceOption[] legacyOnly =
        [
            V("pt-BR-FranciscaNeural", "pt-BR", "Female"),
            V("pt-BR-ThalitaMultilingualNeural", "pt-BR", "Female"),
        ];
        // Multilingual soa melhor que a neural de 1a geração.
        Assert.Equal("pt-BR-ThalitaMultilingualNeural", AzureVoices.Select(legacyOnly, "pt-BR", false));
    }

    [Fact]
    public void Select_IgnoresOtherLocales() =>
        Assert.Null(AzureVoices.Select(Catalog, "ja-JP", true));

    [Fact]
    public void Select_IgnoresNeutralAndMultitalkerVoices()
    {
        var chosen = AzureVoices.Select(Catalog, "en-US", male: true);
        Assert.DoesNotContain("Multitalker", chosen, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_ReturnsNull_ForAnEmptyCatalog() =>
        Assert.Null(AzureVoices.Select([], "en-US", true));

    [Theory]
    [InlineData("male", true)]
    [InlineData("feminina", true)]
    [InlineData("MULHER", true)]
    [InlineData(" f ", true)]
    [InlineData("en-US-Ava:DragonHDLatestNeural", false)]
    [InlineData("pt-BR-FranciscaNeural", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    public void IsAlias_DistinguishesAliasesFromVoiceNames(string? value, bool expected) =>
        Assert.Equal(expected, AzureVoices.IsAlias(value));

    [Theory]
    [InlineData("en-US-Emma2:DragonHDLatestNeural")]
    [InlineData("pt-BR-FranciscaNeural")]
    public async Task ResolveAsync_PassesFullVoiceNamesThrough_WithoutCallingTheService(string name) =>
        // Credenciais inválidas de propósito: um nome completo não deve consultar o catálogo.
        Assert.Equal(name, await AzureVoices.ResolveAsync(name, "en-US", "chave-invalida", "eastus"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_ReturnsNull_WhenNothingWasAsked(string? value) =>
        Assert.Null(await AzureVoices.ResolveAsync(value, "en-US", "chave-invalida", "eastus"));

    [Theory]
    [InlineData("male", "en-US", "en-US-Andrew:DragonHDLatestNeural")]
    [InlineData("female", "en-US", "en-US-Ava:DragonHDLatestNeural")]
    [InlineData("masculina", "pt-BR", "pt-BR-Macerio:DragonHDLatestNeural")]
    [InlineData("feminina", "pt-BR", "pt-BR-Thalita:DragonHDLatestNeural")]
    public async Task ResolveAsync_UsesKnownGoodVoices_WhenTheCatalogIsUnreachable(
        string alias, string locale, string expected) =>
        // Região inexistente: a consulta falha e cai no padrão verificado.
        Assert.Equal(expected, await AzureVoices.ResolveAsync(alias, locale, "chave-invalida", "regiao-inexistente"));

    [Fact]
    public async Task ResolveAsync_ReturnsNull_ForAnAliasInAnUnknownLocale_WhenOffline() =>
        // Sem catálogo nem padrão conhecido: o serviço escolhe a voz dele.
        Assert.Null(await AzureVoices.ResolveAsync("male", "ja-JP", "chave-invalida", "regiao-inexistente"));
}
