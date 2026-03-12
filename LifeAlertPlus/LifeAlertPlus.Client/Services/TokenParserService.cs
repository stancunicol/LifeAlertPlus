using Microsoft.JSInterop;
using System.IdentityModel.Tokens.Jwt;

namespace LifeAlertPlus.Client.Services
{
    public record TokenClaims(
        Guid UserId,
        string Email,
        string FirstName,
        string LastName,
        string ProfilePictureUrl,
        string Provider
    );

    public class TokenParserService
    {
        private static readonly JwtSecurityTokenHandler _tokenHandler = new();
        private readonly IJSRuntime _jsRuntime;

        public TokenParserService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task<TokenClaims?> GetClaimsAsync()
        {
            try
            {
                var token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", new object[] { "authToken" });
                if (string.IsNullOrEmpty(token))
                    return null;

                var jsonToken = _tokenHandler.ReadJwtToken(token);

                var sub = jsonToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
                if (!Guid.TryParse(sub, out var userId))
                    return null;

                var email = jsonToken.Claims.FirstOrDefault(c =>
                    c.Type == JwtRegisteredClaimNames.Email || c.Type == "email")?.Value ?? string.Empty;
                var firstName = jsonToken.Claims.FirstOrDefault(c => c.Type == "firstName")?.Value ?? string.Empty;
                var lastName = jsonToken.Claims.FirstOrDefault(c => c.Type == "lastName")?.Value ?? string.Empty;
                var provider = jsonToken.Claims.FirstOrDefault(c => c.Type == "provider")?.Value ?? string.Empty;
                var profilePictureUrl = jsonToken.Claims.FirstOrDefault(c => c.Type == "profilePictureUrl")?.Value ?? string.Empty;

                var storedPicture = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", new object[] { "profilePictureUrl" });
                if (!string.IsNullOrEmpty(storedPicture))
                    profilePictureUrl = storedPicture;

                return new TokenClaims(userId, email, firstName, lastName, profilePictureUrl, provider);
            }
            catch
            {
                return null;
            }
        }
    }
}
