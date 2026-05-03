using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.Notification;

namespace LifeAlertPlus.Client.Pages.Notifications;

public partial class NotificationsPage : ComponentBase, IAsyncDisposable
{
    [Inject] private TokenParserService TokenParserService { get; set; } = default!;
    [Inject] private UserService UserService { get; set; } = default!;
    [Inject] private NotificationService NotificationService { get; set; } = default!;
    [Inject] private PushNotificationClientService PushService { get; set; } = default!;
    [Inject] private LanguageService Lang { get; set; } = default!;

    private string T(string key) => Lang.T(key);

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
        var claims = await TokenParserService.GetClaimsAsync();
        if (claims != null)
        {
            UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
            ProfilePictureUrl = claims.ProfilePictureUrl;

            var userProfile = await UserService.GetUserByIdAsync(claims.UserId);
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
            UserFullName = "User";
        }

        await LoadPageAsync();

        if (!_subscribed)
        {
            PushService.OnNotificationReceived += OnPushNotificationReceived;
            _subscribed = true;
        }
    }

    private async Task LoadPageAsync()
    {
        _loading = true;
        StateHasChanged();

        _paged = await NotificationService.GetPagedAsync(
            _page, PageSize,
            string.IsNullOrEmpty(_activeFilter) ? null : _activeFilter,
            _unreadOnly,
            PersonId != Guid.Empty ? PersonId : null);

        if (string.IsNullOrEmpty(_patientName) && PersonId != Guid.Empty)
            _patientName = _paged?.Items.FirstOrDefault()?.MonitoredName ?? "";

        _loading = false;
        StateHasChanged();
    }

    private async Task SetFilter(string filter)
    {
        if (_activeFilter == filter && !_unreadOnly) return;
        _activeFilter = filter;
        _unreadOnly = false;
        _page = 1;
        await LoadPageAsync();
    }

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

    private async Task MarkAllAsReadAsync()
    {
        _markingAll = true;
        StateHasChanged();

        await NotificationService.MarkAllAsReadAsync();
        await LoadPageAsync();

        _markingAll = false;
        StateHasChanged();
    }

    private async void OnPushNotificationReceived(string message, string severity)
    {
        _page = 1;
        await LoadPageAsync();
        await InvokeAsync(StateHasChanged);
    }

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

    private static string GetTypeIcon(string type) => type switch
    {
        "Critical" => "🚨",
        "Alert"    => "⚠️",
        _          => "🔔"
    };

    private static string GetTypeClass(string type) => type switch
    {
        "Critical" => "critical",
        "Alert"    => "alert",
        _          => "info"
    };

    private static string FormatDate(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var diff  = DateTime.Now - local;

        if (diff.TotalMinutes < 1)  return "Acum";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min";
        if (diff.TotalHours < 24)   return $"{(int)diff.TotalHours}h";
        if (diff.TotalDays < 7)     return local.ToString("ddd, HH:mm");
        return local.ToString("dd MMM, HH:mm");
    }

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
