using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Text.Json;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Web;

namespace LifeAlertPlus.Client.Pages.MonitoredUsers
{
	// Code-behind pentru pagina Admin de persoane monitorizate — agregă persoanele monitorizate de toți utilizatorii, status online/alertă și ștergere logică (soft delete) cu reactivare
	public partial class MonitoredUsersPage : ComponentBase
	{
		[Inject]
		private UserMonitoredApiClient UserMonitoredApiClient { get; set; } = default!;

		[Inject]
		private MonitoredApiClient MonitoredApiClient { get; set; } = default!;

		[Inject]
		private MeasurementApiClient MeasurementApiClient { get; set; } = default!;

		[Inject]
		private TokenParserService TokenParser { get; set; } = default!;

		[Inject]
		private IJSRuntime JSRuntime { get; set; } = default!;

		[Inject]
		private NavigationManager NavigationManager { get; set; } = default!;

		[Inject]
		private LanguageService Lang { get; set; } = default!;

		private string T(string key) => Lang.TEnglish(key);

		protected List<MonitoredUserDTO> Users { get; private set; } = new();
		protected List<MonitoredPersonRow> MonitoredRows { get; private set; } = new();
		protected string UserFullName { get; private set; } = "Admin";
		protected string ProfilePictureUrl { get; private set; } = string.Empty;
		protected string SearchText { get; set; } = string.Empty;
		protected string StatusFilter { get; private set; } = "all";
		protected bool IsLoading { get; private set; } = true;
		protected string? ErrorMessage { get; private set; }
		private string? _currentUserEmail;

		private MonitoredPersonRow? _deleteTarget;
		private bool _showDeleteConfirm;
		private bool _isProcessingDelete;

		protected int TotalMonitors => MonitoredRows
			.SelectMany(r => r.Monitors)
			.Select(m => m.Email)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Count();
		protected int TotalMonitored => MonitoredRows.Count;
		protected int OnlineMonitoredCount => MonitoredRows.Count(r => r.Online);
		protected int OfflineMonitoredCount => MonitoredRows.Count(r => !r.Online);
		protected int AlertsCount { get; set; }

		// Rânduri afișate: aplică căutarea text și filtrul de stare (online/offline/alerte), sortate alfabetic după nume
		protected IEnumerable<MonitoredPersonRow> FilteredRows => MonitoredRows
			.Where(r => MatchesSearch(r, SearchText))
			.Where(r => MatchesFilter(r, StatusFilter))
			.OrderBy(r => r.PersonName);

		protected override async Task OnInitializedAsync()
		{
			await LoadUserFromTokenAsync();
			await LoadDataAsync();
		}

		// Extrage identitatea administratorului curent din token, pentru afișare în antetul paginii
		private async Task LoadUserFromTokenAsync()
		{
			var claims = await TokenParser.GetClaimsAsync();
			if (claims == null)
				return;

			UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
			ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
			_currentUserEmail = claims.Email;
		}

		// Încarcă toți utilizatorii cu persoanele lor monitorizate, construiește rândurile agregate și determină status online/alerte
		protected async Task LoadDataAsync()
		{
			IsLoading = true;
			ErrorMessage = null;

			try
			{
				var data = await UserMonitoredApiClient.GetAllMonitoredUsersAsync();
				Users = data
					.Where(u => !IsAdminRole(u.Role))
					.ToList();
				MonitoredRows = BuildMonitoredRows(Users);
				await PopulateOnlineAndAlertsAsync();
			}
			catch
			{
				ErrorMessage = "Could not load monitored users.";
			}
			finally
			{
				IsLoading = false;
			}
		}

		// Setează filtrul de stare activ (all/online/offline/alerts) — tabelul se refiltrează automat via FilteredRows
		protected void SetFilter(string filter)
		{
			StatusFilter = filter;
		}

		protected string GetInitials(string name)
		{
			var cleaned = name?.Trim();
			if (string.IsNullOrWhiteSpace(cleaned))
				return "US";

			var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length >= 2)
				return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();

