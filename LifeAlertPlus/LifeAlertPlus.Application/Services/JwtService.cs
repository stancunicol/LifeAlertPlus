using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LifeAlertPlus.Application.Services
{
    public class JwtService : IJwtService
    {
        private readonly IConfiguration _config;
        private static readonly JwtSecurityTokenHandler _tokenHandler = new();

        public JwtService(IConfiguration config)
        {
            _config = config;
        }

        public string GenerateToken(User user, string roleName)
        {
            var jwtKey = _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
                throw new InvalidOperationException("Missing Jwt:Key configuration.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("firstName", user.FirstName),
                new Claim("lastName", user.LastName),
                new Claim("provider", user.Provider ?? "Local"),
                new Claim(ClaimTypes.Role, roleName),
                new Claim("role", roleName),
                new Claim("lastChangedPasswordAt", user.LastChangedPasswordAt?.ToString("o") ?? string.Empty),
                new Claim("profilePictureUrl", user.ProfilePictureUrl ?? string.Empty)
            };

            int.TryParse(_config["Jwt:ExpiresInMinutes"], out var expiresInMinutes);
            if (expiresInMinutes <= 0) expiresInMinutes = 60;

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiresInMinutes),
                signingCredentials: creds
            );

            return _tokenHandler.WriteToken(token);
        }
    }
}
