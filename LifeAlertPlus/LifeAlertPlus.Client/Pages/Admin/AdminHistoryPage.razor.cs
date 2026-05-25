using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Admin;

public partial class AdminHistoryPage : ComponentBase
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
	protected string ActionFilter { get; set; } = "all";
	protected List<AuditEntry> Entries { get; set; } = new();

	protected int TotalEvents => Entries.Count;
	protected int AccountEvents => Entries.Count(e => e.Category == "Account");
	protected int SystemEvents => Entries.Count(e => e.Category == "System");
	protected int DeleteActions => Entries.Count(e => e.Action.Contains("Delete", StringComparison.OrdinalIgnoreCase));

	protected IEnumerable<AuditEntry> FilteredEntries => Entries
		.Where(e => string.IsNullOrWhiteSpace(SearchText) || e.User.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || e.Action.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || e.Details.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
		.Where(e => ActionFilter == "all" || e.Category == ActionFilter)
		.OrderByDescending(e => e.Timestamp);

	protected override async Task OnInitializedAsync()
	{
		await LoadUserFromTokenAsync();
		await LoadEntriesAsync();
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

	protected Task LoadEntriesAsync()
	{
		IsLoading = true;
		ErrorMessage = null;

		try
		{
			Entries = new List<AuditEntry>
			{
				new(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5), "admin@lifealert.plus", "Deleted user alice@example.com", "Removed inactive account and revoked access.", "Account"),
				new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), "admin@lifealert.plus", "Updated user roles for bob@example.com", "Assigned role 'caretaker'.", "Account"),
				new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-4), "system", "Applied database migration", "Added audit log tables.", "System"),
				new(Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), "admin@lifealert.plus", "Changed password policy", "Increased minimum password length to 8.", "System"),
				new(Guid.NewGuid(), DateTime.UtcNow.AddDays(-2), "admin@lifealert.plus", "Deleted user charlie@example.com", "Permanent account deletion due to support request.", "Account"),
			};
		}
		catch (Exception ex)
		{
			ErrorMessage = $"Failed to load history: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}

		return Task.CompletedTask;
	}

	protected void SetFilter(string filter)
	{
		ActionFilter = filter;
	}

	protected string GetFilterClass(string filter)
	{
		return ActionFilter == filter ? "filter-btn active" : "filter-btn";
	}

	protected string FormatDate(DateTime timestamp)
	{
		return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
	}

	protected record AuditEntry(Guid Id, DateTime Timestamp, string User, string Action, string Details, string Category);
}
