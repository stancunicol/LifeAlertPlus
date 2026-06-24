using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components;
using static LifeAlertPlus.Client.Services.AdminApiClient;

namespace LifeAlertPlus.Client.Pages.Admin;

// Code-behind pentru pagina de admin "Error Log" — listează, filtrează și caută în erorile/avertismentele
// logate de aplicație (returnate de AdminApiClient.GetErrorLogAsync)
public partial class AdminErrorLogPage : ComponentBase
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
	protected string SearchText { get; set; } = string.Empty;
	protected string LevelFilter { get; set; } = "all";
	protected List<ErrorLogEntry> Entries { get; set; } = new();

	// Contoare agregate folosite pentru cardurile de sumar din pagină
	protected int ErrorCount => Entries.Count(e => e.Level == "Error");
	protected int WarningCount => Entries.Count(e => e.Level == "Warning");
	protected int InfoCount => Entries.Count(e => e.Level == "Info");
	protected int Recent24hCount => Entries.Count(e => e.Timestamp >= DateTime.UtcNow.AddHours(-24));

	// Aplică filtrul de text (căutare pe sursă/mesaj/detalii) și filtrul de nivel, sortat descrescător după timp
	protected IEnumerable<ErrorLogEntry> FilteredEntries => Entries
		.Where(e => string.IsNullOrWhiteSpace(SearchText) || e.Source.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || e.Details.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
		.Where(e => LevelFilter == "all" || e.Level == LevelFilter)
		.OrderByDescending(e => e.Timestamp);

	protected override async Task OnInitializedAsync()
	{
		await LoadUserFromTokenAsync();
		await LoadErrorsAsync();
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

	// Cere de la API ultimele 200 de intrări din log-ul de erori al aplicației
	protected async Task LoadErrorsAsync()
	{
		IsLoading = true;
		ErrorMessage = null;
		try
		{
			var raw = await AdminApi.GetErrorLogAsync(200);
			Entries = raw.Select(e => new ErrorLogEntry(e.Timestamp, e.Level, e.Source, e.Message, e.Details)).ToList();
		}
		catch (Exception ex)
		{
			ErrorMessage = $"Failed to load error log: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}

	// Setează nivelul activ de filtrare (all/Error/Warning/Info) la click pe butonul de filtru
	protected void SetFilter(string filter)
	{
		LevelFilter = filter;
	}

	// Returnează clasa CSS corespunzătoare butonului de filtru, marcându-l ca activ dacă e selectat
	protected string GetFilterClass(string filter)
	{
		return LevelFilter == filter ? "filter-btn active" : "filter-btn";
	}

	// Formatează data în ora locală a browserului, pentru afișare în tabel
	protected string FormatDate(DateTime timestamp)
	{
		return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
	}

	protected record ErrorLogEntry(DateTime Timestamp, string Level, string Source, string Message, string Details);
}
