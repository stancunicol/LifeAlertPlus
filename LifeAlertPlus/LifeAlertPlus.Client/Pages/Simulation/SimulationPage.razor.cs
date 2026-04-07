using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using System.Linq;
using LifeAlertPlus.Shared.DTOs.Requests.Measurement;

namespace LifeAlertPlus.Client.Pages.Simulation
{
	public partial class SimulationPage
	{
		[Inject]
		private UserMonitoredService UserMonitoredService { get; set; } = default!;

		[Inject]
		private SimulationService SimulationService { get; set; } = default!;

		[Inject]
		private TokenParserService TokenParser { get; set; } = default!;

		[Inject]
		private MeasurementService MeasurementService { get; set; } = default!;

		[Inject]
		private IJSRuntime JSRuntime { get; set; } = default!;

		[Inject]
		private LanguageService Lang { get; set; } = default!;

		private string T(string key) => Lang.T(key);

		protected bool IsLoading { get; private set; } = true;
		protected string? ErrorMessage { get; private set; }
		protected List<SimPerson> Persons { get; } = new();
		protected string UserFullName { get; private set; } = "Admin";
		protected string ProfilePictureUrl { get; private set; } = string.Empty;
		private bool _disposed;

		protected string SearchQuery { get; set; } = string.Empty;
		protected string StatusFilter { get; set; } = "all";

		protected IEnumerable<SimPerson> FilteredPersons => Persons.Where(p =>
		{
			if (StatusFilter == "running" && !p.IsRunning) return false;
			if (StatusFilter == "idle" && p.IsRunning) return false;

			if (!string.IsNullOrWhiteSpace(SearchQuery))
			{
				var q = SearchQuery.Trim();
				return p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
					|| p.Serial.Contains(q, StringComparison.OrdinalIgnoreCase)
					|| p.UserName.Contains(q, StringComparison.OrdinalIgnoreCase)
					|| p.UserEmail.Contains(q, StringComparison.OrdinalIgnoreCase);
			}

			return true;
		});

		protected int RunningCount => Persons.Count(p => p.IsRunning);
		protected int IdleCount => Persons.Count(p => !p.IsRunning);

		protected void SetStatusFilter(string filter)
		{
			StatusFilter = filter;
		}

		protected override async Task OnInitializedAsync()
		{
			await LoadUserFromTokenAsync();
			await LoadAsync();
			await RestoreRunningSimulationsAsync();
		}

		private async Task LoadAsync()
		{
			IsLoading = true;
			ErrorMessage = null;
			Persons.Clear();

			try
			{
				var users = await UserMonitoredService.GetAllMonitoredUsersAsync();
				foreach (var user in users)
				{
					if (user.MonitoredPeople == null)
						continue;

					foreach (var person in user.MonitoredPeople)
					{
						if (string.IsNullOrWhiteSpace(person.DeviceSerialNumber))
							continue;

						Persons.Add(new SimPerson
						{
							PersonId = person.Id,
							Serial = person.DeviceSerialNumber.Trim(),
							Name = $"{person.FirstName} {person.LastName}".Trim(),
							UserName = $"{user.FirstName} {user.LastName}".Trim(),
							UserEmail = user.Email
						});
					}
				}
			}
			catch
			{
				ErrorMessage = "Failed to load the list of monitored persons.";
			}
			finally
			{
				IsLoading = false;
			}
		}

		protected Task RefreshAsync()
		{
			return LoadAsync();
		}

		protected async Task SendSimulationAsync(SimPerson person)
		{
			if (person.IsRunning || person.IsSending)
				return;

			person.IsSending = true;
			person.LastStatus = null;
			StateHasChanged();

			try
			{
				await SendForPersonAsync(person);
			}
			finally
			{
				person.IsSending = false;
				StateHasChanged();
			}
		}

		protected async Task SendAllAsync()
		{
			var candidates = Persons.Where(p => !p.IsRunning && !p.IsSending).ToList();
			if (!candidates.Any())
				return;

			foreach (var person in candidates)
			{
				person.LastStatus = null;
			}
			StateHasChanged();

			foreach (var person in candidates)
			{
				person.IsSending = true;
				StateHasChanged();
				await SendForPersonAsync(person);
				person.IsSending = false;
				StateHasChanged();
			}
		}

		private async Task SendForPersonAsync(SimPerson person)
		{
			try
			{
				var payload = LifeAlertPlus.Shared.Helpers.ESPDataGenerator.GeneratePayload(person.Serial);
				var sendTask = SimulationService.SendSimulationAsync(payload);
				var completed = await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(3)));
				var ok = completed == sendTask
					? await sendTask
					: true; // timeout: treat as success to keep UI responsive

