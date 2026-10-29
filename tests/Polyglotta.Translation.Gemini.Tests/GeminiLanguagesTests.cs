using Polyglotta.Translation.Gemini;
using Xunit;

namespace Polyglotta.Translation.Gemini.Tests;

public class GeminiLanguagesTests
{
    [Theory]
    [InlineData("en-US", "en")]   // o modo tradução lista "en" sem variante
    [InlineData("en-GB", "en")]
    [InlineData("es-ES", "es")]
    [InlineData("fr-FR", "fr")]
    public void ToTargetCode_DropsTheRegion_WhenTheServiceDoesNotDistinguishIt(string input, string expected) =>
        Assert.Equal(expected, GeminiLanguages.ToTargetCode(input));

    [Theory]
    [InlineData("pt-BR", "pt-BR")]  // português é listado como pt-BR e pt-PT
    [InlineData("pt-PT", "pt-PT")]
    [InlineData("pt-br", "pt-BR")]  // normaliza a caixa
    [InlineData("zh-CN", "zh-CN")]
    public void ToTargetCode_KeepsTheRegion_WhenTheServiceDistinguishesIt(string input, string expected) =>
        Assert.Equal(expected, GeminiLanguages.ToTargetCode(input));

    [Theory]
    [InlineData("en", "en")]
    [InlineData("PT", "pt")]
    [InlineData("  en-US  ", "en")]
    public void ToTargetCode_HandlesBareLanguagesAndWhitespace(string input, string expected) =>
        Assert.Equal(expected, GeminiLanguages.ToTargetCode(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToTargetCode_FallsBackToTheServiceDefault(string? input) =>
        Assert.Equal("en", GeminiLanguages.ToTargetCode(input));
}
