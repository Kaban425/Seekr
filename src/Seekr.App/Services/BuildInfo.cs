using System.Reflection;

namespace Seekr.App.Services;

public static class BuildInfo
{
    public static string Version => typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
    public static string DisplayVersion => Version.Split('+')[0] + " · сборка " + (typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version.Split('.').LastOrDefault() ?? "1");
}
