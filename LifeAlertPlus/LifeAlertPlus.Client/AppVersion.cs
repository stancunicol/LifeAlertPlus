using System.Reflection;

namespace LifeAlertPlus.Client;

public static class AppVersion
{
    public static string Version
    {
        get
        {
            try
            {
                if (File.Exists("LifeAlertPlus/LifeAlertPlus.Client/VERSION"))
                {
                    var version = File.ReadAllText("LifeAlertPlus/LifeAlertPlus.Client/VERSION").Trim();
                    if (!string.IsNullOrEmpty(version))
                        return version;
                }

                return typeof(AppVersion).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion
                    ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
