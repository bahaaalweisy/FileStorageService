using System.Text;

namespace FileStorage.Application.Common;

public static class FilenameSanitizer
{
    public const string FallbackName = "file";

    public static string Sanitize(string? rawName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return FallbackName;
        }

        var name = rawName.Replace('\\', '/');
        var lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            name = name[(lastSlash + 1)..];
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c))
            {
                continue;
            }

            if (c is '"' or '<' or '>' or '|' or ':' or '*' or '?')
            {
                continue;
            }

            sb.Append(c);
        }

        var cleaned = sb.ToString().Trim().TrimStart('.');

        if (cleaned.Length == 0)
        {
            return FallbackName;
        }

        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength];
        }

        return cleaned;
    }

    public static string ToContentDispositionValue(string sanitizedName)
    {
        var asciiFallback = new string(sanitizedName.Select(c => c <= 127 ? c : '_').ToArray());
        if (asciiFallback.Length == 0)
        {
            asciiFallback = FallbackName;
        }

        var encoded = Uri.EscapeDataString(sanitizedName);
        return $"attachment; filename=\"{asciiFallback}\"; filename*=UTF-8''{encoded}";
    }
}
