using System.Reflection;

public static class AppVersion
{
    public static string Version
    {
        get
        {
            try
            {
                // Dacă fișierul VERSION există, citește-l
                if (File.Exists("LifeAlertPlus/LifeAlertPlus.Client/VERSION"))
                {
                    var version = File.ReadAllText("LifeAlertPlus/LifeAlertPlus.Client/VERSION").Trim();
                    if (!string.IsNullOrEmpty(version))
                        return version;
                }

                // Fallback la atributul din assembly
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
