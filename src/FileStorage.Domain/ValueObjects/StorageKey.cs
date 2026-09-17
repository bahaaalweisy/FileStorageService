using System.Security.Cryptography;

namespace FileStorage.Domain.ValueObjects;

public static class StorageKey
{
    public const int Length = 22;

    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
