using System.Collections.Concurrent;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;

namespace LifeAlertPlus.Client.Services
{
	/// <summary>
	/// Runs simulation generation in background loops per person until explicitly stopped.
	/// </summary>
	public class SimulationBackgroundService
	{
		private readonly ConcurrentDictionary<Guid, (CancellationTokenSource Cts, Task RunningTask)> _runs = new();

		public Task StartAsync(
			Guid personId,
			Func<ESPDataResponseDTO> payloadFactory,
			Func<ESPDataResponseDTO, Task<bool>> sendAsync,
			Action<bool>? onResult,
			TimeSpan? interval = null)
		{
			var delay = interval ?? TimeSpan.FromSeconds(30);
			var cts = new CancellationTokenSource();

			var runningTask = Task.Run(async () =>
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

			if (!_runs.TryAdd(personId, (cts, runningTask)))
			{
				if (_runs.TryGetValue(personId, out var existing))
				{
					try
					{
						existing.Cts.Cancel();
					}
					catch { }
				}
				_runs[personId] = (cts, runningTask);
			}

			return runningTask;
		}

		public async Task StopAsync(Guid personId)
		{
			if (_runs.TryRemove(personId, out var entry))
			{
				try
				{
					entry.Cts.Cancel();
				}
				catch { }

				try
				{
					await Task.WhenAny(entry.RunningTask, Task.Delay(5000));
				}
				catch { }

				try
				{
					entry.Cts.Dispose();
				}
				catch { }
			}
		}

		public async Task StopAllAsync()
		{
			var entries = _runs.ToArray();
			foreach (var kvp in entries)
			{
				try { kvp.Value.Cts.Cancel(); } catch { }
			}

			var tasks = entries.Select(e => e.Value.RunningTask).ToArray();
			try
			{
				await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(5000));
			}
			catch { }

			foreach (var kvp in entries)
			{
				try { kvp.Value.Cts.Dispose(); } catch { }
				_runs.TryRemove(kvp.Key, out _);
			}
		}
	}
}