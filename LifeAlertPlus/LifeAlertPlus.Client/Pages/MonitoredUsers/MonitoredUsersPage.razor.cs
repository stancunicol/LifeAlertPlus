using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Text.Json;
using Microsoft.JSInterop;

namespace LifeAlertPlus.Client.Pages.MonitoredUsers
{
	public partial class MonitoredUsersPage : ComponentBase
	{
		[Inject]
		private UserMonitoredService UserMonitoredService { get; set; } = default!;

		[Inject]
		private MonitoredService MonitoredService { get; set; } = default!;

		[Inject]
		private TokenParserService TokenParser { get; set; } = default!;

		[Inject]
		private IJSRuntime JSRuntime { get; set; } = default!;

		[Inject]
		private ImportService ImportService { get; set; } = default!;

		protected List<MonitoredUserDTO> Users { get; private set; } = new();
		protected List<MonitoredPersonRow> MonitoredRows { get; private set; } = new();
		protected string UserFullName { get; private set; } = "Admin";
		protected string ProfilePictureUrl { get; private set; } = string.Empty;
		protected string SearchText { get; set; } = string.Empty;
		protected string StatusFilter { get; private set; } = "all";
		protected bool IsLoading { get; private set; } = true;
		protected string? ErrorMessage { get; private set; }
		protected string? ImportMessage { get; private set; }
		private string? _currentUserEmail;

		protected object[]? ImportPreviewData { get; set; }
		protected string[]? ImportErrors { get; set; }

		protected int TotalMonitors => MonitoredRows
			.SelectMany(r => r.Monitors)
			.Select(m => m.Email)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Count();
		protected int TotalMonitored => MonitoredRows.Count;
		protected int ActiveMonitoredCount => MonitoredRows.Count(r => r.Online);
		protected int InactiveMonitoredCount => MonitoredRows.Count(r => !r.Online);

		protected IEnumerable<MonitoredPersonRow> FilteredRows => MonitoredRows
			.Where(r => MatchesSearch(r, SearchText))
			.Where(r => MatchesFilter(r, StatusFilter))
			.OrderBy(r => r.PersonName);

		protected override async Task OnInitializedAsync()
		{
			await LoadUserFromTokenAsync();
			await LoadDataAsync();
		}

		private async Task LoadUserFromTokenAsync()
		{
			var claims = await TokenParser.GetClaimsAsync();
			if (claims == null)
				return;

			UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
			ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
			_currentUserEmail = claims.Email;
		}

		protected async Task LoadDataAsync()
		{
			IsLoading = true;
			ErrorMessage = null;

			try
			{
				var data = await UserMonitoredService.GetAllMonitoredUsersAsync();
				Users = data
					.Where(u => !IsAdminRole(u.Role))
					.ToList();
				MonitoredRows = BuildMonitoredRows(Users);
			}
			catch
			{
				ErrorMessage = "Could not load monitored users.";
			}
			finally
			{
				IsLoading = false;
			}
		}

		protected void SetFilter(string filter)
		{
			StatusFilter = filter;
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

		private List<MonitoredPersonRow> BuildMonitoredRows(IEnumerable<MonitoredUserDTO> users)
		{
			var grouped = users
				.SelectMany(u => (u.MonitoredPeople ?? Array.Empty<MonitoredPersonDTO>()).Select(p => new { User = u, Person = p }))
				.GroupBy(x => x.Person.Id);

			var rows = new List<MonitoredPersonRow>();

			foreach (var group in grouped)
			{
				var sample = group.First();
				var person = sample.Person;

				var monitors = group
					.Select(g => new MonitorInfo(FullName(g.User), g.User.Email))
					.GroupBy(m => m.Email, StringComparer.OrdinalIgnoreCase)
					.Select(g => g.First())
					.ToList();

				rows.Add(new MonitoredPersonRow(
					person.Id,
					$"{person.FirstName} {person.LastName}".Trim(),
					person.DeviceSerialNumber,
					person.IsActive && person.DeletedAt == null,
					FormatDate(person.UpdatedAt ?? person.CreatedAt),
					monitors));
			}

			return rows;
		}

		private static bool MatchesSearch(MonitoredPersonRow row, string search)
		{
			if (string.IsNullOrWhiteSpace(search))
				return true;

			var term = search.Trim();
			return row.PersonName.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| row.DeviceSerial.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| row.Monitors.Any(m => m.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || m.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
		}

		private static bool MatchesFilter(MonitoredPersonRow row, string filter)
		{
			return filter switch
			{
				"active" => row.Online,
				"inactive" => !row.Online,
				_ => true
			};
		}

		protected string GetFilterClass(string filter)
		{
			return StatusFilter == filter ? "filter-btn active" : "filter-btn";
		}

		protected string GetStatusClass(bool online)
		{
			return online ? "ok" : "offline";
		}

		protected string GetStatusText(bool online)
		{
			return online ? "Active" : "Inactive";
		}

		private static bool IsAdminRole(string? role)
		{
			if (string.IsNullOrWhiteSpace(role))
				return false;

			return role.Contains("admin", StringComparison.OrdinalIgnoreCase);
		}

		protected string GetRowClass(bool online)
		{
			return online ? string.Empty : "row-offline";
		}

		protected string FullName(MonitoredUserDTO user)
		{
			return $"{user.FirstName} {user.LastName}".Trim();
		}

		protected string FormatDate(DateTime? value)
		{
			return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";
		}

		protected sealed record MonitorInfo(string Name, string Email);

		protected sealed record MonitoredPersonRow(
			Guid PersonId,
			string PersonName,
			string DeviceSerial,
			bool Online,
			string LastUpdate,
			IReadOnlyList<MonitorInfo> Monitors);

			private bool ShowImportModal { get; set; } = false;

        protected async Task HandleImportAsync(InputFileChangeEventArgs e)
        {
            ImportMessage = null;
            ImportPreviewData = null;
            ImportErrors = null;
            ShowImportModal = false;
            try
            {
                await using var stream = e.File.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                var result = await ImportService.ImportESPDataAsync(json);

                ImportPreviewData = result.Data;
                ImportErrors = result.Errors;
                ShowImportModal = true;

                if (result.Success)
                {
                    ImportMessage = "Import reușit!";
                }
                else
                {
                    ImportMessage = "Import eșuat: " + (result.Errors != null ? string.Join("; ", result.Errors) : result.Message);
                }
            }
            catch (Exception ex)
            {
                ImportMessage = $"Import failed: {ex.Message}";
                ShowImportModal = true;
            }
        }

        private async Task ConfirmImportAsync()
        {
            if (ImportPreviewData == null || ImportPreviewData.Length == 0)
            {
                ImportMessage = "Nu există date de importat!";
                return;
            }
            var result = await ImportService.ConfirmESPDataAsync(ImportPreviewData);
            if (result.Success)
            {
                ImportMessage = "Datele au fost adăugate în baza de date!";
                ShowImportModal = false;
                await LoadDataAsync();
            }
            else
            {
                ImportMessage = "Eroare la salvare: " + (result.Errors != null ? string.Join("; ", result.Errors) : result.Message);
            }
        }

        private void CloseImportModal()
        {
            ShowImportModal = false;
        }
	}
}
