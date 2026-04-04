using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Text.Json;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.MonitoredUsers
{
	public partial class MonitoredUsersPage : ComponentBase
	{
		[Inject]
		private UserMonitoredService UserMonitoredService { get; set; } = default!;

		[Inject]
		private MonitoredService MonitoredService { get; set; } = default!;

		[Inject]
		private MeasurementService MeasurementService { get; set; } = default!;

		[Inject]
		private TokenParserService TokenParser { get; set; } = default!;

		[Inject]
		private IJSRuntime JSRuntime { get; set; } = default!;

		[Inject]
		private NavigationManager NavigationManager { get; set; } = default!;

		[Inject]
		private LanguageService Lang { get; set; } = default!;

		private string T(string key) => Lang.T(key);

		protected List<MonitoredUserDTO> Users { get; private set; } = new();
		protected List<MonitoredPersonRow> MonitoredRows { get; private set; } = new();
		protected string UserFullName { get; private set; } = "Admin";
		protected string ProfilePictureUrl { get; private set; } = string.Empty;
		protected string SearchText { get; set; } = string.Empty;
		protected string StatusFilter { get; private set; } = "all";
		protected bool IsLoading { get; private set; } = true;
		protected string? ErrorMessage { get; private set; }
		private string? _currentUserEmail;

		protected int TotalMonitors => MonitoredRows
			.SelectMany(r => r.Monitors)
			.Select(m => m.Email)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Count();
		protected int TotalMonitored => MonitoredRows.Count;
		protected int OnlineMonitoredCount => MonitoredRows.Count(r => r.Online);
		protected int OfflineMonitoredCount => MonitoredRows.Count(r => !r.Online);
		protected int AlertsCount { get; set; }

		protected IEnumerable<MonitoredPersonRow> FilteredRows => MonitoredRows
			.Where(r => MatchesSearch(r, SearchText))
			.Where(r => MatchesFilter(r, StatusFilter))
			.OrderBy(r => r.PersonName);

		protected override async Task OnInitializedAsync()
		{
			await LoadUserFromTokenAsync();
			await LoadDataAsync();
		}

		private async Task LoadUserFromTokenAsync()
		{
			var claims = await TokenParser.GetClaimsAsync();
			if (claims == null)
				return;

			UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
			ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
			_currentUserEmail = claims.Email;
		}

		protected async Task LoadDataAsync()
		{
			IsLoading = true;
			ErrorMessage = null;

			try
			{
				var data = await UserMonitoredService.GetAllMonitoredUsersAsync();
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
					false));
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

		protected void NavigateToSelected(Guid personId)
		{
			NavigationManager.NavigateTo($"/view-selected-monitored/{personId}");
		}

		protected void OnRowKeyDown(KeyboardEventArgs e, Guid personId)
		{
			if (e.Key == "Enter" || e.Key == " ")
			{
				NavigateToSelected(personId);
			}
		}

		protected string FullName(MonitoredUserDTO user)
		{
			return $"{user.FirstName} {user.LastName}".Trim();
		}

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
			bool HasAlert);

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
							var esp = await MonitoredService.GetEspDataAsync(device);
							online = esp?.IsAvailable == true;
						}
						catch { online = false; }

						MeasurementResponseDTO? last = null;
						try
						{
							var measurements = await MeasurementService.GetMeasurementsByMonitoredIdAsync(personId, 1, 1);
							last = measurements.FirstOrDefault();
							if (last != null)
							{
								if (last.Pulse > 100 || last.Pulse < 50 || last.Temperature > 37.5 || last.IsFall)
									hasAlert = true;
							}
						}
						catch { /* ignore measurement errors */ }

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
