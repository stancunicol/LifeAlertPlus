using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.ViewSelectedUser
{
    public partial class ViewSelectedUser
    {
        [Inject]
        private LanguageService Lang { get; set; } = default!;

        private string T(string key) => Lang.TEnglish(key);
    }
}
