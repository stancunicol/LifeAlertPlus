using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.Notification;

namespace LifeAlertPlus.Client.Pages.Notifications;

// Code-behind pentru pagina de Notificări — listează notificările paginat, cu filtrare pe tip/necitite,
// marcare ca citit și actualizare în timp real la primirea unei notificări push
public partial class NotificationsPage : ComponentBase, IAsyncDisposable
{
    [Inject] private TokenParserService TokenParserService { get; set; } = default!;
    [Inject] private UserApiClient UserApiClient { get; set; } = default!;
    [Inject] private NotificationService NotificationService { get; set; } = default!;
    [Inject] private PushNotificationClientService PushService { get; set; } = default!;
    [Inject] private LanguageService Lang { get; set; } = default!;

    private string T(string key) => Lang.T(key);

    // Dacă e setat, pagina arată notificările pentru o singură persoană monitorizată (nu pentru tot contul)
    [Parameter] public Guid PersonId { get; set; }

    private string UserFullName = "";
    private string ProfilePictureUrl = "";
    private string _patientName = "";

    private NotificationPagedResponseDTO? _paged;
    private bool _loading;
    private bool _markingAll;

    private string _activeFilter = "";   // "" = All, "Critical", "Alert"
    private bool _unreadOnly;
    private int _page = 1;
    private const int PageSize = 10;

    private bool _subscribed;

    protected override async Task OnInitializedAsync()
    {
        // Citește mai întâi datele de bază din claims-urile token-ului (rapid, fără apel API)
        var claims = await TokenParserService.GetClaimsAsync();
        if (claims != null)
        {
            UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
            ProfilePictureUrl = claims.ProfilePictureUrl;

            // Apoi suprascrie cu datele proaspete din API, dacă sunt disponibile (claims-urile pot fi vechi/incomplete)
            var userProfile = await UserApiClient.GetUserByIdAsync(claims.UserId);
            if (userProfile != null)
            {
                var apiName = $"{userProfile.FirstName} {userProfile.LastName}".Trim();
                if (!string.IsNullOrWhiteSpace(apiName)) UserFullName = apiName;
                if (!string.IsNullOrWhiteSpace(userProfile.ProfilePictureUrl))
                    ProfilePictureUrl = userProfile.ProfilePictureUrl;
            }
        }
        else
        {
            // Fallback dacă utilizatorul nu e autentificat sau token-ul nu poate fi decodat
            UserFullName = "User";
        }

        await LoadPageAsync();

        // Se abonează o singură dată la notificările push, pentru a reîmprospăta lista în timp real
        if (!_subscribed)
        {
            PushService.OnNotificationReceived += OnPushNotificationReceived;
            _subscribed = true;
        }
    }

    // Cere de la API pagina curentă de notificări, aplicând filtrele active (tip, necitite, persoană)
    private async Task LoadPageAsync()
    {
        _loading = true;
        StateHasChanged();

        _paged = await NotificationService.GetPagedAsync(
            _page, PageSize,
            string.IsNullOrEmpty(_activeFilter) ? null : _activeFilter,
            _unreadOnly,
            PersonId != Guid.Empty ? PersonId : null);

        // Reține numele persoanei monitorizate din prima notificare primită, pentru titlul paginii
        if (string.IsNullOrEmpty(_patientName) && PersonId != Guid.Empty)
            _patientName = _paged?.Items.FirstOrDefault()?.MonitoredName ?? "";

        _loading = false;
        StateHasChanged();
    }

    // Comută pe filtrul de tip (All/Critical/Alert) și resetează paginarea
    private async Task SetFilter(string filter)
    {
        if (_activeFilter == filter && !_unreadOnly) return;
        _activeFilter = filter;
        _unreadOnly = false;
        _page = 1;
        await LoadPageAsync();
    }

    // Comută filtrul "doar necitite" (exclusiv cu filtrul de tip) și resetează paginarea
    private async Task SetUnreadFilter()
    {
        _unreadOnly = !_unreadOnly;
        _activeFilter = "";
        _page = 1;
        await LoadPageAsync();
    }

    private async Task GoToPage(int p)
    {
        if (_paged == null || p < 1 || p > _paged.TotalPages) return;
        _page = p;
        await LoadPageAsync();
    }

    // Marchează o notificare ca citită — actualizează imediat starea locală (fără reload complet) pentru UI rapid
    private async Task MarkAsReadAsync(Guid id)
    {
        await NotificationService.MarkAsReadAsync(id);
        if (_paged?.Items is { } items)
        {
            var item = items.FirstOrDefault(n => n.Id == id);
            if (item != null)
            {
                item.IsRead = true;
                if (_paged.UnreadCount > 0) _paged.UnreadCount--;
            }
        }
        StateHasChanged();
    }

    // Marchează toate notificările utilizatorului ca citite — arată spinner pe buton cât durează operația
    private async Task MarkAllAsReadAsync()
    {
        _markingAll = true;
        StateHasChanged();

        await NotificationService.MarkAllAsReadAsync();
        await LoadPageAsync();

        _markingAll = false;
        StateHasChanged();
    }

    // Handler apelat de serviciul de push când vine o notificare nouă — revine la prima pagină și reîncarcă lista
    private async void OnPushNotificationReceived(string message, string severity)
    {
        _page = 1;
        await LoadPageAsync();
        await InvokeAsync(StateHasChanged);
    }

    // Generează numerele de pagină de afișat în paginare, comprimând paginile îndepărtate cu un separator (-1)
    private IEnumerable<int> GetPageNumbers()
    {
        if (_paged == null) yield break;
        int total = _paged.TotalPages;
        int cur = _page;

        for (int i = 1; i <= total; i++)
        {
            if (i == 1 || i == total || Math.Abs(i - cur) <= 1)
                yield return i;
            else if (i == cur - 2 || i == cur + 2)
                yield return -1; // separator
        }
    }

    // Returnează iconița emoji corespunzătoare tipului de notificare (afișată în lista de notificări)
    private static string GetTypeIcon(string type) => type switch
    {
        "Critical" => "🚨",
        "Alert"    => "⚠️",
        _          => "🔔"
    };

    // Returnează clasa CSS corespunzătoare tipului de notificare (critical/alert/info) pentru stilizare vizuală
    private static string GetTypeClass(string type) => type switch
    {
        "Critical" => "critical",
        "Alert"    => "alert",
        _          => "info"
    };

    // Formatează data relativ la momentul curent ("acum", "5 min", "2h", ziua săptămânii sau data completă)
    private string FormatDate(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var diff  = DateTime.Now - local;

        if (diff.TotalMinutes < 1)  return T("time.justNow");
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} {T("time.min")}";
        if (diff.TotalHours < 24)   return $"{(int)diff.TotalHours}{T("time.h")}";
        if (diff.TotalDays < 7)     return local.ToString("ddd, HH:mm");
        return local.ToString("dd MMM, HH:mm");
    }

    // Dezabonează handler-ul de push la distrugerea paginii, ca să nu rămână referințe vechi active
    public async ValueTask DisposeAsync()
    {
        if (_subscribed)
        {
            PushService.OnNotificationReceived -= OnPushNotificationReceived;
            _subscribed = false;
        }
        GC.SuppressFinalize(this);
    }
}
