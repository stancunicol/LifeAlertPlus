using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.ViewSelectedUser
{
    // Code-behind pentru pagina de vizualizare a unui utilizator selectat (ex: din lista de admin).
    // NOTĂ: acest fișier conține DOAR helper-ul de traducere — restul logicii paginii (parametrul
    // UserId, OnInitializedAsync, apelurile către UserApiClient/UserMonitoredApiClient, navigare)
    // e scris direct în blocul @code din ViewSelectedUser.razor, nu aici.
    public partial class ViewSelectedUser
    {
        [Inject]
        private LanguageService Lang { get; set; } = default!;

        // Helper de traducere — folosește varianta în engleză ca fallback pentru textele din UI
        private string T(string key) => Lang.TEnglish(key);
    }
}
