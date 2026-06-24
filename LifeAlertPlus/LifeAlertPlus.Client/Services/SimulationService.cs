using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;

namespace LifeAlertPlus.Client.Services
{
	// Client HTTP pentru endpoint-urile /api/esp/simulate și /api/simulations — controlează simularea
	// de date ESP (pornire/oprire continuă, payload unic, reseed/seed date pentru grafice)
	public class SimulationService
	{
		private readonly HttpClient _httpClient;

		// API Endpoints
		private const string SimulateEndpoint = "api/esp/simulate";
		private const string StartEndpoint = "api/simulations/start";
		private const string StopEndpoint = "api/simulations/stop";
		private const string StopAllEndpoint = "api/simulations/stopAll";
		private const string RunningEndpoint = "api/simulations/running";

		public SimulationService(HttpClient httpClient)
		{
			_httpClient = httpClient;
		}

		// Șterge toate datele simulate generate pentru un dispozitiv (după numărul de serie)
		public async Task<bool> ClearSimulatedDataAsync(string serial)
		{
			try
			{
				var response = await _httpClient.DeleteAsync($"api/esp/simulate/{Uri.EscapeDataString(serial)}");
				return response.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		// Trimite manual un singur payload simulat (funcția "Generate once" din UI)
		public async Task<bool> SendSimulationAsync(ESPDataResponseDTO payload)
		{
			try
			{
				var response = await _httpClient.PostAsJsonAsync(SimulateEndpoint, payload);
				return response.IsSuccessStatusCode;
			}
			catch
			{
				return false;
			}
		}

		// Pornește simularea continuă (server-side) pentru o persoană monitorizată
		public async Task<bool> StartSimulationAsync(Guid personId)
		{
			try
			{
				var response = await _httpClient.PostAsync($"{StartEndpoint}/{personId}", null);
				return response.IsSuccessStatusCode;
			}
			catch
			{
				return false;
			}
		}

		// Pornește simularea continuă pentru toate persoanele monitorizate
		public async Task<bool> StartAllSimulationsAsync()
		{
			try
			{
				var response = await _httpClient.PostAsync("api/simulations/startAll", null);
				return response.IsSuccessStatusCode;
			}
			catch
			{
				return false;
			}
		}

		// Oprește simularea continuă pentru o persoană monitorizată
		public async Task<bool> StopSimulationAsync(Guid personId)
		{
			try
			{
				var response = await _httpClient.PostAsync($"{StopEndpoint}/{personId}", null);
				return response.IsSuccessStatusCode;
			}
			catch
			{
				return false;
			}
		}

		// Oprește toate simulările active de pe server
		public async Task<bool> StopAllSimulationsAsync()
		{
			try
			{
				var response = await _httpClient.PostAsync(StopAllEndpoint, null);
				return response.IsSuccessStatusCode;
			}
			catch
			{
				return false;
			}
		}

		// Forțează regenerarea datelor de azi — elimină rândurile seed cu SpO2 zero apoi
		// le regenerează cu valori corecte
		public async Task<bool> ReseedTodayAsync(Guid personId)
		{
			try
			{
				var response = await _httpClient.PostAsync($"api/simulations/reseedToday/{personId}", null);
				return response.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		// Generează date inițiale (seed) pentru graficul zilei curente — o măsurătoare la fiecare
		// 30 de minute, de la miezul nopții până acum
		public async Task<bool> SeedTodayAsync(Guid personId)
		{
			try
			{
				var response = await _httpClient.PostAsync($"api/simulations/seedToday/{personId}", null);
				return response.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		// Obține lista id-urilor persoanelor pentru care simularea rulează curent pe server
		public async Task<IEnumerable<Guid>> GetRunningSimulationsAsync()
		{
			try
			{
				var response = await _httpClient.GetAsync(RunningEndpoint);
				if (response.IsSuccessStatusCode)
				{
					var ids = await response.Content.ReadFromJsonAsync<IEnumerable<Guid>>();
					return ids ?? Enumerable.Empty<Guid>();
				}
			}
			catch
			{
				// Log errors in production environment
			}
			return Enumerable.Empty<Guid>();
		}
	}
}