using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.UserMonitored;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Admin;

public partial class AdminPage : ComponentBase
{
	[Inject]
	private TokenParserService TokenParser { get; set; } = default!;

	[Inject]
	private UserMonitoredService UserMonitoredService { get; set; } = default!;

	[Inject]
	private NavigationManager Navigation { get; set; } = default!;

	protected string UserFullName { get; set; } = "Admin";
	protected string ProfilePictureUrl { get; set; } = string.Empty;

	protected IReadOnlyList<MonitoredUserDTO> AdminUsers { get; private set; } = Array.Empty<MonitoredUserDTO>();
	protected bool IsLoading { get; set; } = true;
	protected string? ErrorMessage { get; set; }

	protected int TotalUsers => AdminUsers.Count;
	protected int TotalMonitored => AdminUsers.Sum(u => u.MonitoredPeople.Count);
	protected int ActiveMonitored => AdminUsers.Sum(u => u.MonitoredPeople.Count(m => m.IsActive && m.DeletedAt == null));
	protected int InactiveMonitored => AdminUsers.Sum(u => u.MonitoredPeople.Count(m => !m.IsActive || m.DeletedAt != null));
    protected int ActiveUsers => AdminUsers.Count(u => u.IsActive);

	protected IEnumerable<MonitoredRow> MonitoredRows => AdminUsers
		.SelectMany(u => u.MonitoredPeople.Select(m => new MonitoredRow(
			$"{m.FirstName} {m.LastName}".Trim(),
			0, // Age not available in DTO
			$"{u.FirstName} {u.LastName}".Trim(),
			u.Email,
			m.DeviceSerialNumber,
			FormatDate(m.UpdatedAt ?? m.CreatedAt),
			m.IsActive && m.DeletedAt == null)));

	protected override async Task OnInitializedAsync()
	{
		await LoadUserFromTokenAsync();
		await LoadDataAsync();
	}

	private async Task LoadUserFromTokenAsync()
	{
		var claims = await TokenParser.GetClaimsAsync();
		if (claims == null)
		{
			Navigation.NavigateTo("/login");
			return;
		}

		UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
		ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
	}

	private async Task LoadDataAsync()
	{
		IsLoading = true;
		ErrorMessage = null;

		try
		{
			var monitoredUsers = await UserMonitoredService.GetAllMonitoredUsersAsync();
			AdminUsers = monitoredUsers.ToList();
		}
		catch (Exception ex)
		{
			ErrorMessage = $"Failed to load data: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}

	protected string GetStatusClass(bool online)
	{
		return online ? "ok" : "offline";
	}

	protected string GetStatusText(bool online)
	{
		return online ? "Active" : "Inactive";
	}

	protected string GetRowClass(bool online)
	{
		return online ? string.Empty : "row-offline";
	}

	protected string GetInitials(string name)
	{
		var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length >= 2)
		{
			return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
		}

		return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
	}

	protected string FullName(MonitoredUserDTO user) => $"{user.FirstName} {user.LastName}".Trim();

	protected string FullName(MonitoredPersonDTO monitored) => $"{monitored.FirstName} {monitored.LastName}".Trim();

	protected bool IsUserActive(MonitoredUserDTO user) => user.IsActive;

	protected string UserRoleLabel(MonitoredUserDTO user) => user.Role;

	protected string FormatDate(DateTime dateTime) => dateTime.ToLocalTime().ToString("g");

	protected int GetAge(MonitoredPersonDTO person)
	{
		// Age not available in DTO, return 0 or calculate if needed
		return 0;
	}

	protected sealed record MonitoredRow(
		string PersonName,
		int Age,
		string UserName,
		string UserEmail,
		string DeviceSerial,
		string LastUpdate,
		bool Online);
}
