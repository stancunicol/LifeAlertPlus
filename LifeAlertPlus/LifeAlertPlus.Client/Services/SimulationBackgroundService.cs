using System.Collections.Concurrent;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;

namespace LifeAlertPlus.Client.Services
{
	/// <summary>
	/// Runs simulation generation in background loops per person until explicitly stopped.
	/// </summary>
	public class SimulationBackgroundService
	{
		private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runs = new();

		public Task StartAsync(
			Guid personId,
			Func<ESPDataResponseDTO> payloadFactory,
			Func<ESPDataResponseDTO, Task<bool>> sendAsync,
			Action<bool>? onResult,
			TimeSpan? interval = null)
		{
			var delay = interval ?? TimeSpan.FromMinutes(1);
			var cts = new CancellationTokenSource();

			if (!_runs.TryAdd(personId, cts))
			{
				if (_runs.TryGetValue(personId, out var existing))
				{
					existing.Cancel();
					existing.Dispose();
				}
				_runs[personId] = cts;
			}

			return Task.Run(async () =>
			{
				while (!cts.IsCancellationRequested)
				{
					try
					{
						var payload = payloadFactory();
						var ok = await sendAsync(payload);
						onResult?.Invoke(ok);
					}
					catch
					{
						onResult?.Invoke(false);
					}

					try
					{
						await Task.Delay(delay, cts.Token);
					}
					catch (OperationCanceledException)
					{
						break;
					}
				}
			}, CancellationToken.None);
		}

		public Task StopAsync(Guid personId)
		{
			if (_runs.TryRemove(personId, out var cts))
			{
				cts.Cancel();
				cts.Dispose();
			}

			return Task.CompletedTask;
		}

		public Task StopAllAsync()
		{
			foreach (var kvp in _runs)
			{
				kvp.Value.Cancel();
				kvp.Value.Dispose();
			}

			_runs.Clear();
			return Task.CompletedTask;
		}
	}
}