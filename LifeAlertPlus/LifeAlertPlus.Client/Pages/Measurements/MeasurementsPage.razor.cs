using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Client.Pages.Measurements
{
	public partial class MeasurementsPage
	{
		[Parameter]
		public Guid PersonId { get; set; }

		[Inject] private MeasurementApiClient MeasurementApiClient { get; set; } = default!;
		[Inject] private MonitoredApiClient MonitoredApiClient { get; set; } = default!;
		[Inject] private TokenParserService TokenParser { get; set; } = default!;
		[Inject] private NavigationManager NavigationManager { get; set; } = default!;
		[Inject] private LanguageService Lang { get; set; } = default!;

		private string T(string key) => Lang.T(key);

		protected string UserFullName { get; set; } = "";
		protected string ProfilePictureUrl { get; set; } = "";
		protected string PersonName { get; set; } = "";
		protected bool IsLoading { get; set; } = true;
		protected string? ErrorMessage { get; set; }

		private List<MeasurementResponseDTO> AllMeasurements { get; } = new();
		protected int CurrentPage { get; set; } = 1;
		private const int PageSize = 25;

		// Vital ranges from monitored person
		private int _minHr = 60;
		private int _maxHr = 100;
		private double _minTemp = 36.0;
		private double _maxTemp = 37.5;

		// Filters
		protected string StatusFilter { get; set; } = "all";
		protected string TypeFilter { get; set; } = "all";

		private IEnumerable<MeasurementResponseDTO> ApplyFilters() => AllMeasurements.Where(m =>
		{
			var status = GetStatus(m);
			if (StatusFilter != "all" && status != StatusFilter) return false;

			if (TypeFilter == "hr" && !IsHrAbnormal(m)) return false;
			if (TypeFilter == "spo2" && !IsSpo2Abnormal(m)) return false;
			if (TypeFilter == "temp" && !IsTempAbnormal(m)) return false;
			if (TypeFilter == "fall" && !m.IsFall) return false;

			return true;
		});

		protected IEnumerable<MeasurementResponseDTO> FilteredMeasurements =>
			ApplyFilters().Skip((CurrentPage - 1) * PageSize).Take(PageSize);

		protected int FilteredCount => ApplyFilters().Count();
		protected int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)PageSize));

		// Stats reflect ALL measurements
		protected int TotalLoaded => AllMeasurements.Count;
		protected int NormalCount => AllMeasurements.Count(m => GetStatus(m) == "normal");
		protected int AlertCount => AllMeasurements.Count(m => GetStatus(m) == "alert");
		protected int CriticalCount => AllMeasurements.Count(m => GetStatus(m) == "critical");

		protected bool HasPrevious => CurrentPage > 1;
		protected bool HasNext => CurrentPage < TotalPages;

		protected List<int> GetPageNumbers()
		{
			var pages = new List<int>();
			var total = TotalPages;
			if (total <= 7)
			{
				for (int i = 1; i <= total; i++) pages.Add(i);
			}
			else
			{
				// Always show first 3
				pages.Add(1);
				pages.Add(2);
				pages.Add(3);

				if (CurrentPage > 4 && CurrentPage < total - 2)
				{
					pages.Add(-1); // ellipsis
					pages.Add(CurrentPage - 1);
					pages.Add(CurrentPage);
					pages.Add(CurrentPage + 1);
					pages.Add(-1); // ellipsis
				}
				else if (CurrentPage <= 4)
				{
					pages.Add(4);
					if (CurrentPage == 4 || total > 5) pages.Add(-1);
				}
				else
				{
					pages.Add(-1); // ellipsis
					pages.Add(total - 2);
					pages.Add(total - 1);
				}

				if (!pages.Contains(total))
					pages.Add(total);
			}

			return pages.Distinct().ToList();
		}

		protected void SetStatusFilter(string value)
		{
			StatusFilter = value;
			CurrentPage = 1;
		}

		protected void SetTypeFilter(string value)
		{
			TypeFilter = value;
			CurrentPage = 1;
		}

		protected override async Task OnInitializedAsync()
		{
			var claims = await TokenParser.GetClaimsAsync();
			UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
			ProfilePictureUrl = claims.ProfilePictureUrl;

			await LoadPersonAsync();
			await LoadAllAsync();
		}

		private async Task LoadPersonAsync()
		{
			try
			{
				var monitored = await MonitoredApiClient.GetMonitoredPersonByIdAsync(PersonId);
				if (monitored != null)
				{
					PersonName = $"{monitored.FirstName} {monitored.LastName}".Trim();
					_minHr = monitored.MinHeartRate ?? 60;
					_maxHr = monitored.MaxHeartRate ?? 100;
					_minTemp = monitored.MinTemperature ?? 36.0;
					_maxTemp = monitored.MaxTemperature ?? 37.5;
				}
			}
			catch { }
		}

		protected async Task LoadAllAsync()
		{
			IsLoading = true;
			ErrorMessage = null;
			AllMeasurements.Clear();
			CurrentPage = 1;

			try
			{
				int page = 1;
				bool hasMore = true;
				while (hasMore)
				{
					var batch = await MeasurementApiClient.GetMeasurementsByMonitoredIdAsync(PersonId, page, 200);
					var list = batch.ToList();
					AllMeasurements.AddRange(list);
					hasMore = list.Count >= 200;
					page++;
				}
			}
			catch
			{
				ErrorMessage = T("meas.error");
			}
			finally
			{
				IsLoading = false;
			}
		}

		protected void GoToPage(int page)
		{
			if (page < 1 || page > TotalPages || page == CurrentPage) return;
			CurrentPage = page;
		}

		protected string GetStatus(MeasurementResponseDTO m)
		{
			// Critical: fall, or extreme values
			if (m.IsFall) return "critical";
			if (m.Pulse > _maxHr + 30 || m.Pulse < _minHr - 20) return "critical";
			if (m.Temperature > _maxTemp + 1.0 || m.Temperature < _minTemp - 1.0) return "critical";
			if (m.SpO2 > 0 && m.SpO2 < 90) return "critical";

			// Alert: out of range
			if (m.Pulse > _maxHr || m.Pulse < _minHr) return "alert";
			if (m.Temperature > _maxTemp || m.Temperature < _minTemp) return "alert";
			if (m.SpO2 > 0 && m.SpO2 < 95) return "alert";

			return "normal";
		}

		protected string GetStatusLabel(string status) => status switch
		{
			"normal" => T("meas.normal"),
			"alert" => T("meas.alert"),
			"critical" => T("meas.critical"),
			_ => "-"
		};

		protected string GetCellClass(MeasurementResponseDTO m, string type)
		{
			return type switch
			{
				"hr" => m.Pulse > _maxHr + 30 || m.Pulse < _minHr - 20 ? "cell-critical"
					   : m.Pulse > _maxHr || m.Pulse < _minHr ? "cell-alert" : "cell-normal",
				"temp" => m.Temperature > _maxTemp + 1.0 || m.Temperature < _minTemp - 1.0 ? "cell-critical"
						: m.Temperature > _maxTemp || m.Temperature < _minTemp ? "cell-alert" : "cell-normal",
				"spo2" => m.SpO2 > 0 && m.SpO2 < 90 ? "cell-critical"
						: m.SpO2 > 0 && m.SpO2 < 95 ? "cell-alert" : "cell-normal",
				_ => ""
			};
		}

		private bool IsHrAbnormal(MeasurementResponseDTO m) => m.Pulse > _maxHr || m.Pulse < _minHr;
		private bool IsSpo2Abnormal(MeasurementResponseDTO m) => m.SpO2 > 0 && m.SpO2 < 95;
		private bool IsTempAbnormal(MeasurementResponseDTO m) => m.Temperature > _maxTemp || m.Temperature < _minTemp;

		private void GoBack() => NavigationManager.NavigateTo($"/monitored/{PersonId}");
	}
}
