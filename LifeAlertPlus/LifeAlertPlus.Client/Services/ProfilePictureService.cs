namespace LifeAlertPlus.Client.Services
{
    // Stare partajată (singleton/scoped) pentru URL-ul pozei de profil — notifică componentele UI
    // prin evenimentul OnChange ca avatar-ul să se actualizeze imediat fără reload de pagină
    public class ProfilePictureService
    {
        private string? _originalUrl;
        private string? _cacheBustedUrl;

        // Url exposed to UI includes a cache-busting query parameter and is stable
        // until the next SetUrl call.
        public string? Url => _cacheBustedUrl;

        public event Action<string?>? OnChange;

        // Setează noul URL al pozei și adaugă un query param unic (cache-busting) ca browser-ul
        // să reîncarce imaginea în loc să o servească din cache după upload-ul unei poze noi
        public void SetUrl(string? url)
        {
            _originalUrl = url;
            if (string.IsNullOrEmpty(url))
            {
                _cacheBustedUrl = null;
            }
            else
            {
                var sep = url.Contains('?') ? '&' : '?';
                _cacheBustedUrl = $"{url}{sep}cb={Guid.NewGuid():N}";
            }

            OnChange?.Invoke(_cacheBustedUrl);
        }
    }
}
