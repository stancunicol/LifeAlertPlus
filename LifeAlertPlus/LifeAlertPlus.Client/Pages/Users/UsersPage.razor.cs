using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using Microsoft.JSInterop;
using System.Threading;
using LifeAlertPlus.Shared.DTOs.Responses.User;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Users
{
	// Code-behind pentru pagina Admin de gestionare a utilizatorilor — listare, căutare/filtrare, activare/dezactivare și ștergere logică (soft delete)
	public partial class UsersPage : ComponentBase
	{
		[Inject]
		private UserApiClient UserApiClient { get; set; } = default!;

		[Inject]
		private NavigationManager NavigationManager { get; set; } = default!;

		[Inject]
		private TokenParserService TokenParser { get; set; } = default!;

		[Inject]
		private UserMonitoredApiClient UserMonitoredApiClient { get; set; } = default!;

		[Inject]
		private IJSRuntime JSRuntime { get; set; } = default!;

		[Inject]
		private LanguageService Lang { get; set; } = default!;

		private string T(string key) => Lang.TEnglish(key);

		protected List<UserListItemDTO> Users { get; private set; } = new();
		protected string UserFullName { get; private set; } = "Admin";
		protected string ProfilePictureUrl { get; private set; } = string.Empty;
		protected string SearchText { get; set; } = string.Empty;
		protected string StatusFilter { get; private set; } = "all";
		protected bool IsLoading { get; private set; } = true;
		protected string? ErrorMessage { get; private set; }

		private UserListItemDTO? _userToDelete;
		private bool _showUserDeleteConfirm;
		private bool _isProcessingUserDelete;

		protected int TotalUsers => Users.Count;
		protected int ActiveUsers => Users.Count(u => u.DeletedAt == null);
		protected int ConfirmedUsers => Users.Count(u => u.IsEmailConfirmed);

		protected Dictionary<Guid, int> MonitoredCounts { get; private set; } = new();

		// Lista afișată: exclude adminii, aplică căutarea text și filtrul de stare, sortată alfabetic după nume
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

		// Extrage din token-ul JWT identitatea administratorului curent, pentru afișare în antetul paginii
		private async Task LoadUserFromTokenAsync()
		{
			var claims = await TokenParser.GetClaimsAsync();
			if (claims == null)
				return;

			UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
			ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;
		}

		// Încarcă toți utilizatorii din API (excluzând adminii) și numărul de persoane monitorizate pentru fiecare
		protected async Task LoadUsersAsync()
		{
			IsLoading = true;
			ErrorMessage = null;

			try
			{
				var users = await UserApiClient.GetAllUsersAsync();
				Users = users
					.Where(u => !IsAdminRole(u.Role))
					.ToList();

				// Load monitored counts for each user (limited concurrency)
				await RefreshMonitoredCountsAsync();
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

		protected async Task RefreshMonitoredCountsAsync()
		{
			// Use admin-level aggregated endpoint to get monitored counts for all users in one call
			try
			{
				// Construiește un dicționar UserId -> numărul de persoane monitorizate, pentru afișare rapidă în tabel
				var all = await UserMonitoredApiClient.GetAllMonitoredUsersAsync();
				var dict = new Dictionary<Guid, int>();
				foreach (var mu in all)
				{
					dict[mu.UserId] = mu.MonitoredPeople?.Count ?? 0;
				}
				MonitoredCounts = dict;
			}
			catch
			{
				MonitoredCounts = new Dictionary<Guid, int>();
			}
		}

		// Comută starea contului — dezactivează dacă e activ, reactivează dacă e deja dezactivat
		protected async Task ToggleActive(UserListItemDTO user)
		{
			if (user.DeletedAt == null)
				await DeactivateUser(user);
			else
				await ActivateUser(user);
		}

		// Dezactivează contul (soft-delete) după confirmare în browser — contul rămâne în baza de date, dar nu se mai poate autentifica
		protected async Task DeactivateUser(UserListItemDTO user)
		{
			var ok = await JSRuntime.InvokeAsync<bool>("confirm", $"Deactivate user {user.Email}? This will disable the account.");
			if (!ok) return;

			var success = await UserApiClient.DeactivateUserAsync(user.Id);
			if (success)
			{
				user.DeletedAt = DateTime.UtcNow;
			}
			else
			{
				ErrorMessage = "Failed to deactivate user.";
			}
		}

		// Reactivează un cont dezactivat anterior, ștergând marca DeletedAt
		protected async Task ActivateUser(UserListItemDTO user)
		{
			var success = await UserApiClient.ActivateUserAsync(user.Id);
			if (success)
			{
				user.DeletedAt = null;
			}
			else
			{
				ErrorMessage = "Failed to activate user.";
			}
		}

		// Deschide modalul de confirmare a ștergerii, memorând utilizatorul țintă
		protected void ConfirmDelete(UserListItemDTO user)
		{
			_userToDelete = user;
			_showUserDeleteConfirm = true;
		}

		protected void CloseUserDeleteConfirm()
		{
			_showUserDeleteConfirm = false;
			_userToDelete = null;
		}

		// Confirmă ștergerea logică (soft-delete) a utilizatorului selectat în modal — reutilizează același endpoint ca dezactivarea
		protected async Task ConfirmUserSoftDeleteAsync()
		{
			if (_userToDelete == null) return;
			_isProcessingUserDelete = true;
			try
			{
				var success = await UserApiClient.DeactivateUserAsync(_userToDelete.Id);
				if (success)
					_userToDelete.DeletedAt = DateTime.UtcNow;
				else
					ErrorMessage = "Failed to delete user.";
			}
			finally
			{
				_isProcessingUserDelete = false;
				CloseUserDeleteConfirm();
			}
		}

		protected void SetFilter(string filter)
		{
			StatusFilter = filter;
		}

		// Verifică dacă termenul de căutare se găsește în prenume, nume sau email (insensibil la majuscule)
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

		// Adminii nu sunt afișați în această listă (pagina e dedicată gestionării utilizatorilor obișnuiți)
		private static bool IsAdminRole(string? role)
		{
			return !string.IsNullOrWhiteSpace(role)
				&& role.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		// Generează inițialele utilizatorului (din prenume+nume) pentru avatar, cu fallback "US" dacă nu există nume
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

		protected void ViewUser(UserListItemDTO user)
		{
			NavigationManager.NavigateTo($"/view-selected-user/{user.Id}");
		}
	}
}
