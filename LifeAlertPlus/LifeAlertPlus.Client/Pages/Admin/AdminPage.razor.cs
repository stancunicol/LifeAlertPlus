using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Admin;

public partial class AdminPage : ComponentBase
{
	[Inject]
	private TokenParserService TokenParser { get; set; } = default!;

	[Inject]
	private UserMonitoredApiClient UserMonitoredApiClient { get; set; } = default!;

	[Inject]
	private NavigationManager Navigation { get; set; } = default!;

	[Inject]
	private MonitoredApiClient MonitoredApiClient { get; set; } = default!;

	[Inject]
	private MeasurementApiClient MeasurementApiClient { get; set; } = default!;

	[Inject]
	private LanguageService Lang { get; set; } = default!;

	[Inject]
	private AdminApiClient AdminApi { get; set; } = default!;

	[Inject]
	private HttpClient Http { get; set; } = default!;

	private string T(string key) => Lang.TEnglish(key);

	protected string UserFullName { get; set; } = "Admin";
	protected string ProfilePictureUrl { get; set; } = string.Empty;

	protected IReadOnlyList<MonitoredUserDTO> AdminUsers { get; private set; } = Array.Empty<MonitoredUserDTO>();
	protected bool IsLoading { get; set; } = true;
	protected string? ErrorMessage { get; set; }

	protected int TotalUsers => AdminUsers.Count;
	protected int TotalMonitored => AdminUsers.Sum(u => u.MonitoredPeople.Count);
	protected int ActiveMonitored => AdminUsers.Sum(u => u.MonitoredPeople.Count(m => m.IsActive && m.DeletedAt == null));
	protected int InactiveMonitored => AdminUsers.Sum(u => u.MonitoredPeople.Count(m => !m.IsActive || m.DeletedAt != null));
    protected int ActiveUsers => AdminUsers.Count(u => u.IsActive);

	// New admin stats
	protected int OnlineMonitored { get; set; }
	protected int OfflineMonitored { get; set; }
	protected int AlertsCount { get; set; }
	protected int MeasurementsRecentCount { get; set; }
	protected int MeasurementsStaleCount { get; set; }
	protected int MeasurementsNoneCount { get; set; }
	protected int MeasurementsTodayCount { get; set; }
	protected List<NotificationItem> Notifications { get; set; } = new();
	protected List<DeviceStatusRow> DeviceStatuses { get; set; } = new();
	protected List<AdminApiClient.AuditEntryDTO> RecentAudit { get; set; } = new();

	// Pagination for device status table
	protected int DevicePageSize = 10;
	protected int DeviceCurrentPage = 1;
	protected IEnumerable<DeviceStatusRow> PaginatedDevices => DeviceStatuses
		.Skip((DeviceCurrentPage - 1) * DevicePageSize)
		.Take(DevicePageSize);
	protected int DeviceTotalPages => (DeviceStatuses.Count + DevicePageSize - 1) / DevicePageSize;

	// Per-person status map
	protected Dictionary<Guid, PersonStatus> PersonStatuses { get; set; } = new();

	protected IEnumerable<MonitoredRow> MonitoredRows => AdminUsers
		.SelectMany(u => u.MonitoredPeople.Select(m => new MonitoredRow(
			$"{m.FirstName} {m.LastName}".Trim(),
			0, // Age not available in DTO
			$"{u.FirstName} {u.LastName}".Trim(),
			u.Email,
			m.DeviceSerialNumber,
			FormatDate(m.UpdatedAt ?? m.CreatedAt),
			PersonStatuses.TryGetValue(m.Id, out var s) ? s.Online : (m.IsActive && m.DeletedAt == null),
			PersonStatuses.TryGetValue(m.Id, out var s2) ? s2.HasAlert : false,
			PersonStatuses.TryGetValue(m.Id, out var s3) ? s3.MeasurementStatus : "Unknown")));

	protected override async Task OnInitializedAsync()
	{
		await LoadUserFromTokenAsync();
		await LoadDataAsync();
	}

	private async Task LoadUserFromTokenAsync()
	{
		var claims = await TokenParser.GetClaimsAsync();
		if (claims == null)
		{
			Navigation.NavigateTo("/login");
			return;
		}

		UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
		ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
	}

