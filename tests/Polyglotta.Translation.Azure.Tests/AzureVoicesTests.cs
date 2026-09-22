using Polyglotta.Translation.Azure;
using Xunit;

namespace Polyglotta.Translation.Azure.Tests;

/// <summary>
/// Resolução de apelidos de voz. Não toca na nuvem — o catálogo (<c>ListAsync</c>)
/// exige credenciais e é verificado manualmente com <c>--list-voices</c>.
/// </summary>
public class AzureVoicesTests
{
    [Theory]
    [InlineData("female", "en-US", "en-US-Ava:DragonHDLatestNeural")]
    [InlineData("male", "en-US", "en-US-Andrew:DragonHDLatestNeural")]
    [InlineData("feminina", "pt-BR", "pt-BR-Francisca:DragonHDOmniLatestNeural")]
    [InlineData("masculina", "pt-BR", "pt-BR-Antonio:DragonHDOmniLatestNeural")]
    [InlineData("MALE", "PT-br", "pt-BR-Antonio:DragonHDOmniLatestNeural")]  // sem case-sensitivity
    [InlineData(" f ", "en-US", "en-US-Ava:DragonHDLatestNeural")]           // tolera espaços
    public void Resolve_MapsGenderAliasesToHighDefinitionVoices(string alias, string locale, string expected) =>
        Assert.Equal(expected, AzureVoices.Resolve(alias, locale));

    [Fact]
    public void Resolve_PassesFullVoiceNamesThrough() =>
        Assert.Equal("en-US-Emma2:DragonHDLatestNeural",
            AzureVoices.Resolve("en-US-Emma2:DragonHDLatestNeural", "en-US"));

    [Fact]
    public void Resolve_PassesLegacyVoiceNamesThrough() =>
        Assert.Equal("pt-BR-FranciscaNeural", AzureVoices.Resolve("pt-BR-FranciscaNeural", "pt-BR"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ReturnsNull_WhenNothingWasAsked(string? value) =>
        Assert.Null(AzureVoices.Resolve(value, "en-US"));

    [Fact]
    public void Resolve_ReturnsNull_ForAnAliasInAnUnknownLocale() =>
        // Sem padrão conhecido: o serviço escolhe a voz dele em vez de falhar.
        Assert.Null(AzureVoices.Resolve("male", "ja-JP"));

    [Theory]
    [InlineData("male", true)]
    [InlineData("feminina", true)]
    [InlineData("en-US-Ava:DragonHDLatestNeural", false)]
    [InlineData(null, false)]
    public void IsAlias_DistinguishesAliasesFromVoiceNames(string? value, bool expected) =>
        Assert.Equal(expected, AzureVoices.IsAlias(value));
}
