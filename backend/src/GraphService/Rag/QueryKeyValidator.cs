using System.Security.Cryptography;
using System.Text;

namespace GraphService.Rag;

/// <summary>Constant-time check of the internal graph query key; empty configured or provided keys never match.</summary>
public static class QueryKeyValidator
{
    public static bool IsValid(string? provided, string? expected)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
