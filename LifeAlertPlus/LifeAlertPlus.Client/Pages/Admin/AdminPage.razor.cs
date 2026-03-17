using System;
using System.Collections.Generic;
using System.Linq;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Domain.Entities;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Admin;

public partial class AdminPage : ComponentBase
{
	[Inject]
	private TokenParserService TokenParser { get; set; } = default!;

	protected string UserFullName { get; set; } = "Admin";
	protected string ProfilePictureUrl { get; set; } = string.Empty;

	protected IReadOnlyList<AdminUserView> AdminUsers { get; private set; } = Array.Empty<AdminUserView>();

	protected int TotalUsers => AdminUsers.Count;
	protected int TotalMonitored => AdminUsers.Sum(u => u.MonitoredPeople.Count);
	protected int ActiveMonitored => AdminUsers.Sum(u => u.MonitoredPeople.Count(m => m.IsActive && m.DeletedAt == null));
	protected int InactiveMonitored => AdminUsers.Sum(u => u.MonitoredPeople.Count(m => !m.IsActive || m.DeletedAt != null));
    protected int ActiveUsers => AdminUsers.Count(u => u.User.DeletedAt == null);

	protected IEnumerable<MonitoredRow> MonitoredRows => AdminUsers
		.SelectMany(u => u.MonitoredPeople.Select(m => new MonitoredRow(
			FullName(m),
			GetAge(m),
			FullName(u.User),
			u.User.Email,
			m.DeviceSerialNumber,
			FormatDate(m.UpdatedAt ?? m.CreatedAt),
			m.IsActive && m.DeletedAt == null)));

	protected override async Task OnInitializedAsync()
	{
		await LoadUserFromTokenAsync();
		SeedDemoData();
	}

	private async Task LoadUserFromTokenAsync()
	{
		var claims = await TokenParser.GetClaimsAsync();
		if (claims == null)
		{
			UserFullName = "Admin";
			ProfilePictureUrl = string.Empty;
			return;
		}

		UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
		ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
	}

	private void SeedDemoData()
	{
		AdminUsers = new List<AdminUserView>
		{
			new(
				new User
				{
					Id = Guid.NewGuid(),
					FirstName = "Ana",
					LastName = "Munteanu",
					Email = "ana.munteanu@lifeplus.com",
					IsEmailConfirmed = true,
					CreatedAt = DateTime.UtcNow.AddDays(-2)
				},
				new List<Domain.Entities.Monitored>
				{
					new Domain.Entities.Monitored
					{
						Id = Guid.NewGuid(),
						FirstName = "Ioan",
						LastName = "Radu",
						Birthdate = new DateTime(1952, 3, 12),
						DeviceSerialNumber = "ESP-0001",
						IsActive = true,
						UpdatedAt = DateTime.UtcNow.AddMinutes(-2),
						CreatedAt = DateTime.UtcNow.AddMonths(-3)
					},
					new Domain.Entities.Monitored
					{
						Id = Guid.NewGuid(),
						FirstName = "Carmen",
						LastName = "Rusu",
						Birthdate = new DateTime(1956, 9, 4),
						DeviceSerialNumber = "ESP-0008",
						IsActive = true,
						UpdatedAt = DateTime.UtcNow.AddMinutes(-18),
						CreatedAt = DateTime.UtcNow.AddMonths(-1)
					}
				}),
			new(
				new User
				{
					Id = Guid.NewGuid(),
					FirstName = "Vlad",
					LastName = "Petrescu",
					Email = "vlad.petrescu@lifeplus.com",
					IsEmailConfirmed = true,
					CreatedAt = DateTime.UtcNow.AddDays(-5)
				},
				new List<Domain.Entities.Monitored>
				{
					new Domain.Entities.Monitored
					{
						Id = Guid.NewGuid(),
						FirstName = "Mihai",
						LastName = "Ene",
						Birthdate = new DateTime(1962, 11, 2),
						DeviceSerialNumber = "ESP-0042",
						IsActive = true,
						UpdatedAt = DateTime.UtcNow.AddMinutes(-7),
						CreatedAt = DateTime.UtcNow.AddMonths(-2)
					},
					new Domain.Entities.Monitored
					{
						Id = Guid.NewGuid(),
						FirstName = "Sorin",
						LastName = "Luca",
						Birthdate = new DateTime(1954, 2, 20),
						DeviceSerialNumber = "ESP-0033",
						IsActive = false,
						UpdatedAt = DateTime.UtcNow.AddMinutes(-45),
						CreatedAt = DateTime.UtcNow.AddMonths(-4)
					}
				}),
			new(
				new User
				{
					Id = Guid.NewGuid(),
					FirstName = "Eliza",
					LastName = "Pop",
					Email = "eliza.pop@lifeplus.com",
					IsEmailConfirmed = true,
					CreatedAt = DateTime.UtcNow.AddDays(-1)
				},
				new List<Domain.Entities.Monitored>
				{
					new Domain.Entities.Monitored
					{
						Id = Guid.NewGuid(),
						FirstName = "Teodor",
						LastName = "Ilies",
						Birthdate = new DateTime(1948, 1, 9),
						DeviceSerialNumber = "ESP-0021",
						IsActive = true,
						UpdatedAt = DateTime.UtcNow.AddMinutes(-3),
						CreatedAt = DateTime.UtcNow.AddMonths(-5)
					}
				}),
			new(
				new User
				{
					Id = Guid.NewGuid(),
					FirstName = "Rares",
					LastName = "Stan",
					Email = "rares.stan@lifeplus.com",
					IsEmailConfirmed = false,
					CreatedAt = DateTime.UtcNow.AddDays(-9),
					DeletedAt = DateTime.UtcNow.AddDays(-1)
				},
				new List<Domain.Entities.Monitored>
				{
					new Domain.Entities.Monitored
					{
						Id = Guid.NewGuid(),
						FirstName = "Angela",
						LastName = "Voicu",
						Birthdate = new DateTime(1958, 6, 15),
						DeviceSerialNumber = "ESP-0101",
						IsActive = true,
						UpdatedAt = DateTime.UtcNow.AddMinutes(-26),
						CreatedAt = DateTime.UtcNow.AddMonths(-6)
					}
				})
		};
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

	protected string FullName(User user) => $"{user.FirstName} {user.LastName}".Trim();

	protected string FullName(Domain.Entities.Monitored monitored) => $"{monitored.FirstName} {monitored.LastName}".Trim();

	protected bool IsUserActive(User user) => user.DeletedAt == null;

	protected string UserRoleLabel(User user) => user.IsEmailConfirmed ? "Confirmed" : "Unconfirmed";

	protected string FormatDate(DateTime dateTime) => dateTime.ToLocalTime().ToString("g");

	protected int GetAge(Domain.Entities.Monitored person)
	{
		if (person.Birthdate == null)
		{
			return 0;
		}

		var today = DateTime.Today;
		var age = today.Year - person.Birthdate.Value.Year;
		if (person.Birthdate.Value.Date > today.AddYears(-age))
		{
			age--;
		}

		return age;
	}

	protected sealed record AdminUserView(User User, List<Domain.Entities.Monitored> MonitoredPeople);

	protected sealed record MonitoredRow(
		string PersonName,
		int Age,
		string UserName,
		string UserEmail,
		string DeviceSerial,
		string LastUpdate,
		bool Online);
}
