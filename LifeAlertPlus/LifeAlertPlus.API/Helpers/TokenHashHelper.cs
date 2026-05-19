using System.Security.Cryptography;
using System.Text;

namespace LifeAlertPlus.API.Helpers
{
    internal static class TokenHashHelper
    {
        internal static string ComputeSha256(string rawToken)
        {
            var bytes = Encoding.UTF8.GetBytes(rawToken);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
    }
}
