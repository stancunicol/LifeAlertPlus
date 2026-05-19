using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Pages.Support;

public partial class SupportPage : ComponentBase
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
        var claims = await TokenParserService.GetClaimsAsync();
        if (claims != null)
        {
            UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
            ProfilePictureUrl = claims.ProfilePictureUrl;

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
            UserFullName = "User";
        }
    }
}
