using System.Reflection;
using System.IO;
using System.Linq;

namespace LifeAlertPlus.Client;

public static class AppVersion
{
    public static string Version
    {
        get
        {
            try
            {
                var asm = typeof(AppVersion).Assembly;

                // Try embedded resource first (works in Blazor/WebAssembly when the file is embedded)
                try
                {
                    var resourceName = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith(".VERSION", System.StringComparison.OrdinalIgnoreCase) || n.EndsWith(".VERSION.txt", System.StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(resourceName))
                    {
                        using var s = asm.GetManifestResourceStream(resourceName);
                        if (s != null)
                        {
                            using var reader = new StreamReader(s);
                            var text = reader.ReadToEnd().Trim();
                            if (!string.IsNullOrEmpty(text))
                                return text;
                        }
                    }
                }
                catch { }

                // Fallback: try common filesystem locations (useful for server-side scenarios)
                try
                {
                    var candidates = new[] { "wwwroot/VERSION", "VERSION", Path.Combine("LifeAlertPlus", "LifeAlertPlus.Client", "VERSION") };
                    foreach (var p in candidates)
                    {
                        if (File.Exists(p))
                        {
                            var version = File.ReadAllText(p).Trim();
                            if (!string.IsNullOrEmpty(version))
                                return version;
                        }
                    }
                }
                catch { }

                // Final fallback: assembly informational version or unknown
                return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
