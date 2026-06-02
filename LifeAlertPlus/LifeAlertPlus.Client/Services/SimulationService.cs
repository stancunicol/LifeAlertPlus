using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;

namespace LifeAlertPlus.Client.Services
{
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

		/// <summary>
		public async Task<bool> ClearSimulatedDataAsync(string serial)
		{
			try
			{
				var response = await _httpClient.DeleteAsync($"api/esp/simulate/{Uri.EscapeDataString(serial)}");
				return response.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		/// Send a single simulated payload manually (for "Generate once" feature)
		/// </summary>
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

		/// <summary>
		/// Start continuous simulation for a monitored person
		/// </summary>
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

		/// <summary>
		/// Start continuous simulations for all monitored persons
		/// </summary>
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

		/// <summary>
		/// Stop continuous simulation for a monitored person
		/// </summary>
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

		/// <summary>
		/// Stop all running simulations
		/// </summary>
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

		/// <summary>
		/// Force-reseed today — removes zero-SpO2 seed rows then regenerates with correct values
		/// </summary>
		public async Task<bool> ReseedTodayAsync(Guid personId)
		{
			try
			{
				var response = await _httpClient.PostAsync($"api/simulations/reseedToday/{personId}", null);
				return response.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		/// <summary>
		/// Seed today's chart data (one measurement per 30 min from midnight to now)
		/// </summary>
		public async Task<bool> SeedTodayAsync(Guid personId)
		{
			try
			{
				var response = await _httpClient.PostAsync($"api/simulations/seedToday/{personId}", null);
				return response.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		/// <summary>
		/// Get list of currently running simulation person IDs
		/// </summary>
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