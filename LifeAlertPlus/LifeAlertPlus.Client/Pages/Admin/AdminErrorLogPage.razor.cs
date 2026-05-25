using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Admin;

public partial class AdminErrorLogPage : ComponentBase
{
	[Inject]
	private TokenParserService TokenParser { get; set; } = default!;

	[Inject]
	private LanguageService Lang { get; set; } = default!;

	[Inject]
	private NavigationManager Navigation { get; set; } = default!;

	private string T(string key) => Lang.T(key);

	protected string UserFullName { get; set; } = "Admin";
	protected string ProfilePictureUrl { get; set; } = string.Empty;
	protected bool IsLoading { get; set; } = true;
	protected string? ErrorMessage { get; set; }
	protected string SearchText { get; set; } = string.Empty;
	protected string LevelFilter { get; set; } = "all";
	protected List<ErrorLogEntry> Entries { get; set; } = new();

	protected int ErrorCount => Entries.Count(e => e.Level == "Error");
	protected int WarningCount => Entries.Count(e => e.Level == "Warning");
	protected int InfoCount => Entries.Count(e => e.Level == "Info");
	protected int Recent24hCount => Entries.Count(e => e.Timestamp >= DateTime.UtcNow.AddHours(-24));

	protected IEnumerable<ErrorLogEntry> FilteredEntries => Entries
		.Where(e => string.IsNullOrWhiteSpace(SearchText) || e.Source.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || e.Details.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
		.Where(e => LevelFilter == "all" || e.Level == LevelFilter)
		.OrderByDescending(e => e.Timestamp);

	protected override async Task OnInitializedAsync()
	{
		await LoadUserFromTokenAsync();
		await LoadErrorsAsync();
}

	private async Task LoadUserFromTokenAsync()
	{
		var claims = await TokenParser.GetClaimsAsync();
		if (claims == null)
		{
			Navigation.NavigateTo("/login", forceLoad: true);
			return;
		}

		UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
		ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
	}

	protected Task LoadErrorsAsync()
	{
		IsLoading = true;
		ErrorMessage = null;

		try
		{
			Entries = new List<ErrorLogEntry>
			{
				new(DateTime.UtcNow.AddMinutes(-18), "Error", "UserController", "Failed to delete user account.", "Database constraint violation while deleting user id 7d1f..."),
				new(DateTime.UtcNow.AddHours(-2), "Warning", "AuthenticationController", "Multiple failed login attempts.", "IP 192.168.1.100 exceeded 5 invalid logins."),
				new(DateTime.UtcNow.AddHours(-4), "Info", "SimulationManager", "Simulation batch completed.", "Generated 120 measurements for 8 devices."),
				new(DateTime.UtcNow.AddDays(-1), "Error", "ESPController", "Device heartbeat missing.", "No heartbeat received from device F3A2-01 for 18 minutes."),
				new(DateTime.UtcNow.AddDays(-1).AddHours(-3), "Warning", "EmailController", "SMTP relay slow.", "Delivery was delayed by 12 seconds for user jane@example.com."),
			};
		}
		catch (Exception ex)
		{
			ErrorMessage = $"Failed to load error log: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}

		return Task.CompletedTask;
	}

	protected void SetFilter(string filter)
	{
		LevelFilter = filter;
	}

	protected string GetFilterClass(string filter)
	{
		return LevelFilter == filter ? "filter-btn active" : "filter-btn";
	}

	protected string FormatDate(DateTime timestamp)
	{
		return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
	}

	protected record ErrorLogEntry(DateTime Timestamp, string Level, string Source, string Message, string Details);
}
