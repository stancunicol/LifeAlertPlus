namespace LifeAlertPlus.Client.Services
{
    public class UserStateService
    {
        public string? DisplayName { get; private set; }

        public event Action<string?>? OnChange;

        public void SetDisplayName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (name == DisplayName) return;
            DisplayName = name;
            OnChange?.Invoke(name);
        }
    }
}
