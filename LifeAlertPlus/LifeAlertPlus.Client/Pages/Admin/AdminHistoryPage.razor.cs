using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components;
using static LifeAlertPlus.Client.Services.AdminApiClient;

namespace LifeAlertPlus.Client.Pages.Admin;

// Code-behind pentru pagina de admin "Istoric" — afișează jurnalul de audit (AuditEntry) cu paginare,
// căutare text și filtrare pe categorie (ex: Account/System)
public partial class AdminHistoryPage : ComponentBase
{
	[Inject]
	private TokenParserService TokenParser { get; set; } = default!;

	[Inject]
	private LanguageService Lang { get; set; } = default!;

	[Inject]
	private NavigationManager Navigation { get; set; } = default!;

	[Inject]
	private AdminApiClient AdminApi { get; set; } = default!;

	private string T(string key) => Lang.TEnglish(key);

	protected string UserFullName { get; set; } = "Admin";
	protected string ProfilePictureUrl { get; set; } = string.Empty;
	protected bool IsLoading { get; set; } = true;
	protected string? ErrorMessage { get; set; }
	private string _searchText = string.Empty;
	// La modificarea textului de căutare, resetează paginarea la prima pagină (rezultatele filtrate se schimbă)
	protected string SearchText
	{
		get => _searchText;
		set { _searchText = value; HistoryPage = 1; }
	}
	protected string ActionFilter { get; set; } = "all";
	protected List<AuditEntry> Entries { get; set; } = new();

	// Contoare agregate pentru cardurile de sumar din pagină
	protected int TotalEvents => Entries.Count;
	protected int AccountEvents => Entries.Count(e => e.Category == "Account");
	protected int SystemEvents => Entries.Count(e => e.Category == "System");
	protected int DeleteActions => Entries.Count(e => e.Action.Contains("Delete", StringComparison.OrdinalIgnoreCase));

	protected int HistoryPage { get; set; } = 1;
	protected int HistoryPageSize { get; set; } = 20;

	// Aplică filtrul de text (căutare pe utilizator/acțiune/detalii) și filtrul de categorie, sortat descrescător după timp
	protected IEnumerable<AuditEntry> FilteredEntries => Entries
		.Where(e => string.IsNullOrWhiteSpace(SearchText) || e.User.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || e.Action.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || e.Details.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
		.Where(e => ActionFilter == "all" || e.Category == ActionFilter)
		.OrderByDescending(e => e.Timestamp);

	// Extrage doar elementele paginii curente din rezultatele deja filtrate
	protected IEnumerable<AuditEntry> PagedEntries => FilteredEntries
		.Skip((HistoryPage - 1) * HistoryPageSize)
		.Take(HistoryPageSize);

	protected int HistoryTotalPages => Math.Max(1, (int)Math.Ceiling(FilteredEntries.Count() / (double)HistoryPageSize));

	// Navighează la o pagină din istoric, doar dacă numărul este în intervalul valid
	protected void GoToHistoryPage(int page)
	{
		if (page >= 1 && page <= HistoryTotalPages)
			HistoryPage = page;
	}

	protected override async Task OnInitializedAsync()
	{
		await LoadUserFromTokenAsync();
		await LoadEntriesAsync();
	}

	// Încarcă datele utilizatorului admin curent din token, pentru afișare în antet; redirecționează la login dacă nu e autentificat
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

	// Cere de la API ultimele 200 de intrări din jurnalul de audit
	protected async Task LoadEntriesAsync()
	{
		IsLoading = true;
		ErrorMessage = null;
		try
		{
			var raw = await AdminApi.GetAuditLogAsync(200);
			Entries = raw.Select(e => new AuditEntry(e.Id, e.Timestamp, e.User, e.Action, e.Details, e.Category)).ToList();
		}
		catch (Exception ex)
		{
			ErrorMessage = $"Failed to load history: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}

	// Setează categoria activă de filtrare și resetează paginarea la prima pagină
	protected void SetFilter(string filter)
	{
		ActionFilter = filter;
		HistoryPage = 1;
	}

	// Returnează clasa CSS corespunzătoare butonului de filtru, marcându-l ca activ dacă e selectat
	protected string GetFilterClass(string filter)
	{
		return ActionFilter == filter ? "filter-btn active" : "filter-btn";
	}

	// Formatează data în ora locală a browserului, pentru afișare în tabel
	protected string FormatDate(DateTime timestamp)
	{
		return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
	}

	protected record AuditEntry(Guid Id, DateTime Timestamp, string User, string Action, string Details, string Category);
}