			if (ok)
			{
				// Also save to measurements database
				var request = new MeasurementRequestDTO
				{
					Name = "Simulated",
					IdMonitored = person.PersonId,
					Pulse = payload.Max30100?[0] ?? 0,
					Temperature = payload.Temperature ?? 0,
					Activity = "Simulated Activity",
					IsFall = false,
					Coordinates = payload.Neo6m ?? string.Empty
				};

				// keep last coordinates on the UI even if saving fails
				person.LastCoordinates = request.Coordinates;

				// initialize the map immediately for this person (if coords parse)
				if (TryParseGpsToLatLonSimple(person.LastCoordinates, out var _lat, out var _lon))
				{
					try
					{
						await JSRuntime.InvokeVoidAsync("googleMapsInterop.initMapOnElementById", $"sim-map-{person.PersonId}", _lat, _lon);
						_mapCoords[person.PersonId] = person.LastCoordinates!;
					}
					catch
					{
						// ignore JS errors
					}
				}

				try
				{
					await MeasurementService.AddMeasurementAsync(request);
					person.LastStatus = SimStatus.Ok("Sent & Saved");
				}
				catch
				{
					person.LastStatus = SimStatus.Ok("Sent (save failed)");
				}
			}
			else
			{
				person.LastStatus = SimStatus.Warn("Error sending data");
			}
		}
		catch
		{
			person.LastStatus = SimStatus.Warn("Unexpected error");
		}
	}

	protected async Task StartAutoAsync(SimPerson person)
	{
		if (person.IsRunning)
			return;

		try
		{
			var ok = await SimulationService.StartSimulationAsync(person.PersonId);
			if (ok)
			{
				person.IsRunning = true;
				person.LastStatus = SimStatus.Ok("Auto running");
			}
			else
			{
				person.LastStatus = SimStatus.Warn("Failed to start");
			}
		}
		catch
		{
			person.LastStatus = SimStatus.Warn("Error starting");
		}
		StateHasChanged();
	}

	protected async Task StopAutoAsync(SimPerson person)
		{
			if (!person.IsRunning)
				return;

			try
			{
				var ok = await SimulationService.StopSimulationAsync(person.PersonId);
				person.IsRunning = false;
				person.LastStatus = ok ? SimStatus.Warn("Auto stopped") : SimStatus.Warn("Stop failed");
			}
			catch
			{
				person.IsRunning = false;
				person.LastStatus = SimStatus.Warn("Error stopping");
			}
			StateHasChanged();
		}

		protected async Task StartAllAutoAsync()
		{
			try
			{
				// Use the efficient server-side StartAll endpoint
				var ok = await SimulationService.StartAllSimulationsAsync();
				if (ok)
				{
					// Mark all eligible persons as running
					var targets = Persons.Where(p => !p.IsRunning).ToList();
					foreach (var person in targets)
					{
						person.IsRunning = true;
						person.LastStatus = SimStatus.Ok("Auto running");
					}
				}
				else
				{
					// Fallback: individual starts
					var targets = Persons.Where(p => !p.IsRunning).ToList();
					foreach (var person in targets)
					{
						await StartAutoAsync(person);
					}
				}
			}
			catch
			{
				// Fallback: individual starts
				var targets = Persons.Where(p => !p.IsRunning).ToList();
				foreach (var person in targets)
				{
					await StartAutoAsync(person);
				}
			}
			StateHasChanged();
		}

		protected async Task StopAllAutoAsync()
		{
			try
			{
				await SimulationService.StopAllSimulationsAsync();
				var running = Persons.Where(p => p.IsRunning).ToList();
				foreach (var person in running)
				{
					person.IsRunning = false;
					person.LastStatus = SimStatus.Warn("Auto stopped");
				}
			}
			catch
			{
				// best effort
			}
			StateHasChanged();
		}

		protected record SimStatus(bool Success, string Message)
		{
			public static SimStatus Ok(string msg) => new(true, msg);
			public static SimStatus Warn(string msg) => new(false, msg);
		}

		protected class SimPerson
		{
			public Guid PersonId { get; init; }
			public string Serial { get; init; } = string.Empty;
			public string Name { get; init; } = string.Empty;
			public string UserName { get; init; } = string.Empty;
			public string UserEmail { get; init; } = string.Empty;
			public SimStatus? LastStatus { get; set; }
			public bool IsRunning { get; set; }
			public bool IsSending { get; set; }
			public string? LastCoordinates { get; set; }
		}

		// Track last-used coordinates per person to avoid reinitializing unchanged maps
		private readonly Dictionary<Guid, string> _mapCoords = new();

		private bool TryParseGpsToLatLonSimple(string gps, out double lat, out double lon)
		{
			lat = 0; lon = 0;
			if (string.IsNullOrWhiteSpace(gps)) return false;
			var parts = gps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length < 2) return false;
			return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
				&& double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
		}

		protected override async Task OnAfterRenderAsync(bool firstRender)
		{
			if (_disposed) return;
			// initialize or update maps for persons that have coordinates — use element IDs
			foreach (var person in Persons.ToList())
			{
				if (string.IsNullOrWhiteSpace(person.LastCoordinates))
					continue;

				if (_mapCoords.TryGetValue(person.PersonId, out var prev) && prev == person.LastCoordinates)
					continue; // already initialized with same coords

				if (!TryParseGpsToLatLonSimple(person.LastCoordinates, out var lat, out var lon))
					continue;

				try
				{
					var elementId = $"sim-map-{person.PersonId}";
					await JSRuntime.InvokeVoidAsync("googleMapsInterop.initMapOnElementById", elementId, lat, lon);
					_mapCoords[person.PersonId] = person.LastCoordinates!;
				}
				catch
				{
					// ignore JS errors
				}
			}
		}

		private async Task LoadUserFromTokenAsync()
		{
			var claims = await TokenParser.GetClaimsAsync();
			if (claims == null)
				return;

			UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
			ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
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

		public void Dispose()
		{
			_disposed = true;
		}

		private async Task RestoreRunningSimulationsAsync()
		{
			try
			{
				var ids = await SimulationService.GetRunningSimulationsAsync();
				if (!ids.Any())
					return;

				// ensure Persons are loaded
				var attempts = 0;
				while (!Persons.Any() && attempts < 10)
				{
					await Task.Delay(200);
					attempts++;
				}
				if (!Persons.Any())
					return;

				foreach (var id in ids)
				{
					var person = Persons.FirstOrDefault(p => p.PersonId == id);
					if (person != null)
					{
						person.IsRunning = true;
						person.LastStatus = SimStatus.Ok("Auto running");
					}
				}
				StateHasChanged();
			}
			catch
			{
				// ignore restore errors
			}
		}
	}
}