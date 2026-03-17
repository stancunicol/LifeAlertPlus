using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Responses.User;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Users
{
	public partial class UsersPage : ComponentBase
	{
		[Inject]
		private UserService UserService { get; set; } = default!;

		[Inject]
		private TokenParserService TokenParser { get; set; } = default!;

		protected List<UserListItemDTO> Users { get; private set; } = new();
		protected string UserFullName { get; private set; } = "Admin";
		protected string ProfilePictureUrl { get; private set; } = string.Empty;
		protected string SearchText { get; set; } = string.Empty;
		protected string StatusFilter { get; private set; } = "all";
		protected bool IsLoading { get; private set; } = true;
		protected string? ErrorMessage { get; private set; }

		protected int TotalUsers => Users.Count;
		protected int ActiveUsers => Users.Count(u => u.DeletedAt == null);
		protected int ConfirmedUsers => Users.Count(u => u.IsEmailConfirmed);

		protected IEnumerable<UserListItemDTO> FilteredUsers => Users
			.Where(u => !IsAdminRole(u.Role))
			.Where(u => MatchesSearch(u, SearchText))
			.Where(u => MatchesFilter(u, StatusFilter))
			.OrderBy(u => u.LastName)
			.ThenBy(u => u.FirstName);

		protected override async Task OnInitializedAsync()
		{
			await LoadUserFromTokenAsync();
			await LoadUsersAsync();
		}

		private async Task LoadUserFromTokenAsync()
		{
			var claims = await TokenParser.GetClaimsAsync();
			if (claims == null)
				return;

			UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
			ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
		}

		protected async Task LoadUsersAsync()
		{
			IsLoading = true;
			ErrorMessage = null;

			try
			{
				var users = await UserService.GetAllUsersAsync();
				Users = users
					.Where(u => !IsAdminRole(u.Role))
					.ToList();
			}
			catch
			{
				ErrorMessage = "Could not load users.";
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

		private static bool MatchesSearch(UserListItemDTO user, string search)
		{
			if (string.IsNullOrWhiteSpace(search))
				return true;

			var term = search.Trim();
			return user.FirstName.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| user.LastName.Contains(term, StringComparison.OrdinalIgnoreCase)
				|| user.Email.Contains(term, StringComparison.OrdinalIgnoreCase);
		}

		private static bool MatchesFilter(UserListItemDTO user, string filter)
		{
			return filter switch
			{
				"active" => user.DeletedAt == null,
				"inactive" => user.DeletedAt != null,
				_ => true
			};
		}

		private static bool IsAdminRole(string? role)
		{
			return !string.IsNullOrWhiteSpace(role)
				&& role.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		protected string GetInitials(UserListItemDTO user)
		{
			var composed = $"{user.FirstName} {user.LastName}".Trim();
			if (string.IsNullOrWhiteSpace(composed))
				return "US";

			var parts = composed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length >= 2)
				return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();

			return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
		}

		protected string GetFilterClass(string filter)
		{
			return StatusFilter == filter ? "filter-btn active" : "filter-btn";
		}

		protected string GetStatusClass(UserListItemDTO user)
		{
			return user.DeletedAt == null ? "status-pill ok" : "status-pill off";
		}

		protected string GetStatusLabel(UserListItemDTO user)
		{
			return user.DeletedAt == null ? "Active" : "Inactive";
		}

		protected string GetStatusIndicatorClass(UserListItemDTO user)
		{
			return user.DeletedAt == null ? "ok" : "offline";
		}

		protected string GetRowClass(UserListItemDTO user)
		{
			return user.DeletedAt == null ? string.Empty : "row-offline";
		}

		protected string GetEmailLabel(UserListItemDTO user)
		{
			return user.IsEmailConfirmed ? "Email confirmed" : "Email not confirmed";
		}

		protected string GetProviderLabel(UserListItemDTO user)
		{
			return string.IsNullOrWhiteSpace(user.Provider) ? "Local" : user.Provider;
		}

		protected string FormatDate(DateTime? value)
		{
			return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";
		}

		protected string FormatDate(DateTime value)
		{
			return FormatDate((DateTime?)value);
		}
	}
}
