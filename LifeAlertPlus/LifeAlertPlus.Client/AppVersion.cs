using System.Reflection;

namespace LifeAlertPlus.Client
{
    public static class AppVersion
    {
        public static string Version =>
            (typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?
            .Split('-', '+')[0])
            ?? "unknown";
    }
}