			return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
		}

		// Grupează persoanele monitorizate pe Id (aceeași persoană poate fi monitorizată de mai mulți utilizatori) și construiește un rând agregat per persoană
		private List<MonitoredPersonRow> BuildMonitoredRows(IEnumerable<MonitoredUserDTO> users)
		{
			var grouped = users
				.SelectMany(u => (u.MonitoredPeople ?? Array.Empty<MonitoredPersonDTO>()).Select(p => new { User = u, Person = p }))
				.GroupBy(x => x.Person.Id);

			var rows = new List<MonitoredPersonRow>();

			foreach (var group in grouped)
			{
				var sample = group.First();
				var person = sample.Person;

				// Deduplică monitorii după email, în caz că aceeași persoană apare de mai multe ori în date
				var monitors = group
					.Select(g => new MonitorInfo(FullName(g.User), g.User.Email))
					.GroupBy(m => m.Email, StringComparer.OrdinalIgnoreCase)
					.Select(g => g.First())
					.ToList();

				rows.Add(new MonitoredPersonRow(
					person.Id,
					$"{person.FirstName} {person.LastName}".Trim(),
					person.DeviceSerialNumber,
					person.IsActive && person.DeletedAt == null,
					FormatDate(person.UpdatedAt ?? person.CreatedAt),
					monitors,
					false,
					person.DeletedAt));
			}

			return rows;
		}

