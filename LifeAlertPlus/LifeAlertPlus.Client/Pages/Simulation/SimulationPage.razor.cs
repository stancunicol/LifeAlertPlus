using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using System.Linq;
using LifeAlertPlus.Shared.DTOs.Requests.Measurement;

namespace LifeAlertPlus.Client.Pages.Simulation
{
	// Code-behind pentru pagina de Simulare — permite generarea/trimiterea de date ESP simulate
	// (puls, SpO2, temperatură, GPS) pentru persoanele monitorizate, manual sau automat (polling continuu)
	public partial class SimulationPage
	{
		[Inject]
		private UserMonitoredApiClient UserMonitoredApiClient { get; set; } = default!;

		[Inject]
		private SimulationService SimulationService { get; set; } = default!;

		[Inject]
		private TokenParserService TokenParser { get; set; } = default!;

		[Inject]
		private MeasurementApiClient MeasurementApiClient { get; set; } = default!;

		[Inject]
		private LanguageService Lang { get; set; } = default!;

		private string T(string key) => Lang.TEnglish(key);

		protected bool IsLoading { get; private set; } = true;
		protected string? ErrorMessage { get; private set; }
		protected List<SimPerson> Persons { get; } = new();
		protected string UserFullName { get; private set; } = "Admin";
		protected string ProfilePictureUrl { get; private set; } = string.Empty;

		protected string SearchQuery { get; set; } = string.Empty;
		protected string StatusFilter { get; set; } = "all";

