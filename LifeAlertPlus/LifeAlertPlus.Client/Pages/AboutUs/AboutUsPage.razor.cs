using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Pages.AboutUs;

// Code-behind pentru pagina "Despre noi" — afișează și numele/poza utilizatorului autentificat în antet
public partial class AboutUsPage : ComponentBase
{
    [Inject]
    private TokenParserService TokenParserService { get; set; } = default!;

    [Inject]
    private UserApiClient UserApiClient { get; set; } = default!;

    [Inject]
    private LanguageService Lang { get; set; } = default!;

    private string T(string key) => Lang.T(key);

    private string UserFullName = "";
    private string ProfilePictureUrl = "";

    protected override async Task OnInitializedAsync()
    {
        // Citește mai întâi datele de bază din claims-urile token-ului (rapid, fără apel API)
        var claims = await TokenParserService.GetClaimsAsync();
        if (claims != null)
        {
            UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
            ProfilePictureUrl = claims.ProfilePictureUrl;

            // Apoi suprascrie cu datele proaspete din API, dacă sunt disponibile (claims-urile pot fi vechi/incomplete)
            var userProfile = await UserApiClient.GetUserByIdAsync(claims.UserId);
            if (userProfile != null)
            {
                var apiName = $"{userProfile.FirstName} {userProfile.LastName}".Trim();
                if (!string.IsNullOrWhiteSpace(apiName))
                    UserFullName = apiName;
                if (!string.IsNullOrWhiteSpace(userProfile.ProfilePictureUrl))
                    ProfilePictureUrl = userProfile.ProfilePictureUrl;
            }
        }
        else
        {
            // Fallback dacă utilizatorul nu e autentificat sau token-ul nu poate fi decodat
            UserFullName = "User";
        }
    }
}
