using System.Security.Cryptography;
using System.Text;

namespace DayTrace.Domain.Utilities;

/// <summary>
/// Shared cryptographic utility methods.
/// </summary>
public static class CryptoUtils
{
    /// <summary>
    /// Computes SHA256 hash of the input string and returns a lowercase hex string.
    /// </summary>
    public static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