		private static bool MatchesSearch(MonitoredPersonRow row, string search)
		{
			if (string.IsNullOrWhiteSpace(search))
				return true;

			var term = search.Trim();
			return row.PersonName.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| row.DeviceSerial.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| row.Monitors.Any(m => m.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || m.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
		}

		private static bool MatchesFilter(MonitoredPersonRow row, string filter)
		{
			return filter switch
			{
				"online" => row.Online,
				"offline" => !row.Online,
				"alerts" => row.HasAlert,
				_ => true
			};
		}

		protected string GetFilterClass(string filter)
		{
			return StatusFilter == filter ? "filter-btn active" : "filter-btn";
		}

		protected string GetStatusClass(bool online)
		{
			return online ? "ok" : "offline";
		}

		protected string GetStatusText(bool online)
		{
			return online ? "Online" : "Offline";
		}

		private static bool IsAdminRole(string? role)
		{
			if (string.IsNullOrWhiteSpace(role))
				return false;

			return role.Contains("admin", StringComparison.OrdinalIgnoreCase);
		}

		protected string GetRowClass(bool online)
		{
			return online ? string.Empty : "row-offline";
		}

		// Navighează la pagina de detalii ESP+măsurători a persoanei monitorizate selectate (SelectedMonitoredPage)
		protected void NavigateToSelected(Guid personId)
		{
			NavigationManager.NavigateTo($"/view-selected-monitored/{personId}");
		}

		// Handler de tastatură pentru accesibilitate: Enter sau Spațiu pe un rând activează navigarea
		protected void OnRowKeyDown(KeyboardEventArgs e, Guid personId)
		{
			if (e.Key == "Enter" || e.Key == " ")
			{
				NavigateToSelected(personId);
			}
		}

		// Deschide modalul de confirmare soft-delete pentru rândul selectat
		protected void OpenDeleteConfirm(MonitoredPersonRow row)
		{
			_deleteTarget = row;
			_showDeleteConfirm = true;
		}

		// Închide modalul de confirmare și resetează ținta de ștergere
		protected void CloseDeleteConfirm()
		{
			_showDeleteConfirm = false;
			_deleteTarget = null;
		}

		// Confirmă ștergerea logică a persoanei monitorizate selectate, apoi reîncarcă lista pentru a reflecta noua stare
		protected async Task ConfirmSoftDeleteAsync()
		{
			if (_deleteTarget == null) return;
			_isProcessingDelete = true;
			try
			{
				await MonitoredApiClient.RemoveMonitoredAsync(_deleteTarget.PersonId);
				await LoadDataAsync();
			}
			finally
			{
				_isProcessingDelete = false;
				CloseDeleteConfirm();
			}
		}

		// Anulează ștergerea logică a unei persoane monitorizate (în perioada de grație) și reîncarcă lista
		protected async Task ReactivateAsync(Guid id)
		{
			await MonitoredApiClient.ReactivateMonitoredAsync(id);
			await LoadDataAsync();
		}

		// Consideră dispozitivul "online" doar dacă a trimis date ESP în ultimele `thresholdSeconds` secunde
		private static bool IsEspDataFresh(ESPDataResponseDTO esp, int thresholdSeconds)
		{
			if (esp.Date <= 0) return false;
			return (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - esp.Date) <= thresholdSeconds;
		}

		// Calculează zilele rămase până la ștergerea definitivă (perioadă de grație de 7 zile după soft-delete)
		protected static int DaysUntilDeletion(DateTime deletedAt)
		{
			var remaining = deletedAt.AddDays(7) - DateTime.UtcNow;
			return Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
		}

		// Construiește numele complet al utilizatorului supraveghetor (pentru coloana "Monitorizat de")
		protected string FullName(MonitoredUserDTO user)
		{
			return $"{user.FirstName} {user.LastName}".Trim();
		}

		// Formatează un DateTime nullable în ora locală (format yyyy-MM-dd HH:mm); returnează "-" dacă lipsește
		protected string FormatDate(DateTime? value)
		{
			return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";
		}

		protected sealed record MonitorInfo(string Name, string Email);

		protected sealed record MonitoredPersonRow(
			Guid PersonId,
			string PersonName,
			string DeviceSerial,
			bool Online,
			string LastUpdate,
			IReadOnlyList<MonitorInfo> Monitors,
			bool HasAlert,
			DateTime? DeletedAt);

		// Pentru fiecare persoană monitorizată, interoghează în paralel (limitat la 10 concurente) starea ESP și ultima măsurătoare,
		// pentru a determina dacă e online și dacă are o alertă activă — evită blocarea UI la liste mari
		private async Task PopulateOnlineAndAlertsAsync()
		{
			var semaphore = new SemaphoreSlim(10);
			var tasks = new List<Task>();
			foreach (var row in MonitoredRows.ToList())
			{
				await semaphore.WaitAsync();
				var personId = row.PersonId;
				var device = row.DeviceSerial;
				tasks.Add(Task.Run(async () =>
				{
					try
					{
						bool online = false;
						bool hasAlert = false;
						try
						{
							var esp = await MonitoredApiClient.GetEspDataAsync(device);
							online = esp?.IsAvailable == true && IsEspDataFresh(esp, 300);
						}
						catch { online = false; }

						MeasurementResponseDTO? last = null;
						try
						{
							var measurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(personId, 1, 1);
							last = measurements.FirstOrDefault();
							if (last != null)
							{
								// Praguri fixe de alertă rapidă pentru lista de sinteză (independent de pragurile personalizate ale persoanei)
								if (last.Pulse > 100 || last.Pulse < 50 || last.Temperature > 37.5 || last.IsFall)
									hasAlert = true;
							}
						}
						catch { /* ignore measurement errors */ }

						// Actualizare protejată cu lock — rândurile sunt modificate concurent din mai multe task-uri paralele
						lock (MonitoredRows)
						{
							var idx = MonitoredRows.FindIndex(r => r.PersonId == personId);
							if (idx >= 0)
							{
								var current = MonitoredRows[idx];
								var lastUpdate = current.LastUpdate;
								if (last != null)
								{
									lastUpdate = FormatDate(last.CreatedAt);
								}
								MonitoredRows[idx] = current with { Online = online, HasAlert = hasAlert, LastUpdate = lastUpdate };
							}
						}
					}
					finally
					{
						semaphore.Release();
					}
				}));
			}

			await Task.WhenAll(tasks);
			AlertsCount = MonitoredRows.Count(r => r.HasAlert);
		}
	}
}
