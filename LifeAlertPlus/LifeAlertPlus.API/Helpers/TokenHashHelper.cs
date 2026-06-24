using System.Security.Cryptography;
using System.Text;

namespace LifeAlertPlus.API.Helpers
{
    // Utilitar intern pentru hash-uirea token-urilor de securitate
    // Folosit pentru a stoca token-urile în DB sub formă de hash (nu în clar)
    // Dacă DB-ul e compromis, atacatorul nu poate folosi hash-urile direct
    internal static class TokenHashHelper
    {
        // Calculează hash-ul SHA-256 al unui token și îl returnează ca string hexazecimal
        // Exemplu: "abc123" → "6ca13d52ca70c883e0f0bb101e425a89e8624de51db2d2392593af6a84118090"
        internal static string ComputeSha256(string rawToken)
        {
            var bytes = Encoding.UTF8.GetBytes(rawToken); // Convertim string-ul în bytes
            return Convert.ToHexString(SHA256.HashData(bytes)); // Hash SHA-256 → string hex uppercase
        }
    }
}
