using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;

namespace LifeAlertPlus.Client.Services
{
	public class SimulationService
	{
		private readonly HttpClient _httpClient;

		public SimulationService(HttpClient httpClient)
		{
			_httpClient = httpClient;
		}

		public async Task<bool> SendSimulationAsync(ESPDataResponseDTO payload)
		{
			var response = await _httpClient.PostAsJsonAsync("api/esp/simulate", payload);
			return response.IsSuccessStatusCode;
		}

		public async Task<bool> StartSimulationAsync(Guid personId)
		{
			var response = await _httpClient.PostAsync($"api/simulations/start/{personId}", null);
			return response.IsSuccessStatusCode;
		}

		public async Task<bool> StopSimulationAsync(Guid personId)
		{
			var response = await _httpClient.PostAsync($"api/simulations/stop/{personId}", null);
			return response.IsSuccessStatusCode;
		}

		public async Task<bool> StopAllSimulationsAsync()
		{
			var response = await _httpClient.PostAsync("api/simulations/stopAll", null);
			return response.IsSuccessStatusCode;
		}

		public async Task<IEnumerable<Guid>> GetRunningSimulationsAsync()
		{
			try
			{
				var response = await _httpClient.GetAsync("api/simulations/running");
				if (response.IsSuccessStatusCode)
				{
					var ids = await response.Content.ReadFromJsonAsync<IEnumerable<Guid>>();
					return ids ?? Enumerable.Empty<Guid>();
				}
			}
			catch { }
			return Enumerable.Empty<Guid>();
		}
	}
}