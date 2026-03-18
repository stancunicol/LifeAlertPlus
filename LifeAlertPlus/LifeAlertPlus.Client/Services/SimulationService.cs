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
	}
}