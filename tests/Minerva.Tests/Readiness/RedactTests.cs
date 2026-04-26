using Minerva.Readiness;

namespace Minerva.Tests.Readiness;

[Trait("Category", "Readiness")]
public class RedactTests
{
    [Fact]
    public void Apply_PasswordInConnectionString_IsMasked()
    {
        var input = "Host=localhost;Password=secret;Database=minerva";
        var output = Redact.Apply(input);
        Assert.Equal("Host=localhost;Password=***;Database=minerva", output);
    }

    [Fact]
    public void Apply_BearerToken_IsMasked()
    {
        var input = "Authorization: Bearer abc.def";
        var output = Redact.Apply(input);
        Assert.Equal("Authorization: Bearer ***", output);
    }

    [Fact]
    public void Apply_BothPatternsInOneString_AreMasked()
    {
        var input = "Password=secret;Authorization=Bearer abc.def";
        var output = Redact.Apply(input);
        Assert.Equal("Password=***;Authorization=Bearer ***", output);
    }

    [Fact]
    public void Apply_NoMatch_ReturnsInputUnchanged()
    {
        var input = "Host=localhost;Database=minerva";
        Assert.Equal(input, Redact.Apply(input));
    }

    [Fact]
    public void Apply_Null_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, Redact.Apply(null));
    }
}