	private async Task LoadDataAsync()
	{
		IsLoading = true;
		ErrorMessage = null;

		try
		{
			var monitoredUsers = await UserMonitoredApiClient.GetAllMonitoredUsersAsync();
			AdminUsers = monitoredUsers.ToList();
			// Reset computed status
			PersonStatuses = new Dictionary<Guid, PersonStatus>();

			// Build list of all monitored people
			var allPersons = AdminUsers.SelectMany(u => u.MonitoredPeople).ToList();

			// Parallel fetch ESP data and latest measurement per monitored person (limited concurrency)
			var semaphore = new System.Threading.SemaphoreSlim(10);
			var tasks = new List<Task>();
			foreach (var person in allPersons)
			{
				await semaphore.WaitAsync();
				tasks.Add(Task.Run(async () =>
				{
					try
					{
						bool online = false;
						try
						{
							var esp = await MonitoredApiClient.GetEspDataAsync(person.DeviceSerialNumber);
							online = esp?.IsAvailable == true;
						}
						catch { online = false; }

						// Fetch last measurement (page 1, size 1)
						var measurements = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(person.Id, 1, 1);
						var last = measurements.FirstOrDefault();

						string measurementStatus;
						bool hasAlert = false;
						if (last == null)
						{
							measurementStatus = "No measurements";
						}
						else
						{
							var age = DateTime.UtcNow - last.CreatedAt.ToUniversalTime();
							if (age.TotalMinutes <= 15) measurementStatus = "Recent";
							else if (age.TotalHours < 24) measurementStatus = "Stale";
							else measurementStatus = "Old";

							// Determine alert by simple heuristics
							if (last.Pulse > 100 || last.Pulse < 50 || last.Temperature > 37.5 || last.IsFall)
								hasAlert = true;
						}

						lock (PersonStatuses)
						{
							PersonStatuses[person.Id] = new PersonStatus
							{
								Online = online,
								HasAlert = hasAlert,
								MeasurementStatus = measurementStatus,
								LastMeasurementAt = last?.CreatedAt
							};
						}
					}
					finally
					{
						semaphore.Release();
					}
				}));
			}

			await Task.WhenAll(tasks);

			// Compute summary stats
			OnlineMonitored = PersonStatuses.Count(p => p.Value.Online);
			OfflineMonitored = PersonStatuses.Count - OnlineMonitored;
			AlertsCount = PersonStatuses.Count(p => p.Value.HasAlert);
			MeasurementsRecentCount = PersonStatuses.Count(p => p.Value.MeasurementStatus == "Recent");
			MeasurementsStaleCount = PersonStatuses.Count(p => p.Value.MeasurementStatus == "Stale");
			MeasurementsNoneCount = PersonStatuses.Count(p => p.Value.MeasurementStatus == "No measurements");

			// Total measurements taken today (global)
			try
			{
				MeasurementsTodayCount = await MeasurementApiClient.GetTodayMeasurementsCountAsync();
			}
			catch
			{
				MeasurementsTodayCount = 0;
			}

			// Build notifications list (alerts, offline, stale/no measurements)
			Notifications = new List<NotificationItem>();
			foreach (var person in allPersons)
			{
				PersonStatus? st = null;
				PersonStatuses.TryGetValue(person.Id, out st);

				if (st == null)
				{
					Notifications.Add(new NotificationItem(person.Id, FullName(person), "No measurements reported.", "info", null));
					continue;
				}

				if (st.HasAlert)
				{
					Notifications.Add(new NotificationItem(person.Id, FullName(person), "Alert detected — check measurements.", "critical", st.LastMeasurementAt));
					continue;
				}

				if (!st.Online)
				{
					Notifications.Add(new NotificationItem(person.Id, FullName(person), "Device offline.", "warning", st.LastMeasurementAt));
					continue;
				}

				if (st.MeasurementStatus == "Stale" || st.MeasurementStatus == "Old")
				{
					Notifications.Add(new NotificationItem(person.Id, FullName(person), $"Measurement status: {st.MeasurementStatus}.", "warning", st.LastMeasurementAt));
				}
			}

			// Keep only the last 10 notifications sorted by time (most recent first)
			Notifications = Notifications
				.OrderByDescending(n => n.Time ?? DateTime.MinValue)
				.Take(10)
				.ToList();

			// Load device status table and recent audit in parallel.
			var deviceTask = LoadDeviceStatusesAsync();
			var auditTask  = LoadRecentAuditAsync();
			await Task.WhenAll(deviceTask, auditTask);
		}
		catch (Exception ex)
		{
			ErrorMessage = $"Failed to load data: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}

	private async Task LoadDeviceStatusesAsync()
	{
		try
		{
			var rows = await Http.GetFromJsonAsync<List<DeviceStatusRow>>("api/admin/device-status");
			DeviceStatuses = rows ?? new List<DeviceStatusRow>();
		}
		catch { DeviceStatuses = new List<DeviceStatusRow>(); }
	}

	private async Task LoadRecentAuditAsync()
	{
		try
		{
			var entries = await AdminApi.GetAuditLogAsync(10);
			RecentAudit = entries;
		}
		catch { RecentAudit = new List<AdminApiClient.AuditEntryDTO>(); }
	}

	protected static string FormatUptime(int? seconds)
	{
		if (!seconds.HasValue) return "-";
		var s = seconds.Value;
		if (s < 60) return $"{s}s";
		if (s < 3600) return $"{s / 60}m {s % 60}s";
		return $"{s / 3600}h {(s % 3600) / 60}m";
	}

	protected class PersonStatus
	{
		public bool Online { get; set; }
		public bool HasAlert { get; set; }
		public string MeasurementStatus { get; set; } = "No measurements";
		public DateTime? LastMeasurementAt { get; set; }
	}

	protected string GetStatusClass(bool online)
	{
		return online ? "ok" : "offline";
	}

	protected string GetStatusText(bool online)
	{
		return online ? "Online" : "Offline";
	}

	protected string GetRowClass(bool online)
	{
		return online ? string.Empty : "row-offline";
	}

	protected string GetInitials(string name)
	{
		var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length >= 2)
		{
			return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
		}

		return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
	}

	protected string FullName(MonitoredUserDTO user) => $"{user.FirstName} {user.LastName}".Trim();

	protected string FullName(MonitoredPersonDTO monitored) => $"{monitored.FirstName} {monitored.LastName}".Trim();

	protected bool IsUserActive(MonitoredUserDTO user) => user.IsActive;

	protected string UserRoleLabel(MonitoredUserDTO user) => user.Role;

	protected string FormatDate(DateTime dateTime) => dateTime.ToLocalTime().ToString("g");

	protected int GetAge(MonitoredPersonDTO person)
	{
		// Age not available in DTO, return 0 or calculate if needed
		return 0;
	}

	protected sealed record MonitoredRow(
		string PersonName,
		int Age,
		string UserName,
		string UserEmail,
		string DeviceSerial,
		string LastUpdate,
		bool Online,
		bool HasAlert,
		string MeasurementStatus);

	protected sealed record NotificationItem(Guid PersonId, string Title, string Message, string Level, DateTime? Time);

	protected sealed class DeviceStatusRow
	{
		public Guid Id { get; set; }
		public string PatientName { get; set; } = string.Empty;
		public string DeviceSerialNumber { get; set; } = string.Empty;
		public bool IsArchived { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime? DeletedAt { get; set; }
		public bool IsOnline { get; set; }
		public double? Battery { get; set; }
		public int? RssiDbm { get; set; }
		public int? UptimeSeconds { get; set; }
		public int? HeartbeatAgeSec { get; set; }
		public long? LastDataDate { get; set; }
	}

	protected async Task ReactivateMonitoredAsync(Guid id)
	{
		var ok = await MonitoredApiClient.ReactivateMonitoredAsync(id);
		if (ok)
			await LoadDeviceStatusesAsync();
		StateHasChanged();
	}


	protected void GoToDevicePage(int page)
	{
		if (page >= 1 && page <= DeviceTotalPages)
		{
			DeviceCurrentPage = page;
		}
	}

	protected void ChangeDevicePageSize(int newSize)
	{
		DevicePageSize = newSize;
		DeviceCurrentPage = 1;
	}

	protected void OnDevicePageSizeChanged(ChangeEventArgs e)
	{
		if (int.TryParse(e.Value?.ToString(), out int newSize))
		{
			ChangeDevicePageSize(newSize);
		}
	}
}
