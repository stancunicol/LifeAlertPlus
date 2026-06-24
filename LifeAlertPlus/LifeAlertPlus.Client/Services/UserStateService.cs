namespace LifeAlertPlus.Client.Services
{
    // Stare partajată cu numele afișat al utilizatorului curent — notifică UI-ul (ex: header/navbar)
    // prin OnChange când numele se schimbă, fără să fie nevoie de reload de pagină
    public class UserStateService
    {
        public string? DisplayName { get; private set; }

        public event Action<string?>? OnChange;

        // Actualizează numele afișat doar dacă valoarea e nevidă și diferită de cea curentă,
        // pentru a evita notificări redundante către componentele abonate
        public void SetDisplayName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (name == DisplayName) return;
            DisplayName = name;
            OnChange?.Invoke(name);
        }
    }
}
