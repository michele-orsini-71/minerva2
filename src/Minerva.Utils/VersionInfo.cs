using System.Reflection;

namespace Minerva.Utils;

public static class VersionInfo
{
    public static void PrintVersion(TextWriter w, Assembly assembly)
    {
        w.WriteLine(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");
    }
}
