using System.Text;
using Microsoft.Extensions.Configuration;

namespace Minerva.Tests.Configuration;

internal static class ConfigFromJson
{
    public static IConfiguration Build(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(bytes))
            .Build();
    }
}
