namespace LifeAlertPlus.Client
{
    public static class AppVersion
    {
        public static string Version =>
            typeof(AppVersion).Assembly.GetName().Version?.ToString() ?? "unknown";
    }
}