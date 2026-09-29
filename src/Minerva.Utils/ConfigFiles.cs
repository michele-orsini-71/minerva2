using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Minerva.Utils;

// Every tool finds its config the same way, so the binaries can live side by side
// in one folder and run from any directory: the explicit path, otherwise
// ~/.config/minerva/<executable name>.json; then <base name>.<DOTNET_ENVIRONMENT>.json
// next to it, when the variable is set. A missing overlay is an error: a mistyped
// environment name would otherwise run silently on the base config alone.
public static class ConfigFiles
{
    public static IConfigurationBuilder AddMinervaConfigFiles(this IConfigurationBuilder builder, string? configPath)
    {
        var basePath = Path.GetFullPath(configPath ?? DefaultPath());
        if (!File.Exists(basePath))
        {
            throw new FileNotFoundException($"Config file not found: {basePath}", basePath);
        }
        builder.AddJsonFile(basePath, optional: false);

        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var overlay = Path.Combine(
                Path.GetDirectoryName(basePath)!,
                $"{Path.GetFileNameWithoutExtension(basePath)}.{env}.json");
            if (!File.Exists(overlay))
            {
                throw new FileNotFoundException(
                    $"Config overlay not found: {overlay} (selected by DOTNET_ENVIRONMENT={env})", overlay);
            }
            builder.AddJsonFile(overlay, optional: false);
        }
        return builder;
    }

    private static string DefaultPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var executable = Assembly.GetEntryAssembly()!.GetName().Name;
        return Path.Combine(home, ".config", "minerva", $"{executable}.json");
    }
}
