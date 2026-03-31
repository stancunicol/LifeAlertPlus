namespace LifeAlertPlus.Client.Services
{
    public class ProfilePictureService
    {
        private string? _originalUrl;
        private string? _cacheBustedUrl;

        // Url exposed to UI includes a cache-busting query parameter and is stable
        // until the next SetUrl call.
        public string? Url => _cacheBustedUrl;

        public event Action<string?>? OnChange;

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
