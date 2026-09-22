using Polyglotta.Core.Configuration;
using Xunit;

namespace Polyglotta.Core.Tests;

public class EnvironmentSettingsTests
{
    [Theory]
    [InlineData("eastus", "eastus")]
    [InlineData("  eastus  ", "eastus")]
    [InlineData("\"eastus\"", "eastus")]          // set VAR="eastus" in cmd.exe
    [InlineData("'eastus'", "eastus")]
    [InlineData(" \" eastus \" ", "eastus")]
    public void Clean_TrimsWhitespaceAndSurroundingQuotes(string raw, string expected) =>
        Assert.Equal(expected, EnvironmentSettings.Clean(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("\"  \"")]
    public void Clean_ReturnsNull_WhenNothingIsLeft(string? raw) =>
        Assert.Null(EnvironmentSettings.Clean(raw));

    [Theory]
    [InlineData("ab\"cd", "ab\"cd")]              // quotes inside the value are kept
    [InlineData("\"unbalanced", "\"unbalanced")]
    [InlineData("unbalanced\"", "unbalanced\"")]
    [InlineData("\"mixed'", "\"mixed'")]
    public void Clean_OnlyUnwrapsAMatchingPair(string raw, string expected) =>
        Assert.Equal(expected, EnvironmentSettings.Clean(raw));

    [Fact]
    public void Require_Throws_WhenVariableIsMissing()
    {
        var name = $"POLYGLOTTA_TEST_{Guid.NewGuid():N}";
        var ex = Assert.Throws<InvalidOperationException>(() => EnvironmentSettings.Require(name));
        Assert.Contains(name, ex.Message);
    }

    [Fact]
    public void ReadAndRequire_UseTheCleanedValue()
    {
        var name = $"POLYGLOTTA_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, "\"eastus\"");
        try
        {
            Assert.Equal("eastus", EnvironmentSettings.Read(name));
            Assert.Equal("eastus", EnvironmentSettings.Require(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
