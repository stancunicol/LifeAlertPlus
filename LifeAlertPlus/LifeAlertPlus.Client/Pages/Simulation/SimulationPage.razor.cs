using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;

namespace LifeAlertPlus.Client.Pages.Simulation
{
	public partial class SimulationPage : ComponentBase
	{
		[Inject]
		private UserMonitoredService UserMonitoredService { get; set; } = default!;

		[Inject]
		private SimulationService SimulationService { get; set; } = default!;

		[Inject]
		private TokenParserService TokenParser { get; set; } = default!;

		protected bool IsLoading { get; private set; } = true;
		protected bool IsBusy { get; private set; }
		protected string? ErrorMessage { get; private set; }
		protected List<SimPerson> Persons { get; } = new();
		protected string UserFullName { get; private set; } = "Admin";
		protected string ProfilePictureUrl { get; private set; } = string.Empty;

		protected override async Task OnInitializedAsync()
		{
			await LoadUserFromTokenAsync();
			await LoadAsync();
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
			if (IsBusy)
				return;

			IsBusy = true;
			person.LastStatus = null;
			StateHasChanged();

			try
			{
				await SendForPersonAsync(person);
			}
			finally
			{
				IsBusy = false;
			}
		}

		protected async Task SendAllAsync()
		{
			if (IsBusy || !Persons.Any())
				return;

			IsBusy = true;
			foreach (var person in Persons)
			{
				person.LastStatus = null;
			}
			StateHasChanged();

			try
			{
				foreach (var person in Persons)
				{
					await SendForPersonAsync(person);
					StateHasChanged();
				}
			}
			finally
			{
				IsBusy = false;
			}
		}

		private async Task SendForPersonAsync(SimPerson person)
		{
			try
			{
				var payload = BuildPayload(person.Serial);
				var ok = await SimulationService.SendSimulationAsync(payload);
				person.LastStatus = ok
					? SimStatus.Ok("Sent")
					: SimStatus.Warn("Error sending data");
			}
			catch
			{
				person.LastStatus = SimStatus.Warn("Unexpected error");
			}
		}

		private static ESPDataResponseDTO BuildPayload(string serial)
		{
			var rnd = Random.Shared;
			var pulse = rnd.Next(62, 101);
			var spo2 = rnd.Next(93, 99);
			var temp = 36.2 + rnd.NextDouble() * 1.2; // 36.2 - 37.4
			var battery = 30 + rnd.NextDouble() * 70; // 30-100

			return new ESPDataResponseDTO
			{
				Serial = serial,
				Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
				IsAvailable = true,
				Mpu6050 = new List<int> { rnd.Next(-16000, 16001), rnd.Next(-16000, 16001), rnd.Next(-16000, 16001) },
				Gyro = new List<int> { rnd.Next(-5000, 5001), rnd.Next(-5000, 5001), rnd.Next(-5000, 5001) },
				Max30100 = new List<int> { pulse, spo2 },
				Hmc5883l = rnd.Next(-800, 801),
				Neo6m = "$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A",
				Temperature = Math.Round(temp, 1),
				Battery = Math.Round(battery, 1)
			};
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
	}
}