using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.Readiness.Checks;

namespace Minerva.Tests.Readiness.Checks;

[Trait("Category", "Readiness")]
public class ConnectionStringParseCheckTests
{
    [Fact]
    public async Task ShortCircuits_WhenConnectionStringNull()
    {
        var check = Build(connectionString: null);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.CONFIG.CONN_STRING_NOT_CONFIGURED", result.Code);
    }

    [Fact]
    public async Task Passes_WhenConnectionStringValid()
    {
        var check = Build("Host=localhost;Username=u;Password=p;Database=d");

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.CONFIG.CONN_STRING_OK", result.Code);
    }

    [Fact]
    public async Task Fails_WhenConnectionStringMalformed()
    {
        // Unrecognized keyword forces NpgsqlConnectionStringBuilder to throw ArgumentException.
        var check = Build("not-a-key-value-string");

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.CONFIG.CONN_STRING_INVALID", result.Code);
        Assert.Contains("Minerva:ConnectionString", result.Remediation);
    }

    [Fact]
    public async Task Fails_RedactsPasswordInMessage()
    {
        // Force a parse failure whose message contains a Password=... fragment.
        var check = Build("Host=localhost;Password=hunter2;BogusKeyword=oops");

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        if (result.Message is not null)
        {
            Assert.DoesNotContain("hunter2", result.Message);
        }
    }

    private static ConnectionStringParseCheck Build(string? connectionString)
    {
        var options = Options.Create(new MinervaOptions { ConnectionString = connectionString });
        return new ConnectionStringParseCheck(options, NullLogger<ConnectionStringParseCheck>.Instance);
    }
}