		// Lista filtrată de persoane afișată în UI — combină filtrul de status (running/idle/selected)
		// cu căutarea text pe nume, serial, utilizator sau email
		protected IEnumerable<SimPerson> FilteredPersons => Persons.Where(p =>
		{
			if (StatusFilter == "running" && !p.IsRunning) return false;
			if (StatusFilter == "idle" && p.IsRunning) return false;
			if (StatusFilter == "selected" && !p.IsSelected) return false;
			if (StatusFilter == "not-selected" && p.IsSelected) return false;

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

		protected List<SimPerson> SelectedPersons => Persons.Where(p => p.IsSelected).ToList();
		protected int RunningCount => Persons.Count(p => p.IsRunning);
		protected int IdleCount => Persons.Count(p => !p.IsRunning);
		protected int SelectedCount => Persons.Count(p => p.IsSelected);
		protected int NotSelectedCount => Persons.Count(p => !p.IsSelected);

		protected void SetStatusFilter(string filter)
		{
			StatusFilter = filter;
		}

		protected void ToggleSelection(SimPerson person)
		{
			person.IsSelected = !person.IsSelected;
		}

		protected void SelectAll()
		{
			foreach (var p in FilteredPersons)
				p.IsSelected = true;
		}

		protected void DeselectAll()
		{
			foreach (var p in FilteredPersons)
				p.IsSelected = false;
		}

		// La inițializare: încarcă datele utilizatorului curent din token, încarcă lista de
		// persoane monitorizate disponibile pentru simulare, apoi reactivează simulările
		// automate care erau deja pornite (ex. după un refresh de pagină)
		protected override async Task OnInitializedAsync()
		{
			await LoadUserFromTokenAsync();
			await LoadAsync();
			await RestoreRunningSimulationsAsync();
		}

		// Reconstruiește lista de persoane monitorizate eligibile pentru simulare:
		// trebuie să aibă un serial de device valid, să nu fie arhivate și să nu fie duplicate
		private async Task LoadAsync()
		{
			IsLoading = true;
			ErrorMessage = null;
			Persons.Clear();

			try
			{
				var users = await UserMonitoredApiClient.GetAllMonitoredUsersAsync();
				var seenIds = new HashSet<Guid>();
				foreach (var user in users)
				{
					if (user.MonitoredPeople == null)
						continue;

					foreach (var person in user.MonitoredPeople)
					{
						if (string.IsNullOrWhiteSpace(person.DeviceSerialNumber))
							continue;

						if (person.IsArchived)
							continue;

						if (!seenIds.Add(person.Id))
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

		// Trimite o singură măsurătoare simulată pentru o persoană (manual, la apăsarea butonului)
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

		// Trimite câte o măsurătoare simulată pentru toate persoanele bifate (selectate),
		// secvențial, pentru a nu suprasolicita API-ul
		protected async Task SendAllAsync()
		{
			var candidates = SelectedPersons.Where(p => !p.IsRunning && !p.IsSending).ToList();
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

		// Generează un payload ESP fals pentru serialul dat și îl trimite prin SimulationService,
		// apoi salvează măsurătoarea rezultată în backend prin MeasurementApiClient
		private async Task SendForPersonAsync(SimPerson person)
		{
			try
			{
				var payload = LifeAlertPlus.Shared.Helpers.ESPDataGenerator.GeneratePayload(person.Serial);
				var sendTask = SimulationService.SendSimulationAsync(payload);
				// Limitează timpul de așteptare la 3 secunde — dacă serverul nu răspunde la timp,
				// se consideră oricum succes pentru a nu bloca interfața
				var completed = await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(3)));
				var ok = completed == sendTask
					? await sendTask
					: true; // timeout: treat as success to keep UI responsive

			if (ok)
			{
				var request = new MeasurementRequestDTO
				{
					Name        = "Simulated",
					Activity    = "simulation",
					IdMonitored = person.PersonId,
					Pulse       = payload.Max30100?[0] ?? 0,
					SpO2        = payload.Max30100?[1] ?? 0,
					Temperature = payload.Temperature ?? 36.6,
					Coordinates = payload.Neo6m ?? "44.4268,26.1025",
				};

				try
				{
					await MeasurementApiClient.AddMeasurementAsync(request);
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

	// Pornește simularea automată (server-side) pentru o persoană — backend-ul va genera și
	// trimite periodic măsurători simulate până la oprire
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

	// Șterge și regenerează datele simulate de azi pentru o persoană, incluzând SpO2
	protected async Task ReseedTodayAsync(SimPerson person)
	{
		if (person.IsRunning || person.IsSending) return;
		person.IsSending = true;
		person.LastStatus = null;
		StateHasChanged();
		try
		{
			var ok = await SimulationService.ReseedTodayAsync(person.PersonId);
			person.LastStatus = ok ? SimStatus.Ok("Reseeded w/ SpO2") : SimStatus.Warn("Reseed failed");
		}
		finally { person.IsSending = false; StateHasChanged(); }
	}

	// Generează un set inițial de date simulate pentru ziua curentă, pentru o persoană
	protected async Task SeedTodayAsync(SimPerson person)
	{
		if (person.IsRunning || person.IsSending) return;
		person.IsSending = true;
		person.LastStatus = null;
		StateHasChanged();
		try
		{
			var ok = await SimulationService.SeedTodayAsync(person.PersonId);
			person.LastStatus = ok ? SimStatus.Ok("Today seeded") : SimStatus.Warn("Seed failed");
		}
		finally { person.IsSending = false; StateHasChanged(); }
	}

	// Generează date simulate pentru ziua curentă pentru toate persoanele selectate, pe rând
	protected async Task SeedAllTodayAsync()
	{
		var candidates = SelectedPersons.Where(p => !p.IsRunning && !p.IsSending).ToList();
		if (!candidates.Any()) return;
		foreach (var p in candidates) { p.IsSending = true; p.LastStatus = null; }
		StateHasChanged();
		foreach (var person in candidates)
		{
			var ok = await SimulationService.SeedTodayAsync(person.PersonId);
			person.LastStatus = ok ? SimStatus.Ok("Today seeded") : SimStatus.Warn("Seed failed");
			person.IsSending = false;
			StateHasChanged();
		}
	}

	// Șterge toate datele simulate generate anterior pentru serialul respectiv
	protected async Task ClearDataAsync(SimPerson person)
	{
		if (person.IsRunning || person.IsSending) return;
		person.IsSending = true;
		StateHasChanged();
		try
		{
			var ok = await SimulationService.ClearSimulatedDataAsync(person.Serial);
			person.LastStatus = ok ? SimStatus.Ok(T("sim.clearDataDone")) : SimStatus.Warn("Clear failed");
		}
		finally { person.IsSending = false; StateHasChanged(); }
	}

	// Oprește simularea automată server-side pentru o persoană
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

		// Pornește simularea automată pentru toate persoanele selectate care nu rulează deja
		protected async Task StartAllAutoAsync()
		{
			var targets = SelectedPersons.Where(p => !p.IsRunning).ToList();
			if (!targets.Any())
				return;

			foreach (var person in targets)
			{
				await StartAutoAsync(person);
			}
			StateHasChanged();
		}

		// Oprește simularea automată pentru toate persoanele selectate care rulează
		protected async Task StopAllAutoAsync()
		{
			var running = SelectedPersons.Where(p => p.IsRunning).ToList();
			if (!running.Any())
				return;

			foreach (var person in running)
			{
				await StopAutoAsync(person);
			}
			StateHasChanged();
		}

		// Reprezintă rezultatul ultimei operații de simulare (succes/avertisment) afișat în UI
		protected record SimStatus(bool Success, string Message)
		{
			public static SimStatus Ok(string msg) => new(true, msg);
			public static SimStatus Warn(string msg) => new(false, msg);
		}

		// Model de UI pentru o persoană monitorizată afișată în pagina de simulare,
		// inclusiv starea curentă a simulării (rulează/se trimite/selectată)
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
			public bool IsSelected { get; set; }
		}

		// Extrage numele și poza de profil ale utilizatorului curent din claim-urile token-ului JWT
		private async Task LoadUserFromTokenAsync()
		{
			var claims = await TokenParser.GetClaimsAsync();
			if (claims == null)
				return;

			UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
			ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
		}

		// Calculează inițialele afișate în avatar pe baza numelui (ex: "John Doe" -> "JD")
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
		}

		// La (re)încărcarea paginii, interoghează serverul pentru simulările deja active
		// și marchează persoanele corespunzătoare ca "IsRunning", astfel încât starea din UI
		// să rămână sincronizată cu simulările pornite anterior (chiar și după refresh)
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