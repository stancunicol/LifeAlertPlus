using Microsoft.JSInterop;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Services
{
    public record TokenClaims(
        Guid UserId,
        string Email,
        string FirstName,
        string LastName,
        string ProfilePictureUrl,
        string Provider,
        string Role,
        bool NeedsConsent = false
    );

    public class TokenParserService
    {
        private static readonly JwtSecurityTokenHandler _tokenHandler = new();
        private readonly IJSRuntime _jsRuntime;
        private readonly NavigationManager _navigation;

        public TokenParserService(IJSRuntime jsRuntime, NavigationManager navigation)
        {
            _jsRuntime = jsRuntime;
            _navigation = navigation;
        }

        public async Task<TokenClaims?> GetClaimsAsync()
        {
            try
            {
                var token = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", new object[] { "authToken" });

                if (string.IsNullOrWhiteSpace(token))
                {
                    var currentUri = _navigation.ToAbsoluteUri(_navigation.Uri);
                    if (!string.IsNullOrWhiteSpace(currentUri.Fragment))
                    {
                        var tokenFromFragment = TryGetQueryParameter(currentUri.Fragment.TrimStart('#'), "token");
                        if (!string.IsNullOrWhiteSpace(tokenFromFragment))
                        {
                            token = tokenFromFragment;
                            await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", "authToken", token);

                            // Remove token from URL to avoid re-processing it on every navigation.
                            _navigation.NavigateTo(currentUri.GetLeftPart(UriPartial.Path), replace: true);
                        }
                    }
                }

                if (string.IsNullOrEmpty(token))
                    return null;

                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = token.Substring("Bearer ".Length).Trim();

                var jsonToken = _tokenHandler.ReadJwtToken(token);

                if (jsonToken.ValidTo != DateTime.MinValue && jsonToken.ValidTo < DateTime.UtcNow)
                {
                    await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "authToken");
                    await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "profilePictureUrl");
                    return null;
                }

                var sub = jsonToken.Claims.FirstOrDefault(c =>
                    c.Type == JwtRegisteredClaimNames.Sub ||
                    c.Type == ClaimTypes.NameIdentifier ||
                    c.Type == "nameid")?.Value;
                if (!Guid.TryParse(sub, out var userId))
                    return null;

                var email = jsonToken.Claims.FirstOrDefault(c =>
                    c.Type == JwtRegisteredClaimNames.Email ||
                    c.Type == "email" ||
                    c.Type == ClaimTypes.Email)?.Value ?? string.Empty;

                var firstName = jsonToken.Claims.FirstOrDefault(c =>
                    c.Type == "firstName" ||
                    c.Type == ClaimTypes.GivenName ||
                    c.Type == "given_name")?.Value ?? string.Empty;

                var lastName = jsonToken.Claims.FirstOrDefault(c =>
                    c.Type == "lastName" ||
                    c.Type == ClaimTypes.Surname ||
                    c.Type == "family_name")?.Value ?? string.Empty;

                if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
                {
                    var fullName = jsonToken.Claims.FirstOrDefault(c =>
                        c.Type == "name" ||
                        c.Type == ClaimTypes.Name)?.Value;

                    if (!string.IsNullOrWhiteSpace(fullName))
                    {
                        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length > 0)
                        {
                            firstName = parts[0];
                            if (parts.Length > 1)
                                lastName = string.Join(' ', parts.Skip(1));
                        }
                    }
                }

                var provider = jsonToken.Claims.FirstOrDefault(c => c.Type == "provider")?.Value ?? string.Empty;

                var role = jsonToken.Claims
                    .Where(c => c.Type == ClaimTypes.Role || c.Type.Equals("role", StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Value)
                    .FirstOrDefault() ?? string.Empty;
                var profilePictureUrl = jsonToken.Claims.FirstOrDefault(c => c.Type == "profilePictureUrl")?.Value ?? string.Empty;

                var storedPicture = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", new object[] { "profilePictureUrl" });
                if (string.IsNullOrEmpty(profilePictureUrl) && !string.IsNullOrEmpty(storedPicture))
                    profilePictureUrl = storedPicture;

                var needsConsentStr = jsonToken.Claims.FirstOrDefault(c => c.Type == "needsConsent")?.Value;
                var needsConsent = string.Equals(needsConsentStr, "true", StringComparison.OrdinalIgnoreCase);

                return new TokenClaims(userId, email, firstName, lastName, profilePictureUrl, provider, role, needsConsent);
            }
            catch
            {
                return null;
            }
        }

        private static string? TryGetQueryParameter(string query, string key)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pair in pairs)
            {
                var tokens = pair.Split('=', 2);
                if (tokens.Length == 0)
                    continue;

                var currentKey = Uri.UnescapeDataString(tokens[0]);
                if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (tokens.Length < 2)
                    return string.Empty;

                return Uri.UnescapeDataString(tokens[1]);
            }

            return null;
        }
    }
}
